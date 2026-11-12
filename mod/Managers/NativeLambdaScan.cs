using System;

namespace SixtarGateMod.Managers;

/// <summary>
/// 람다 본문 x64 기계어에서 "클로저 → 소유자(<c>&lt;&gt;4__this</c>) → 대상 필드" 로드 사슬을 찾는다.
/// IL2CPP에 의존하지 않는 순수 함수라 실제 GameAssembly.dll 바이트로 오프라인 시험할 수 있다
/// (docs/03-development/REVIEW_2026-11-12.md).
///
/// 이 빌드(Windows x64 MSVC)에서 관찰한 모양:
/// <code>
///   mov rcx, [rcx+0x18]   ; 클로저.&lt;&gt;4__this
///   mov rcx, [rcx+0x30]   ; 소유자.jacketFull
///   jmp Image.set_sprite
/// </code>
/// 또는 클로저 포인터를 먼저 보존 레지스터로 옮기는 모양(<c>mov rbx, rcx</c> → <c>mov rax, [rbx+0x18]</c> → <c>mov rcx, [rax+0x28]</c>).
///
/// 엄격 모드는 레지스터를 따라간다: 대상 필드 로드의 기준 레지스터가 실제로 소유자를 담고 있어야 맞음.
/// 느슨 모드(2026-11-07 ~ 11-11의 방식)는 레지스터를 보지 않고 변위만 순서대로 맞춘다 — 엄격 모드가 아무 후보도
/// 못 맞혔을 때만 쓴다.
/// </summary>
public static class NativeLambdaScan
{
    public enum Result
    {
        /// <summary>소유자를 거쳐 대상 필드를 읽는 명령을 찾음.</summary>
        Hit,
        /// <summary>소유자 로드는 보였지만 대상 필드 로드는 없음(다른 필드를 쓰는 람다일 가능성이 큼).</summary>
        OwnerOnly,
        /// <summary>소유자 로드조차 못 봄(본문이 길거나 모양이 다름) — 판단 불가.</summary>
        Inconclusive,
    }

    /// <summary>
    /// 기본 스캔 길이. 자켓 람다는 0x30바이트, BGA 람다의 대상 필드 로드는 0x39바이트 지점. 첫 int3 2개(함수 끝 패딩)에서 멈춘다.
    /// </summary>
    public const int DefaultScanBytes = 192;

    private const int Rcx = 1;

    /// <param name="code">람다 네이티브 본문(앞에서부터).</param>
    /// <param name="ownerPtrOffset">클로저 안 <c>&lt;&gt;4__this</c> 필드 오프셋. 0 이하면 람다가 소유자 형식 자신의
    /// 인스턴스 메서드라 진입 시 rcx가 곧 소유자.</param>
    /// <param name="targetFieldOffset">소유자의 대상 필드 오프셋(il2cpp_field_get_offset).</param>
    /// <param name="trackRegisters">true면 엄격 모드(레지스터 추적), false면 느슨 모드.</param>
    public static Result Scan(ReadOnlySpan<byte> code, int ownerPtrOffset, int targetFieldOffset, bool trackRegisters = true)
    {
        if (targetFieldOffset <= 0) return Result.Inconclusive;
        return trackRegisters
            ? ScanStrict(code, ownerPtrOffset, targetFieldOffset)
            : ScanLoose(code, ownerPtrOffset, targetFieldOffset);
    }

