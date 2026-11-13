using System;

namespace SixtarGateMod.Managers;

/// <summary>
/// 람다 본문 대조(<see cref="NativeLambdaScan"/>)용 최소 x64 명령 길이 해독기. IL2CPP 의존 없는 순수 함수.
///
/// 2026-11-12까지의 엄격 스캔은 바이트를 하나씩 밀며 <c>mov</c>만 골라 읽어서, 다른 명령의 변위·즉값 바이트를
/// <c>mov</c>로 잘못 읽을 수 있었고 <c>lea</c>/<c>call</c>/<c>xor</c>/<c>pop</c>이 레지스터를 덮는 것도 몰랐다
/// (예: BGA 람다는 <c>lea rcx,[rip+…]</c> → <c>call</c>로 rcx를 덮은 뒤 rbx에 둔 클로저를 씀).
/// 이 해독기로 명령 경계를 따라 걸으며 각 명령이 어느 레지스터에 쓰는지 알려 준다.
///
/// 범위: MSVC가 IL2CPP 람다에 내는 범용 정수·SSE·VEX 명령. 모르는 명령이면 길이 0 → 호출자는 거기서 멈춘다
/// (틀린 길이로 계속 가지 않음). 길이는 tools/lambdascan에서 capstone 결과와 대조해 시험.
/// </summary>
public static class X64Decoder
{
    public enum Kind
    {
        Other,
        /// <summary>MOV r64, r/m64 (8B, REX.W) — 레지스터 추적에서 따로 처리.</summary>
        MovLoad64,
        /// <summary>MOV r/m64, r64 (89, REX.W).</summary>
        MovStore64,
        Call,
        Ret,
        Jmp,
    }

    public struct Insn
    {
        public int Length;
        public Kind Kind;
        public bool HasModRm;
        public int Mod, Reg, Rm;        // REX 확장 반영(0~15)
        public int BaseReg;             // 메모리 피연산자의 기준 레지스터(-1: RIP/없음)
        public int Disp;                // 메모리 피연산자의 변위
        /// <summary>이 명령이 덮어쓰는 범용 레지스터 비트(정확하지 않으면 보수적으로 넓게).</summary>
        public int Writes;
    }

    public const int VolatileRegs = (1 << 0) | (1 << 1) | (1 << 2) | (1 << 8) | (1 << 9) | (1 << 10) | (1 << 11); // rax rcx rdx r8-r11

