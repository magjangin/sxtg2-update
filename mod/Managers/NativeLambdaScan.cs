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

    /// <summary>
    /// 엄격 모드: <see cref="X64Decoder"/>로 명령 경계를 따라 걸으며 레지스터 소속을 추적한다(2026-11-14).
    /// 전에는 바이트를 하나씩 밀며 64비트 mov만 봤기 때문에 ① 다른 명령의 변위·즉값 바이트를 mov로 잘못 읽을 수 있었고
    /// ② lea/xor/pop/32비트 mov와 call(휘발 레지스터 rax·rcx·rdx·r8~r11을 덮음)이 클로저·소유자 레지스터를 덮는 것을
    /// 몰라, 덮인 뒤의 같은 변위 로드를 소유자 로드로 오인할 수 있었다. 모르는 명령을 만나면 그 자리에서 멈춘다.
    /// </summary>
    private static Result ScanStrict(ReadOnlySpan<byte> code, int ownerPtrOffset, int targetFieldOffset)
    {
        // 레지스터 16개를 비트로: closure = 클로저 포인터를 담은 레지스터, owner = 소유자 포인터를 담은 레지스터
        int closure = 0, owner = 0;
        bool sawOwner;
        if (ownerPtrOffset > 0) { closure = 1 << Rcx; sawOwner = false; }
        else { owner = 1 << Rcx; sawOwner = true; }

        for (int i = 0; i < code.Length;)
        {
            if (code[i] == 0xCC && i + 1 < code.Length && code[i + 1] == 0xCC) break; // 함수 끝 int3 패딩
            if (!X64Decoder.TryDecode(code, i, out var ins) || ins.Length <= 0) break;  // 모르는 명령: 추측하지 않음
            i += ins.Length;

            if ((ins.Kind == X64Decoder.Kind.MovLoad64 || ins.Kind == X64Decoder.Kind.MovStore64) && ins.Mod == 3)
            {
                // 레지스터 복사: 원본의 소속을 그대로 옮김
                int dst = ins.Kind == X64Decoder.Kind.MovLoad64 ? ins.Reg : ins.Rm;
                int src = ins.Kind == X64Decoder.Kind.MovLoad64 ? ins.Rm : ins.Reg;
                closure = Set(closure, dst, (closure & (1 << src)) != 0);
                owner = Set(owner, dst, (owner & (1 << src)) != 0);
                continue;
            }

            if (ins.Kind == X64Decoder.Kind.MovLoad64 && ins.BaseReg >= 0)
            {
                // MOV r64, [기준+변위]
                if ((owner & (1 << ins.BaseReg)) != 0 && ins.Disp == targetFieldOffset)
                    return Result.Hit;
                bool loadsOwner = (closure & (1 << ins.BaseReg)) != 0 && ins.Disp == ownerPtrOffset;
                closure = Set(closure, ins.Reg, false);
                owner = Set(owner, ins.Reg, loadsOwner);
                if (loadsOwner) sawOwner = true;
                continue;
            }

            int clobbered = ins.Writes;
            if (ins.Kind == X64Decoder.Kind.Call) clobbered |= X64Decoder.VolatileRegs;
            closure &= ~clobbered;
            owner &= ~clobbered;
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

}
