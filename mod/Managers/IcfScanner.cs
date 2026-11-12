using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Il2CppInterop.Runtime;
using MelonLoader;

namespace SixtarGateMod.Managers;

/// <summary>
/// 실행 중 ICF(동일 코드 접기) 검사.
///
/// PatchResilience의 빈 스텁 검사(1~3바이트)는 내용 있는 함수끼리 접힌 경우를 못 잡는다. 업데이트로 훅 대상이
/// 다른 함수와 같은 네이티브 주소를 쓰게 되면 그 패치는 엉뚱한 클래스의 this로도 실행된다
/// (예: FinishProcessor.set_TrackEndTiming ×11, GearObject.NoteJudgedObserver.Notify ↔ InputEventArgs.Invoke).
/// 그래서 IL2CPP 런타임의 모든 이미지·클래스·메서드를 훑어 훅 대상 주소를 공유하는 메서드 수를 센다.
/// 원래는 업데이트 후 Il2CppDumper script.json을 새로 만들어 tools/version_diff/icf_check.py로만 확인할 수 있었다.
///
/// 결과는 GameAssembly.dll 지문(LambdaTranslator와 같음)과 함께 UserData/SixtarGateMod.IcfCounts.txt에 저장해,
/// 같은 빌드에서는 다시 훑지 않는다(훅 대상이 새로 생기면 그때만 다시 훑음).
/// </summary>
public static class IcfScanner
{
    private const string LOG_PREFIX = "[ICFScan]";
    private const string CacheFileName = "SixtarGateMod.IcfCounts.txt";
    private const int MaxSharerNames = 3;

    private sealed class Entry
    {
        public int Count;
        public string Sharers = ""; // 대상 말고 같은 주소를 쓰는 메서드 이름 몇 개
    }

    private static readonly Dictionary<long, Entry> _byRva = new();
    private static IntPtr _base;

    /// <summary>검사 결과를 쓸 수 있으면 true. 실패하면 PatchResilience는 빈 스텁 검사만 한다.</summary>
    public static bool Available { get; private set; }

    /// <summary>
    /// 훅 대상 네이티브 주소들의 공유 개수를 준비한다(캐시가 맞으면 파일에서, 아니면 런타임을 훑어서).
    /// </summary>
    public static void Prepare(ICollection<IntPtr> targets, string gameRoot)
    {
        Available = false;
        _byRva.Clear();
        _base = LambdaTranslator.GameAssemblyBase;
        if (targets.Count == 0 || _base == IntPtr.Zero) return;

        string cachePath = Path.Combine(gameRoot, "UserData", CacheFileName);
        string fingerprint = LambdaTranslator.BuildFingerprint;
        try
        {
            if (TryLoadCache(cachePath, fingerprint, targets))
            {
                Available = true;
                Core.DebugLog($"{LOG_PREFIX} 같은 빌드의 검사 결과 사용 ({CacheFileName}, 대상 {targets.Count}개)");
                return;
            }

            var sw = Stopwatch.StartNew();
            int methods = Scan(targets);
            sw.Stop();
            Available = true;
            Melon<Core>.Logger.Msg($"{LOG_PREFIX} 훅 대상 {targets.Count}개의 주소 공유 검사: 메서드 {methods:N0}개 확인 ({sw.Elapsed.TotalMilliseconds:F0}ms, 결과 저장)");
            SaveCache(cachePath, fingerprint);
        }
        catch (Exception ex)
        {
            _byRva.Clear();
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 주소 공유 검사 실패, 빈 스텁 검사만 함: {ex.Message}");
        }
    }

    /// <summary>
    /// 주소를 공유하는 메서드 수(대상 자신 포함). 모르면 0.
    /// </summary>
    public static int GetShareCount(IntPtr nativeCode, out string sharers)
    {
        sharers = "";
        if (!Available || nativeCode == IntPtr.Zero) return 0;
        if (!_byRva.TryGetValue(nativeCode.ToInt64() - _base.ToInt64(), out var e)) return 0;
        sharers = e.Sharers;
        return e.Count;
    }

    public static string Rva(IntPtr nativeCode)
        => _base == IntPtr.Zero ? $"0x{nativeCode.ToInt64():X}" : $"0x{nativeCode.ToInt64() - _base.ToInt64():X}";