    /// <summary><paramref name="code"/>의 <paramref name="at"/>에서 명령 하나를 해독. 모르면 false.</summary>
    public static bool TryDecode(ReadOnlySpan<byte> code, int at, out Insn insn)
    {
        insn = default;
        insn.BaseReg = -1;
        int i = at;
        bool opSize16 = false;
        int rex = 0;

        // 접두사(그룹 1~4) — REX는 그 뒤, 옵코드 바로 앞
        for (int n = 0; n < 8; n++)
        {
            if (i >= code.Length) return false;
            byte p = code[i];
            if (p == 0x66) { opSize16 = true; i++; continue; }
            if (p == 0xF2 || p == 0xF3 || p == 0xF0 || p == 0x67 ||
                p == 0x2E || p == 0x3E || p == 0x26 || p == 0x36 || p == 0x64 || p == 0x65) { i++; continue; }
            break;
        }
        if (i < code.Length && (code[i] & 0xF0) == 0x40) { rex = code[i]; i++; }
        if (i >= code.Length) return false;

        bool w = (rex & 8) != 0;
        int rexR = (rex >> 2) & 1, rexX = (rex >> 1) & 1, rexB = rex & 1;
        byte op = code[i++];

        int imm = 0;            // 즉값 바이트 수
        bool modrm = false;
        int regWritesDst = 0;   // 1: reg 필드가 목적지, 2: r/m(mod==3)이 목적지
        int opRegLow = op & 7;

        switch (op)
        {
            // 0F 두 바이트 옵코드
            case 0x0F:
                return DecodeTwoByte(code, at, i, rexR, rexX, rexB, w, ref insn);
            case 0xC4:
            case 0xC5:
                return DecodeVex(code, at, i - 1, ref insn);

            // ALU r/m, r (목적지 r/m) / ALU r, r/m (목적지 reg)
            case 0x00: case 0x01: case 0x08: case 0x09: case 0x10: case 0x11: case 0x18: case 0x19:
            case 0x20: case 0x21: case 0x28: case 0x29: case 0x30: case 0x31:
            case 0x88: case 0x89:
                modrm = true; regWritesDst = 2; break;
            case 0x02: case 0x03: case 0x0A: case 0x0B: case 0x12: case 0x13: case 0x1A: case 0x1B:
            case 0x22: case 0x23: case 0x2A: case 0x2B: case 0x32: case 0x33:
            case 0x8A: case 0x8B: case 0x8D: case 0x63:
                modrm = true; regWritesDst = 1; break;
            case 0x38: case 0x39: case 0x3A: case 0x3B: case 0x84: case 0x85: // cmp/test: 쓰기 없음
                modrm = true; break;
            case 0x86: case 0x87: // xchg: 둘 다
                modrm = true; regWritesDst = 3; break;
            case 0x69: modrm = true; regWritesDst = 1; imm = opSize16 ? 2 : 4; break;
            case 0x6B: modrm = true; regWritesDst = 1; imm = 1; break;
            case 0x80: case 0x82: case 0x83: case 0xC0: case 0xC1: case 0xC6:
                modrm = true; regWritesDst = 2; imm = 1; break;
            case 0x81: case 0xC7:
                modrm = true; regWritesDst = 2; imm = opSize16 ? 2 : 4; break;
            case 0xD0: case 0xD1: case 0xD2: case 0xD3: case 0xFE:
                modrm = true; regWritesDst = 2; break;
            case 0x8F: modrm = true; regWritesDst = 2; break; // pop r/m
            case 0xF6: case 0xF7:
            case 0xFF:
                modrm = true; break; // /reg에 따라 아래에서 처리

            // ModRM 없는 것
            case 0xC3: insn.Kind = Kind.Ret; break;
            case 0xCC: case 0x90: case 0x98: case 0x99: case 0xC9: case 0xF4: break;
            case 0xC2: imm = 2; insn.Kind = Kind.Ret; break;
            case 0xE8: imm = 4; insn.Kind = Kind.Call; break;
            case 0xE9: imm = 4; insn.Kind = Kind.Jmp; break;
            case 0xEB: imm = 1; insn.Kind = Kind.Jmp; break;
            case 0x68: imm = 4; break;
            case 0x6A: imm = 1; break;
            case 0xA8: imm = 1; break;
            case 0xA9: imm = opSize16 ? 2 : 4; break;
            case 0x04: case 0x0C: case 0x14: case 0x1C: case 0x24: case 0x2C: case 0x34: case 0x3C:
                imm = 1; insn.Writes = 1 << 0; break;
            case 0x05: case 0x0D: case 0x15: case 0x1D: case 0x25: case 0x2D: case 0x35:
                imm = opSize16 ? 2 : 4; insn.Writes = 1 << 0; break;
            case 0x3D: imm = opSize16 ? 2 : 4; break; // cmp eax, imm
            // 문자열 명령(rep movs/stos/lods/scas/cmps): rdi·rsi·rcx·rax를 덮었다고 본다
            case 0xA4: case 0xA5: case 0xA6: case 0xA7: case 0xAA: case 0xAB: case 0xAC: case 0xAD: case 0xAE: case 0xAF:
                insn.Writes = (1 << 0) | (1 << 1) | (1 << 6) | (1 << 7); break;
            default:
                if (op >= 0x70 && op <= 0x7F) { imm = 1; break; }          // jcc rel8
                if (op >= 0x50 && op <= 0x57) break;                        // push
                if (op >= 0x58 && op <= 0x5F) { insn.Writes = 1 << (opRegLow | (rexB << 3)); break; } // pop
                if (op >= 0x91 && op <= 0x97) { insn.Writes = 1 | (1 << (opRegLow | (rexB << 3))); break; } // xchg eax, r
                if (op >= 0xB0 && op <= 0xB7) { imm = 1; insn.Writes = 1 << (opRegLow | (rexB << 3)); break; }
                if (op >= 0xB8 && op <= 0xBF) { imm = w ? 8 : (opSize16 ? 2 : 4); insn.Writes = 1 << (opRegLow | (rexB << 3)); break; }
                return false;
        }

        if (modrm)
        {
            if (!ReadModRm(code, ref i, rexR, rexX, rexB, ref insn)) return false;
            if (op == 0xF6 || op == 0xF7)
            {
                int sub = insn.Reg & 7;
                if (sub <= 1) imm = op == 0xF6 ? 1 : (opSize16 ? 2 : 4); // test r/m, imm
                else if (sub == 2 || sub == 3) regWritesDst = 2;          // not/neg
                else insn.Writes |= (1 << 0) | (1 << 2);                  // mul/div → rax, rdx
            }
            else if (op == 0xFF)
            {
                int sub = insn.Reg & 7;
                if (sub <= 1) regWritesDst = 2;                 // inc/dec
                else if (sub == 2 || sub == 3) insn.Kind = Kind.Call;
                else if (sub == 4 || sub == 5) insn.Kind = Kind.Jmp;
            }
            else if (op == 0x80 || op == 0x81 || op == 0x83)
            {
                if ((insn.Reg & 7) == 7) regWritesDst = 0;      // cmp r/m, imm
            }

            if ((regWritesDst & 1) != 0) insn.Writes |= 1 << insn.Reg;
            if ((regWritesDst & 2) != 0 && insn.Mod == 3) insn.Writes |= 1 << insn.Rm;

            if (op == 0x8B && w) insn.Kind = Kind.MovLoad64;
            else if (op == 0x89 && w) insn.Kind = Kind.MovStore64;
        }

        i += imm;
        if (i > code.Length) return false;
        insn.Length = i - at;
        return true;
    }