    private static Result ScanStrict(ReadOnlySpan<byte> code, int ownerPtrOffset, int targetFieldOffset)
    {
        // 레지스터 16개를 비트로: closure = 클로저 포인터를 담은 레지스터, owner = 소유자 포인터를 담은 레지스터
        int closure = 0, owner = 0;
        bool sawOwner;
        if (ownerPtrOffset > 0) { closure = 1 << Rcx; sawOwner = false; }
        else { owner = 1 << Rcx; sawOwner = true; }

        for (int i = 0; i + 2 < code.Length; i++)
        {
            byte b = code[i];
            if (b == 0xCC && code[i + 1] == 0xCC) break; // 함수 끝 int3 패딩

            // REX.W(0x48~0x4F) + MOV r64, r/m64 (8B) 또는 MOV r/m64, r64 (89)
            if ((b & 0xF8) != 0x48) continue;
            byte op = code[i + 1];
            if (op != 0x8B && op != 0x89) continue;

            int rexR = (b >> 2) & 1, rexB = b & 1;
            byte modRm = code[i + 2];
            int mod = modRm >> 6;
            int reg = ((modRm >> 3) & 7) | (rexR << 3);
            int rm = (modRm & 7) | (rexB << 3);

            if (mod == 3)
            {
                // 레지스터 복사: 원본의 소속을 그대로 옮김
                int dst = op == 0x8B ? reg : rm;
                int src = op == 0x8B ? rm : reg;
                bool srcClosure = (closure & (1 << src)) != 0, srcOwner = (owner & (1 << src)) != 0;
                closure = Set(closure, dst, srcClosure);
                owner = Set(owner, dst, srcOwner);
                i += 2;
                continue;
            }

            if (op == 0x89)
            {
                // 메모리에 저장: 레지스터는 안 바뀜. 길이만 건너뜀
                i += 2 + AddressLength(code, i + 2, mod, modRm & 7);
                continue;
            }

            // MOV r64, [메모리]: 목적 레지스터가 새 값으로 덮인다
            if (mod == 0)
            {
                closure = Set(closure, reg, false);
                owner = Set(owner, reg, false);
                i += 2 + AddressLength(code, i + 2, mod, modRm & 7);
                continue;
            }

            int baseReg = rm;
            int dispAt = i + 3;
            if ((modRm & 7) == 4)
            {
                if (dispAt >= code.Length) break;
                baseReg = (code[dispAt] & 7) | (rexB << 3); // SIB 기준 레지스터
                dispAt++;
            }
            if (!TryReadDisp(code, dispAt, mod, out int disp)) break;

            bool loadsOwner = (closure & (1 << baseReg)) != 0 && disp == ownerPtrOffset;
            if ((owner & (1 << baseReg)) != 0 && disp == targetFieldOffset)
                return Result.Hit;

            closure = Set(closure, reg, false);
            owner = Set(owner, reg, loadsOwner);
            if (loadsOwner) sawOwner = true;
            i = dispAt + (mod == 1 ? 1 : 4) - 1;
        }
        return sawOwner ? Result.OwnerOnly : Result.Inconclusive;
    }

    /// <summary>2026-11-09 ~ 11-11의 방식: 변위가 ownerPtrOffset → targetFieldOffset 순서로 나오면 맞음(레지스터는 안 봄).</summary>
    private static Result ScanLoose(ReadOnlySpan<byte> code, int ownerPtrOffset, int targetFieldOffset)
    {
        bool sawOwner = ownerPtrOffset <= 0;
        for (int i = 0; i + 3 < code.Length; i++)
        {
            byte b = code[i];
            if (b == 0xCC && code[i + 1] == 0xCC) break;
            if ((b & 0xF8) != 0x48 || code[i + 1] != 0x8B) continue;
            byte modRm = code[i + 2];
            int mod = modRm >> 6;
            if (mod != 1 && mod != 2) continue;
            int dispAt = i + 3 + ((modRm & 7) == 4 ? 1 : 0);
            if (!TryReadDisp(code, dispAt, mod, out int disp)) continue;

            if (!sawOwner && disp == ownerPtrOffset) sawOwner = true;
            else if (sawOwner && disp == targetFieldOffset) return Result.Hit;
        }
        return sawOwner ? Result.OwnerOnly : Result.Inconclusive;
    }

    private static int Set(int mask, int reg, bool on) => on ? mask | (1 << reg) : mask & ~(1 << reg);

    private static bool TryReadDisp(ReadOnlySpan<byte> code, int at, int mod, out int disp)
    {
        disp = 0;
        if (mod == 1)
        {
            if (at >= code.Length) return false;
            disp = (sbyte)code[at];
            return true;
        }
        if (at + 3 >= code.Length) return false;
        disp = code[at] | (code[at + 1] << 8) | (code[at + 2] << 16) | (code[at + 3] << 24);
        return true;
    }

    /// <summary>ModRM 뒤에 오는 SIB·변위 바이트 수.</summary>
    private static int AddressLength(ReadOnlySpan<byte> code, int modRmAt, int mod, int rmLow)
    {
        int len = 0;
        if (rmLow == 4)
        {
            len++; // SIB
            if (mod == 0 && modRmAt + 1 < code.Length && (code[modRmAt + 1] & 7) == 5) len += 4; // [index*s + disp32]
        }
        if (mod == 0 && rmLow == 5) len += 4; // [rip + disp32]
        else if (mod == 1) len += 1;
        else if (mod == 2) len += 4;
        return len;
    }
}
