// 사용: dotnet run --project tools/lambdascan -- "<게임>\GameAssembly.dll"
// NativeLambdaScan(람다 번역기의 기계어 대조)을 실제 바이너리로 시험한다. RVA·필드 오프셋은 빌드 26455233 기준
// (런타임 il2cpp_field_get_offset 값. dump.cs의 PlayLoadingManager.trackThumbnail 0x48은 틀림 — 실제 0x40).
// 게임이 업데이트되면 RVA를 tools/version_diff/dis.py로 다시 찾아 고칠 것.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SixtarGateMod.Managers;

var dll = File.ReadAllBytes(args[0]);
int pe = BitConverter.ToInt32(dll, 0x3C);
int nsec = BitConverter.ToUInt16(dll, pe + 6);
int optSize = BitConverter.ToUInt16(dll, pe + 20);
int secTab = pe + 24 + optSize;
ReadOnlySpan<byte> At(int rva) {
    for (int s = 0; s < nsec; s++) {
        int o = secTab + s * 40;
        int va = BitConverter.ToInt32(dll, o + 12), vs = BitConverter.ToInt32(dll, o + 8), raw = BitConverter.ToInt32(dll, o + 20);
        if (rva >= va && rva < va + vs) return new ReadOnlySpan<byte>(dll, raw + (rva - va), NativeLambdaScan.DefaultScanBytes);
    }
    throw new Exception("rva");
}
int fail = 0;
void T(string name, int rva, int own, int field, bool strict, NativeLambdaScan.Result want) {
    var got = NativeLambdaScan.Scan(At(rva), own, field, strict);
    bool ok = got == want; if (!ok) fail++;
    Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name,-52} {(strict ? "strict" : "loose ")} -> {got} (want {want})");
}
var H = NativeLambdaScan.Result.Hit; var O = NativeLambdaScan.Result.OwnerOnly;
foreach (bool st in new[] { true, false }) {
  // PlayLoading jacketFull 0x30 / trackThumbnail 0x40 (런타임 오프셋)
  T("PlayLoading b__2 vs jacketFull", 0x525420, 0x18, 0x30, st, H);
  T("PlayLoading b__2 vs trackThumbnail", 0x525420, 0x18, 0x40, st, O);
  T("PlayLoading b__4 vs trackThumbnail", 0x5254A0, 0x18, 0x40, st, H);
  T("PlayLoading b__4 vs jacketFull", 0x5254A0, 0x18, 0x30, st, O);
  T("Result b__3 vs jacket 0x60", 0x509200, 0x18, 0x60, st, H);
  T("Result b__3 vs thumbnail 0x68", 0x509200, 0x18, 0x68, st, O);
  T("Result b__4 vs thumbnail 0x68", 0x509230, 0x18, 0x68, st, H);
  T("Result b__4 vs jacket 0x60", 0x509230, 0x18, 0x60, st, O);
  T("TravelBackground b__1 vs jacket 0x40", 0x509B70, 0x18, 0x40, st, H);
  T("TravelBackground b__1 vs _jacketLoaded 0x58(byte store)", 0x509B70, 0x18, 0x58, st, O);
  T("BGA b__2 (mov rbx,rcx) vs bgaPlayer 0x28", 0x654C40, 0x18, 0x28, st, H);
  T("BGA b__2 vs jacket 0x20 (clip null 분기에서 읽음)", 0x654C40, 0x18, 0x20, st, H);
  T("BGA b__2 vs 0x30(없는 필드)", 0x654C40, 0x18, 0x30, st, O);
  T("BGA b__1(=Inventory ICF) vs jacket 0x20", 0x5097F0, 0x18, 0x20, st, H);
  T("BGA b__1 vs bgaPlayer 0x28", 0x5097F0, 0x18, 0x28, st, O);
}
// 레지스터 추적이 필요한 경우: 소유자 오프셋과 같은 변위가 클로저에서 다시 나오는 TrackObject b__1(소유자 0x10, track 0x18)
T("TrackObject b__1 owner 0x10 -> thumbnail 0x20", 0x63B4D0, 0x10, 0x20, true, H);
// 엄격 모드가 거짓양성을 막는 예: 클로저 [rbx+0x18](track)을 소유자로 잘못 보지 않아야 함 → 0x18에서 0x20 찾기
T("TrackObject b__1 owner 0x18(track) -> 0x20 strict", 0x63B4D0, 0x18, 0x20, true, O);
// 합성 바이트: 소유자 형식 자신의 인스턴스 메서드(rcx=소유자) mov rcx,[rcx+0x30]; jmp
T2("outer-instance lambda", new byte[] { 0x48, 0x83, 0xEC, 0x28, 0x48, 0x8B, 0x49, 0x30, 0xE9, 0, 0, 0, 0, 0xCC, 0xCC }, 0, 0x30, H);
// 합성: 소유자를 r8로 받고 SIB([r12+disp]) 경유
T2("REX.B + SIB", new byte[] { 0x4C, 0x8B, 0x61, 0x18, 0x49, 0x8B, 0x4C, 0x24, 0x48, 0xCC, 0xCC }, 0x18, 0x48, H);
// 합성: 소유자를 다른 값으로 덮은 뒤 같은 변위 → 엄격 모드는 Hit 아님
T2("owner overwritten", new byte[] { 0x48, 0x8B, 0x49, 0x18, 0x48, 0x8B, 0x0D, 1, 2, 3, 4, 0x48, 0x8B, 0x49, 0x30, 0xCC, 0xCC }, 0x18, 0x30, O);
// 2026-11-14 명령 경계 해독: lea/call이 클로저 레지스터를 덮으면 그 뒤 같은 변위는 소유자 로드가 아님
var I = NativeLambdaScan.Result.Inconclusive;
// lea rcx,[rip+X]; mov rax,[rcx+0x18]; mov rcx,[rax+0x30]
T2("lea clobbers closure rcx", new byte[] { 0x48, 0x8D, 0x0D, 1, 2, 3, 4, 0x48, 0x8B, 0x41, 0x18, 0x48, 0x8B, 0x48, 0x30, 0xCC, 0xCC }, 0x18, 0x30, I);
// call rel32; mov rax,[rcx+0x18]; mov rcx,[rax+0x30]
T2("call clobbers volatile rcx", new byte[] { 0xE8, 1, 2, 3, 4, 0x48, 0x8B, 0x41, 0x18, 0x48, 0x8B, 0x48, 0x30, 0xCC, 0xCC }, 0x18, 0x30, I);
// mov rbx,rcx; call; mov rax,[rbx+0x18]; mov rcx,[rax+0x30] → rbx는 call을 넘어 살아남음
T2("closure in rbx survives call", new byte[] { 0x48, 0x8B, 0xD9, 0xE8, 1, 2, 3, 4, 0x48, 0x8B, 0x43, 0x18, 0x48, 0x8B, 0x48, 0x30, 0xCC, 0xCC }, 0x18, 0x30, H);
// xor ecx,ecx(32비트 쓰기)도 rcx를 덮음
T2("xor ecx,ecx clobbers rcx", new byte[] { 0x33, 0xC9, 0x48, 0x8B, 0x41, 0x18, 0x48, 0x8B, 0x48, 0x30, 0xCC, 0xCC }, 0x18, 0x30, I);
// 다른 명령의 즉값 안에 든 "48 8B 41 18"을 mov로 읽지 않음: mov eax, imm32(=48 8B 41 18) 뒤 mov rcx,[rax+0x30]
T2("mov bytes inside an immediate", new byte[] { 0xB8, 0x48, 0x8B, 0x41, 0x18, 0x48, 0x8B, 0x48, 0x30, 0xCC, 0xCC }, 0x18, 0x30, I);