    private static bool ReadModRm(ReadOnlySpan<byte> code, ref int i, int rexR, int rexX, int rexB, ref Insn insn)
    {
        if (i >= code.Length) return false;
        byte m = code[i++];
        insn.HasModRm = true;
        insn.Mod = m >> 6;
        insn.Reg = ((m >> 3) & 7) | (rexR << 3);
        int rmLow = m & 7;
        insn.Rm = rmLow | (rexB << 3);
        if (insn.Mod == 3) return true;

        insn.BaseReg = insn.Rm;
        if (rmLow == 4)
        {
            if (i >= code.Length) return false;
            byte sib = code[i++];
            int baseLow = sib & 7;
            insn.BaseReg = baseLow | (rexB << 3);
            if (insn.Mod == 0 && baseLow == 5)
            {
                insn.BaseReg = -1;
                return ReadDisp(code, ref i, 4, ref insn);
            }
        }
        else if (insn.Mod == 0 && rmLow == 5)
        {
            insn.BaseReg = -1; // RIP 상대
            return ReadDisp(code, ref i, 4, ref insn);
        }
        if (insn.Mod == 1) return ReadDisp(code, ref i, 1, ref insn);
        if (insn.Mod == 2) return ReadDisp(code, ref i, 4, ref insn);
        return true;
    }

    private static bool ReadDisp(ReadOnlySpan<byte> code, ref int i, int size, ref Insn insn)
    {
        if (i + size > code.Length) return false;
        insn.Disp = size == 1 ? (sbyte)code[i] : code[i] | (code[i + 1] << 8) | (code[i + 2] << 16) | (code[i + 3] << 24);
        i += size;
        return true;
    }