    private static unsafe int Scan(ICollection<IntPtr> targets)
    {
        var wanted = new HashSet<IntPtr>(targets);
        var counts = new Dictionary<IntPtr, int>();
        var names = new Dictionary<IntPtr, List<string>>();
        int total = 0;

        IntPtr domain = IL2CPP.il2cpp_domain_get();
        uint assemblyCount = 0;
        IntPtr* assemblies = IL2CPP.il2cpp_domain_get_assemblies(domain, ref assemblyCount);
        for (uint a = 0; a < assemblyCount; a++)
        {
            IntPtr image = IL2CPP.il2cpp_assembly_get_image(assemblies[a]);
            if (image == IntPtr.Zero) continue;
            uint classCount = IL2CPP.il2cpp_image_get_class_count(image);
            for (uint c = 0; c < classCount; c++)
            {
                IntPtr klass = IL2CPP.il2cpp_image_get_class(image, c);
                if (klass == IntPtr.Zero) continue;
                IntPtr iter = IntPtr.Zero;
                IntPtr method;
                while ((method = IL2CPP.il2cpp_class_get_methods(klass, ref iter)) != IntPtr.Zero)
                {
                    total++;
                    IntPtr code = Marshal.ReadIntPtr(method); // Il2CppMethodInfo::methodPointer (첫 필드)
                    if (code == IntPtr.Zero || !wanted.Contains(code)) continue;
                    counts[code] = counts.TryGetValue(code, out int n) ? n + 1 : 1;
                    if (!names.TryGetValue(code, out var list)) names[code] = list = new List<string>();
                    if (list.Count < MaxSharerNames)
                        list.Add(MethodLabel(klass, method));
                }
            }
        }

        foreach (var code in wanted)
        {
            counts.TryGetValue(code, out int n);
            names.TryGetValue(code, out var list);
            _byRva[code.ToInt64() - _base.ToInt64()] = new Entry
            {
                Count = n,
                Sharers = list == null ? "" : string.Join(", ", list),
            };
        }
        return total;
    }

    private static string MethodLabel(IntPtr klass, IntPtr method)
    {
        try
        {
            // 중첩 형식(<>c__DisplayClass…)은 이름공간이 비어 있으므로 바깥 형식 이름을 붙인다.
            string cls = Marshal.PtrToStringUTF8(IL2CPP.il2cpp_class_get_name(klass)) ?? "?";
            IntPtr outer = klass;
            for (int depth = 0; depth < 4; depth++)
            {
                IntPtr declaring = IL2CPP.il2cpp_class_get_declaring_type(outer);
                if (declaring == IntPtr.Zero) break;
                outer = declaring;
                cls = (Marshal.PtrToStringUTF8(IL2CPP.il2cpp_class_get_name(outer)) ?? "?") + "." + cls;
            }
            string ns = Marshal.PtrToStringUTF8(IL2CPP.il2cpp_class_get_namespace(outer)) ?? "";
            string m = Marshal.PtrToStringUTF8(IL2CPP.il2cpp_method_get_name(method)) ?? "?";
            return (ns.Length > 0 ? ns + "." : "") + cls + "." + m;
        }
        catch
        {
            return "?";
        }
    }

    // 형식: 첫 줄 "fingerprint=…", 이후 "0xRVA=개수|이름, 이름". 사람이 열어 봐도 읽히게 단순 텍스트.
    private static bool TryLoadCache(string path, string fingerprint, ICollection<IntPtr> targets)
    {
        if (!File.Exists(path)) return false;
        var lines = File.ReadAllLines(path, Encoding.UTF8);
        if (lines.Length == 0 || lines[0] != "fingerprint=" + fingerprint) return false;

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            int eq = line.IndexOf('=');
            if (eq <= 2 || !line.StartsWith("0x", StringComparison.Ordinal)) continue;
            if (!long.TryParse(line.AsSpan(2, eq - 2), System.Globalization.NumberStyles.HexNumber, null, out long rva)) continue;
            int bar = line.IndexOf('|', eq);
            string countText = bar < 0 ? line.Substring(eq + 1) : line.Substring(eq + 1, bar - eq - 1);
            if (!int.TryParse(countText, out int count)) continue;
            _byRva[rva] = new Entry { Count = count, Sharers = bar < 0 ? "" : line.Substring(bar + 1) };
        }

        // 새 훅 대상이 생겼으면 다시 훑는다
        foreach (var t in targets)
        {
            if (!_byRva.ContainsKey(t.ToInt64() - _base.ToInt64()))
            {
                _byRva.Clear();
                return false;
            }
        }
        return true;
    }

    private static void SaveCache(string path, string fingerprint)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("fingerprint=").Append(fingerprint).Append('\n');
            var keys = new List<long>(_byRva.Keys);
            keys.Sort();
            foreach (long rva in keys)
            {
                var e = _byRva[rva];
                sb.Append("0x").Append(rva.ToString("X")).Append('=').Append(e.Count);
                if (e.Sharers.Length > 0) sb.Append('|').Append(e.Sharers);
                sb.Append('\n');
            }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 결과 저장 실패 (다음 실행에 다시 훑음): {ex.Message}");
        }
    }
}

/// <summary>
/// 훅 대상이 의도적으로 다른 메서드와 네이티브 주소를 공유함을 표시(그 이상 늘면 IcfScanner가 패치를 건너뜀).
/// 공유해도 안전한 이유를 함께 적는다.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class IcfSharedAttribute : Attribute
{
    public int MaxShared { get; }
    public string Reason { get; }
    public IcfSharedAttribute(int maxShared, string reason)
    {
        MaxShared = maxShared;
        Reason = reason;
    }
}