// capstone 대조: X64Decoder 길이가 실제 람다 본문의 명령 길이와 같은지(tools/lambdascan/capstone_lengths.py 출력)
if (args.Length > 1)
{
    int funcs = 0, insns = 0, mismatch = 0, unknownStops = 0, fullyDecoded = 0;
    foreach (var line in File.ReadLines(args[1]))
    {
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) continue;
        int rva = Convert.ToInt32(parts[0], 16);
        var code = At(rva);
        funcs++;
        int at = 0;
        bool ok = true;
        for (int k = 1; k < parts.Length; k++)
        {
            int want = int.Parse(parts[k]);
            if (!X64Decoder.TryDecode(code, at, out var ins))
            {
                // 192바이트 경계에 걸린 마지막 명령은 capstone도 못 읽으므로 여기 오지 않음 → 진짜 미지원
                unknownStops++; ok = false;
                if (unknownStops <= 8) Console.WriteLine($"  미지원 0x{rva:X}+0x{at:X}: {BitConverter.ToString(code.Slice(at, Math.Min(8, code.Length - at)).ToArray())}");
                break;
            }
            insns++;
            if (ins.Length != want)
            {
                mismatch++; ok = false;
                if (mismatch <= 8) Console.WriteLine($"  길이 다름 0x{rva:X}+0x{at:X}: 해독 {ins.Length}, capstone {want}: {BitConverter.ToString(code.Slice(at, Math.Min(10, code.Length - at)).ToArray())}");
                break;
            }
            at += want;
        }
        if (ok) fullyDecoded++;
    }
    Console.WriteLine($"capstone 대조: 함수 {funcs}개, 명령 {insns}개, 길이 다름 {mismatch}, 미지원에서 멈춤 {unknownStops}, 끝까지 해독 {fullyDecoded}");
    if (mismatch > 0) fail++;
}
// ---- BuildFingerprint (업데이트 지문 비교, UpdateGuard) ----
{
    var prev = BuildFingerprint.Parse(new[] { "# c", "buildId=26455233", "gameAssemblySize=64716288", "metadataSize=14020648", "unityPlayerSize=100" })!;
    var same = new Dictionary<string, string> { ["buildId"] = "26455233", ["gameAssemblySize"] = "64716288", ["metadataSize"] = "14020648", ["unityPlayerSize"] = "100" };
    var d0 = BuildFingerprint.Diff(prev, same, out bool r0);
    FP("같은 지문 → 변화 없음", d0.Count == 0 && !r0);

    var gaOnly = new Dictionary<string, string>(same) { ["buildId"] = "26999999", ["gameAssemblySize"] = "64800000" };
    var d1 = BuildFingerprint.Diff(prev, gaOnly, out bool r1);
    FP("빌드ID·GameAssembly만 변경 → 2건, 위험 아님", d1.Count == 2 && !r1);

    var meta = new Dictionary<string, string>(same) { ["metadataSize"] = "14100000" };
    var d2 = BuildFingerprint.Diff(prev, meta, out bool r2);
    FP("메타데이터 변경 → 위험", d2.Count == 1 && r2);

    var unknown = new Dictionary<string, string>(same) { ["metadataSize"] = "?", ["unityPlayerSize"] = "?" };
    var d3 = BuildFingerprint.Diff(prev, unknown, out bool r3);
    FP("못 읽은 항목(?)은 비교 안 함", d3.Count == 0 && !r3);

    FP("항목 없는 파일 → null", BuildFingerprint.Parse(new[] { "# only comment", "", "bad line" }) == null && BuildFingerprint.Parse(null) == null);

    var back = BuildFingerprint.Parse(BuildFingerprint.Format(same, 118, new DateTime(2026, 11, 16, 1, 2, 3)))!;
    FP("저장 → 읽기 왕복", back["buildId"] == "26455233" && back["metadataSize"] == "14020648" && back["patchesApplied"] == "118" && back["savedAt"] == "2026-11-16 01:02:03");

    var oldNoMeta = BuildFingerprint.Parse(new[] { "buildId=26455233" })!;
    var d4 = BuildFingerprint.Diff(oldNoMeta, same, out bool r4);
    FP("옛 기록에 없는 항목은 비교 안 함", d4.Count == 0 && !r4);

    // ---- 2026-11-18: PE 링크 시각·메타데이터 헤더·실기 확인 기록 ----
    string stamp = BuildFingerprint.PeTimestamp(dll.AsSpan(0, 0x1000));
    Console.WriteLine($"     GameAssembly.dll PE TimeDateStamp = {stamp}");
    FP("실제 GameAssembly.dll → PE 링크 시각 읽힘", stamp != "?" && stamp.Length == 8);
    FP("PE가 아닌 바이트 → ?", BuildFingerprint.PeTimestamp(new byte[0x200]) == "?" && BuildFingerprint.PeTimestamp(default) == "?");
    var badPe = (byte[])dll[..0x200].Clone(); BitConverter.GetBytes(0x7FFFFFF0).CopyTo(badPe, 0x3C);
    FP("e_lfanew가 범위 밖 → ?", BuildFingerprint.PeTimestamp(badPe) == "?");

    var hdr = new byte[0x100];
    BitConverter.GetBytes(0xFAB11BAFu).CopyTo(hdr, 0); BitConverter.GetBytes(31).CopyTo(hdr, 4); BitConverter.GetBytes(0x100).CopyTo(hdr, 8);
    BuildFingerprint.MetadataHeader(hdr, out var mv, out var mh);
    var hdr2 = (byte[])hdr.Clone(); hdr2[0x20] ^= 1; // 섹션 크기 하나가 바뀜
    BuildFingerprint.MetadataHeader(hdr2, out var mv2, out var mh2);
    FP("메타데이터 헤더: 버전 31, 섹션 표가 바뀌면 해시가 바뀜", mv == "31" && mv2 == "31" && mh != "?" && mh != mh2);
    BuildFingerprint.MetadataHeader(new byte[64], out var mvBad, out var mhBad);
    FP("메타데이터 sanity 불일치 → ?", mvBad == "?" && mhBad == "?");
    // 실제 메타데이터: <게임>\*_Data\il2cpp_data\Metadata\global-metadata.dat
    string? gameDir = Path.GetDirectoryName(Path.GetFullPath(args[0]));
    string? realMeta = gameDir == null ? null : Directory.GetDirectories(gameDir, "*_Data")
        .Select(d => Path.Combine(d, "il2cpp_data", "Metadata", "global-metadata.dat")).FirstOrDefault(File.Exists);
    if (realMeta != null)
    {
        var head = new byte[0x1000];
        using (var fs = File.OpenRead(realMeta)) { int got = 0; while (got < head.Length) { int r = fs.Read(head, got, head.Length - got); if (r <= 0) break; got += r; } }
        BuildFingerprint.MetadataHeader(head, out var rv, out var rh);
        Console.WriteLine($"     global-metadata.dat 버전 {rv}, 헤더 해시 {rh}");
        FP("실제 global-metadata.dat → 버전 31", rv == "31" && rh != "?");
    }

    var full = new Dictionary<string, string>(same)
    {
        ["gameAssemblyStamp"] = "ABCD1234", ["metadataVersion"] = "31", ["metadataHeader"] = "0123456789ABCDEF", ["unityPlayerStamp"] = "11112222",
    };
    var saved = BuildFingerprint.Parse(BuildFingerprint.Format(full, 118, new DateTime(2026, 11, 18), "2026-11-18 10:00 il vento d'oro\r\nx=y"))!;
    FP("실기 확인 기록 저장 → 읽기 (줄바꿈 제거)", saved[BuildFingerprint.VerifiedPlayKey] == "2026-11-18 10:00 il vento d'oro  x=y" && !saved.ContainsKey("x"));
    FP("기록 없으면 verifiedPlay 줄 없음", !BuildFingerprint.Parse(BuildFingerprint.Format(full, 1, DateTime.Now))!.ContainsKey(BuildFingerprint.VerifiedPlayKey));
    FP("같은 지문 → SameBuild", BuildFingerprint.SameBuild(saved, full));
    FP("링크 시각만 바뀜(크기 같음) → SameBuild 아님 + Diff 1건",
        !BuildFingerprint.SameBuild(saved, new Dictionary<string, string>(full) { ["gameAssemblyStamp"] = "ABCD1235" })
        && BuildFingerprint.Diff(saved, new Dictionary<string, string>(full) { ["gameAssemblyStamp"] = "ABCD1235" }, out _).Count == 1);
    FP("옛 기록(새 키 없음) → SameBuild 아님 (실기 확인 이어받지 않음)", !BuildFingerprint.SameBuild(prev, full));
    FP("GameAssembly 링크 시각을 못 읽음(?) → SameBuild 아님",
        !BuildFingerprint.SameBuild(new Dictionary<string, string>(full) { ["gameAssemblyStamp"] = "?" }, new Dictionary<string, string>(full) { ["gameAssemblyStamp"] = "?" }));
    FP("매니페스트 없는 설치(buildId ?끼리) → SameBuild",
        BuildFingerprint.SameBuild(new Dictionary<string, string>(full) { ["buildId"] = "?" }, new Dictionary<string, string>(full) { ["buildId"] = "?" }));
    FP("UnityPlayer 링크 시각 변경 → 위험",
        BuildFingerprint.Diff(full, new Dictionary<string, string>(full) { ["unityPlayerStamp"] = "33334444" }, out bool rUp).Count == 1 && rUp);
}
void FP(string name, bool ok) { if (!ok) fail++; Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name}"); }

Console.WriteLine(fail == 0 ? "ALL PASS" : $"{fail} FAIL");
return fail;

void T2(string name, byte[] code, int own, int field, NativeLambdaScan.Result want) {
    var got = NativeLambdaScan.Scan(code, own, field, true);
    bool ok = got == want; if (!ok) fail++;
    Console.WriteLine($"{(ok ? "OK  " : "FAIL")} {name,-52} synth  -> {got} (want {want})");
}