    private static bool DecodeTwoByte(ReadOnlySpan<byte> code, int at, int i, int rexR, int rexX, int rexB, bool w, ref Insn insn)
    {
        if (i >= code.Length) return false;
        byte op = code[i++];
        int imm = 0;
        bool modrm = true;
        bool writesReg = false;

        if (op >= 0x80 && op <= 0x8F) { modrm = false; imm = 4; }                          // jcc rel32
        else if (op == 0x77) { modrm = false; }                                             // emms
        else if (op >= 0xC8 && op <= 0xCF) { modrm = false; insn.Writes = 1 << ((op & 7) | (rexB << 3)); } // bswap
        else if (op == 0x05 || op == 0x0B || op == 0xA2 || op == 0x31) { modrm = false; insn.Writes = op == 0x05 || op == 0x0B ? 0 : 0b1111; }
        else if (op == 0x38) { if (i >= code.Length) return false; i++; writesReg = true; }  // 0F 38 xx /r
        else if (op == 0x3A) { if (i >= code.Length) return false; i++; imm = 1; }          // 0F 3A xx /r ib
        else if (op == 0x70 || op == 0x71 || op == 0x72 || op == 0x73 || op == 0xC2 || op == 0xC4 || op == 0xC5 || op == 0xC6 || op == 0xBA || op == 0xA4 || op == 0xAC)
            imm = 1;
        else if ((op >= 0x40 && op <= 0x4F) || op == 0xAF || op == 0xB6 || op == 0xB7 || op == 0xBE || op == 0xBF || op == 0xBC || op == 0xBD || op == 0xB8)
            writesReg = true;                                                                 // cmov/imul/movzx/movsx/bsf/bsr/popcnt
        else if (op == 0x10 || op == 0x11 || op == 0x12 || op == 0x13 || op == 0x14 || op == 0x15 || op == 0x16 || op == 0x17 ||
                 op == 0x18 || op == 0x1E || op == 0x1F || op == 0x0D || (op >= 0x28 && op <= 0x2F) || (op >= 0x50 && op <= 0x7F) ||
                 (op >= 0x90 && op <= 0x9F) || op == 0xA3 || op == 0xAB || op == 0xB3 || op == 0xBB || op == 0xB0 || op == 0xB1 ||
                 op == 0xC0 || op == 0xC1 || op == 0xD0 || (op >= 0xD1 && op <= 0xFE))
        {
            // SSE/setcc/bt/xadd 등. 범용 레지스터에 쓰는 것(movd/movq r, xmm = 0F 7E, cvt*2si = 0F 2C/2D, pmovmskb 0F D7,
            // movmskps 0F 50, setcc 0F 9x)은 보수적으로 reg/rm 둘 다 덮였다고 본다.
            if (op == 0x7E || op == 0x2C || op == 0x2D || op == 0xD7 || op == 0x50 || (op >= 0x90 && op <= 0x9F) ||
                op == 0xC0 || op == 0xC1 || op == 0xB0 || op == 0xB1)
                writesReg = true;
        }
        else return false;

        if (modrm)
        {
            if (!ReadModRm(code, ref i, rexR, rexX, rexB, ref insn)) return false;
            if (writesReg)
            {
                insn.Writes |= 1 << insn.Reg;
                if (insn.Mod == 3) insn.Writes |= 1 << insn.Rm;
            }
        }
        i += imm;
        if (i > code.Length) return false;
        insn.Length = i - at;
        return true;
    }

    /// <summary>VEX(C4/C5) — AVX 명령. 범용 레지스터에 쓰는 일은 드묾(vmovd r32 등)이라 보수적으로 reg/rm을 덮었다고 본다.</summary>
    private static bool DecodeVex(ReadOnlySpan<byte> code, int at, int vexAt, ref Insn insn)
    {
        int i = vexAt;
        byte v = code[i++];
        int map = 1;
        int rexR, rexB;
        if (v == 0xC5)
        {
            if (i >= code.Length) return false;
            rexR = (~code[i] >> 7) & 1; rexB = 0;
            i++;
        }
        else
        {
            if (i + 1 >= code.Length) return false;
            rexR = (~code[i] >> 7) & 1; rexB = (~code[i] >> 5) & 1;
            map = code[i] & 0x1F;
            i += 2;
        }
        if (i >= code.Length) return false;
        i++; // 옵코드
        if (!ReadModRm(code, ref i, rexR, 0, rexB, ref insn)) return false;
        if (map == 3) i += 1; // 0F 3A 계열 ib
        if (i > code.Length) return false;
        insn.Writes |= 1 << insn.Reg;
        if (insn.Mod == 3) insn.Writes |= 1 << insn.Rm;
        insn.Length = i - at;
        return true;
    }
}
