using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using HarmonyLib;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using MelonLoader;

namespace SixtarGateMod.Managers;

/// <summary>
/// Double-Wrapper Lambda Translator (이중래퍼 람다 번역가):
///
/// [1단계 래퍼 - Fast-Path Dictionary Cache (O(1) 직행)]
/// - GameAssembly.dll의 크기·수정 시각 및 Steam Build ID를 버전 지문(Fingerprint)으로 삼아,
///   논리적 람다 키 -> 실제 컴파일러 생성 중첩 타입명/메서드명/소유자 필드명/RVA/고른 방법을
///   UserData/SixtarGateMod.LambdaMap.json 에 캐싱.
/// - 평소 부팅 시에는 GetNestedTypes() 전체 탐색이나 바이트 분석 없이 캐시된 이름으로 직행하고, 현재 네이티브 주소가
///   저장된 RVA와 같은지만 확인(다르면 재번역).
///
/// [2단계 래퍼 - Smart Structural &amp; Native Offset Translator (업데이트 시 자동 재번역)]
/// 1) 후보 수집: 바깥 형식의 클로저(<c>&lt;&gt;c__DisplayClass</c>, <c>&lt;&gt;c</c>)와 바깥 형식 자신(Roslyn은 this만 잡는
///    람다를 바깥 형식의 인스턴스 메서드 <c>&lt;M&gt;b__N_M</c>로 만듦)에서 부모 메서드 접두사 + 파라미터가 맞는 람다.
/// 2) 걸러내기: 패치가 읽는 캡처 필드(예: track, args)가 있는 클로저만, 콜백이면 소유자(<c>&lt;&gt;4__this</c>)에 닿는 것만,
///    파라미터 형식이 정확히 같은 것 우선. 순서는 리플렉션 순서가 아니라 람다 번호(소스 순서) 기준.
/// 3) 같은 시그니처 후보가 여럿이면(PlayLoadingManager의 jacketFull vs trackThumbnail 등) 소유자의 대상 필드
///    오프셋(il2cpp_field_get_offset)과 각 후보의 x64 본문을 <see cref="NativeLambdaScan"/>으로 대조
///    (레지스터를 따라가는 엄격 모드 → 못 맞히면 느슨 모드). 후보가 하나여도 검증해, 대상 필드를 안 읽으면 경고.
/// 4) 결과와 "어떻게 골랐는지"(how)를 저장. 순서 추정(order)으로 고른 항목은 캐시 직행 때도 경고를 다시 낸다.
/// </summary>
public static class LambdaTranslator
{
    private const string LOG_PREFIX = "[LambdaTranslator]";
    private const string CacheFileName = "SixtarGateMod.LambdaMap.json";

    private const BindingFlags AllFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>번역 방법. 캐시에 저장되며 "order"/"ambiguous"는 확인되지 않은 선택이라 매 부팅 경고.</summary>
    private static class How
    {
        public const string Native = "native";          // 엄격 기계어 대조로 하나만 맞음
        public const string NativeLoose = "native-loose"; // 느슨 대조로 하나만 맞음
        public const string Ambiguous = "ambiguous";    // 기계어로 여러 개가 맞아 순서로 고름
        public const string Single = "single";          // 후보 하나(필드 지정 없음)
        public const string Order = "order";            // 후보 여럿을 순서로 고름(기계어 확인 실패/필드 지정 없음)
        public const string Coroutine = "coroutine";

        public static bool Unverified(string how) => how == Order || how == Ambiguous;
    }

    public sealed class LambdaEntry
    {
        public string ClosureTypeName { get; set; } = "";
        public string MethodName { get; set; } = "";
        public string OwnerFieldName { get; set; } = "";
        public int TargetFieldOffset { get; set; } = -1;
        public string RvaHex { get; set; } = "";
        public string How { get; set; } = "";
        public int Candidates { get; set; }
    }

    private sealed class ClosureGroupIndex
    {
        public readonly List<Type> ClosureTypes = new();
        public readonly List<Type> StateMachineTypes = new();
    }

    private sealed class Candidate
    {
        public MethodInfo Method = null!;
        public bool OnOuter;   // 바깥 형식 자신의 인스턴스 메서드(this만 캡처한 람다)
        public bool Exact;     // 파라미터 형식이 정확히 같음
        public int Ordinal;    // 람다 번호(b__N 또는 b__M_N의 N)
    }

    private static readonly Dictionary<string, LambdaEntry> _entries = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, MethodInfo> _resolvedMethods = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, ClosureGroupIndex> _typeIndexCache = new();
    private static readonly HashSet<string> _requestedKeys = new(StringComparer.Ordinal);

    private static string _cacheFilePath = "";
    private static string _currentBuildFingerprint = "";
    private static string _cachedBuildFingerprint = "";
    private static IntPtr _gameAssemblyBase = IntPtr.Zero;

    private static bool _dirty;
    private static bool _retranslating;
    private static int _fastHits;
    private static int _translatedCount;
    private static int _unverifiedCount;
    private static readonly Stopwatch _stopwatch = new();

    /// <summary>"빌드ID|GameAssembly 크기|수정 시각" — 같은 바이너리인지 판단하는 지문(IcfScanner 캐시도 사용).</summary>
    public static string BuildFingerprint => _currentBuildFingerprint;

    /// <summary>GameAssembly.dll이 올라간 주소(RVA 계산용). 못 찾으면 IntPtr.Zero.</summary>
    public static IntPtr GameAssemblyBase => _gameAssemblyBase;

    public static void Initialize(string gameRoot)
    {
        // 번역에 쓴 시간만 잰다(전에는 Initialize부터 FlushSummary까지라 Harmony 패치 약 2초가 섞여 "3131ms"처럼 보였음).
        _stopwatch.Reset();
        _fastHits = 0;
        _translatedCount = 0;
        _unverifiedCount = 0;
        _dirty = false;
        _requestedKeys.Clear();

        try
        {
            string userDataDir = Path.Combine(gameRoot, "UserData");
            if (!Directory.Exists(userDataDir)) Directory.CreateDirectory(userDataDir);
            _cacheFilePath = Path.Combine(userDataDir, CacheFileName);

            var gaInfo = new FileInfo(Path.Combine(gameRoot, "GameAssembly.dll"));
            string buildId = UpdateGuard.CurrentBuildId ?? "unknown";
            _currentBuildFingerprint = gaInfo.Exists
                ? $"{buildId}|{gaInfo.Length}|{gaInfo.LastWriteTimeUtc.Ticks}"
                : $"{buildId}|missing";

            try
            {
                foreach (ProcessModule mod in Process.GetCurrentProcess().Modules)
                {
                    if (string.Equals(mod.ModuleName, "GameAssembly.dll", StringComparison.OrdinalIgnoreCase))
                    {
                        _gameAssemblyBase = mod.BaseAddress;
                        break;
                    }
                }
            }
            catch { }
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 초기화 경고 (실시간 번역 모드로 동작): {ex.Message}");
        }

        // 캐시 파일이 깨졌거나 못 읽으면 빈 사전으로 시작(전에는 읽기 예외가 나면 지문 비교를 건너뛰어
        // 일부만 읽힌 사전을 지문 확인 없이 그대로 썼음).
        try { LoadCache(); }
        catch (Exception ex)
        {
            _entries.Clear();
            _cachedBuildFingerprint = "";
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 번역 사전({CacheFileName})을 읽지 못해 새로 만듭니다: {ex.Message}");
        }

        if (!string.Equals(_cachedBuildFingerprint, _currentBuildFingerprint, StringComparison.Ordinal))
        {
            _retranslating = true;
            if (!string.IsNullOrEmpty(_cachedBuildFingerprint))
                Melon<Core>.Logger.Msg($"{LOG_PREFIX} 게임 빌드 변경 감지 -> 람다/코루틴 번역 사전을 새 버전에 맞게 재번역합니다.");
            else
                Melon<Core>.Logger.Msg($"{LOG_PREFIX} 람다 번역 사전 초기화 중...");
            _entries.Clear();
            _resolvedMethods.Clear();
        }
        else
        {
            _retranslating = false;
        }
    }

    public static void FlushSummary()
    {
        try
        {
            // 이번 실행에서 아무 패치도 묻지 않은 항목(모드에서 지운 패치, 키 형식이 바뀐 옛 항목)은 사전에서 뺀다.
            if (_requestedKeys.Count > 0)
            {
                var stale = new List<string>();
                foreach (var k in _entries.Keys)
                    if (!_requestedKeys.Contains(k)) stale.Add(k);
                foreach (var k in stale) _entries.Remove(k);
                if (stale.Count > 0)
                {
                    _dirty = true;
                    Core.DebugLog($"{LOG_PREFIX} 쓰지 않는 사전 항목 {stale.Count}개 정리");
                }
            }

            if (_dirty) SaveCache();

            string unverified = _unverifiedCount > 0 ? $", 기계어로 확인 못 한 선택 {_unverifiedCount}개(위 경고 참조)" : "";
            if (_translatedCount > 0)
            {
                Melon<Core>.Logger.Msg(
                    $"{LOG_PREFIX} 번역 완료: 새로 번역 {_translatedCount}개, 캐시 직행 {_fastHits}개 " +
                    $"({_stopwatch.Elapsed.TotalMilliseconds:F1}ms, 사전 저장됨{unverified})");
            }
            else if (_fastHits > 0)
            {
                Melon<Core>.Logger.Msg(
                    $"{LOG_PREFIX} 1단계 캐시 직행 완료: 람다/코루틴 {_fastHits}개 즉시 연결 ({_stopwatch.Elapsed.TotalMilliseconds:F1}ms{unverified})");
            }
        }
        catch (Exception ex)
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 사전 저장 실패: {ex.Message}");
        }
    }

    /// <summary>
    /// [이중래퍼] 클로저 람다 메서드를 찾습니다.
    /// 1단계: 번역 사전 캐시에서 O(1) 직행.
    /// 2단계: 캐시 미스 또는 게임 업데이트 시 구조 인덱싱 + 네이티브 필드 오프셋 지문 대조로 정확히 번역.
    /// </summary>
    /// <param name="requiredClosureMembers">패치가 읽는 캡처 필드 이름(예: "track", "args"). 이 필드가 있는 클로저의 람다만 후보.</param>
    /// <param name="ownerType">콜백이 소유자 객체를 꺼내 써야 하면 그 형식. 소유자에 닿지 못하는 람다(정적 <c>&lt;&gt;c</c> 등)는 후보에서 뺌.
    /// <paramref name="accessedOwnerFieldName"/>은 이 형식(없으면 <paramref name="outerType"/>)의 필드.</param>
    public static MethodInfo? TranslateClosureMethod(
        string key,
        Type outerType,
        string methodPrefix,
        Type[] paramTypes,
        int fallbackMatchIndex = 0,
        string? accessedOwnerFieldName = null,
        string[]? requiredClosureMembers = null,
        Type? ownerType = null)
    {
        _requestedKeys.Add(key);
        // 같은 프로세스 내 반복 호출(ICF 검사 -> Harmony 패치)은 메모리 캐시로 즉시 반환
        if (_resolvedMethods.TryGetValue(key, out var inMemory))
            return inMemory;

        _stopwatch.Start();
        try
        {
            return TranslateClosureMethodCore(
                key, outerType, methodPrefix, paramTypes, fallbackMatchIndex, accessedOwnerFieldName, requiredClosureMembers, ownerType);
        }
        finally { _stopwatch.Stop(); }
    }

    private static MethodInfo? TranslateClosureMethodCore(
        string key,
        Type outerType,
        string methodPrefix,
        Type[] paramTypes,
        int fallbackMatchIndex,
        string? accessedOwnerFieldName,
        string[]? requiredClosureMembers,
        Type? ownerType)
    {
        // 1단계 래퍼 (Fast-Path): 버전 지문이 일치하고 사전에 등록된 경우 O(1) 직행
        if (!_retranslating && _entries.TryGetValue(key, out var cached))
        {
            var fastMethod = TryBindCachedMethod(outerType, cached, paramTypes, out string? why);
            if (fastMethod != null)
            {
                _resolvedMethods[key] = fastMethod;
                _fastHits++;
                WarnIfUnverified(key, cached);
                return fastMethod;
            }
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} '{key}' 캐시 항목({cached.ClosureTypeName}.{cached.MethodName})이 현재 바이너리와 달라 재번역합니다: {why}");
        }

        // 2단계 래퍼 (Smart Translator)
        var candidates = CollectClosureCandidates(outerType, methodPrefix, paramTypes);
        if (candidates.Count == 0)
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} '{key}' 번역 실패: {outerType.Name}에서 '{methodPrefix}' 시그니처 후보를 찾지 못함");
            return null;
        }

        if (requiredClosureMembers is { Length: > 0 })
        {
            candidates = Filter(candidates, c => HasMembers(c.Method.DeclaringType!, requiredClosureMembers), key,
                $"캡처 필드({string.Join(", ", requiredClosureMembers)})가 있는 클로저");
        }
        if (ownerType != null)
        {
            candidates = Filter(candidates, c => CanReachOwner(c, outerType, ownerType), key,
                $"소유자({ownerType.Name})에 닿는 람다");
        }
        if (candidates.Exists(c => c.Exact))
            candidates = candidates.FindAll(c => c.Exact);

        MethodInfo chosen;
        string how;
        int targetFieldOffset = -1;
        int idx = fallbackMatchIndex >= 0 && fallbackMatchIndex < candidates.Count ? fallbackMatchIndex : 0;

        if (!string.IsNullOrEmpty(accessedOwnerFieldName))
        {
            targetFieldOffset = GetIl2CppFieldOffset(ownerType ?? outerType, accessedOwnerFieldName!);
            (chosen, how) = ChooseByNativeBody(key, outerType, candidates, idx, accessedOwnerFieldName!, targetFieldOffset);
        }
        else
        {
            chosen = candidates[idx].Method;
            how = candidates.Count == 1 ? How.Single : How.Order;
            if (candidates.Count > 1)
            {
                _unverifiedCount++;
                Melon<Core>.Logger.Warning(
                    $"{LOG_PREFIX} '{key}' 같은 시그니처 후보 {candidates.Count}개({Names(candidates)}) 중 {idx}번째({chosen.Name})를 순서로 선택. " +
                    "구별할 필드(accessedOwnerFieldName)나 캡처 필드(requiredClosureMembers)를 지정하세요.");
            }
        }

        _resolvedMethods[key] = chosen;
        RecordTranslation(key, outerType, chosen, targetFieldOffset, how, candidates.Count);
        return chosen;
    }

    private static (MethodInfo Chosen, string How) ChooseByNativeBody(
        string key, Type outerType, List<Candidate> candidates, int idx, string fieldName, int targetFieldOffset)
    {
        var results = new NativeLambdaScan.Result[candidates.Count];
        Array.Fill(results, NativeLambdaScan.Result.Inconclusive);
        if (targetFieldOffset > 0)
        {
            // 엄격 모드(레지스터 추적) → 아무것도 못 맞히면 느슨 모드
            var hits = ScanAll(outerType, candidates, targetFieldOffset, strict: true, results);
            string how = How.Native;
            if (hits.Count == 0)
            {
                hits = ScanAll(outerType, candidates, targetFieldOffset, strict: false, null);
                how = How.NativeLoose;
            }

            if (hits.Count == 1)
                return (candidates[hits[0]].Method, how);

            if (hits.Count > 1)
            {
                int pick = hits.Contains(idx) ? idx : hits[0];
                _unverifiedCount++;
                Melon<Core>.Logger.Warning(
                    $"{LOG_PREFIX} '{key}' 필드 '{fieldName}'(0x{targetFieldOffset:X})를 읽는 후보가 {hits.Count}개 → {candidates[pick].Method.Name}을(를) 순서로 선택.");
                return (candidates[pick].Method, How.Ambiguous);
            }
        }

        var chosen = candidates[idx].Method;
        if (candidates.Count > 1)
        {
            _unverifiedCount++;
            Melon<Core>.Logger.Warning(
                $"{LOG_PREFIX} '{key}' 기계어 대조 실패(필드 '{fieldName}' 오프셋 {targetFieldOffset}) → 후보 {candidates.Count}개({Names(candidates)}) 중 " +
                $"{idx}번째({chosen.Name})를 순서로 선택. 자켓이 엉뚱하면 tools/version_diff/dis.py로 본문을 확인하세요.");
            return (chosen, How.Order);
        }

        // 후보 하나: 소유자 로드는 보이는데 대상 필드를 안 읽으면 업데이트로 람다 역할이 바뀌었을 수 있음
        if (results[0] == NativeLambdaScan.Result.OwnerOnly)
            Melon<Core>.Logger.Warning(
                $"{LOG_PREFIX} '{key}' 유일한 후보 {chosen.Name}의 본문에서 소유자는 읽지만 필드 '{fieldName}'(0x{targetFieldOffset:X}) 접근이 보이지 않습니다. " +
                "게임 업데이트로 람다가 바뀌었을 수 있으니 dis.py로 확인하세요.");
        return (chosen, How.Single);
    }

    private static List<int> ScanAll(Type outerType, List<Candidate> candidates, int targetFieldOffset, bool strict, NativeLambdaScan.Result[]? results)
    {
        var hits = new List<int>();
        for (int i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            int ownerPtrOffset = c.OnOuter ? 0 : GetOwnerFieldOffsetOnClosure(c.Method.DeclaringType!, outerType);
            var r = ScanNative(c.Method, ownerPtrOffset, targetFieldOffset, strict);
            if (results != null) results[i] = r;
            if (r == NativeLambdaScan.Result.Hit) hits.Add(i);
        }
        return hits;
    }

    private static unsafe NativeLambdaScan.Result ScanNative(MethodInfo method, int ownerPtrOffset, int targetFieldOffset, bool strict)
    {
        IntPtr codePtr = PatchResilience.GetNativeCodePointer(method);
        if (codePtr == IntPtr.Zero) return NativeLambdaScan.Result.Inconclusive;
        try
        {
            var span = new ReadOnlySpan<byte>((void*)codePtr, NativeLambdaScan.DefaultScanBytes);
            return NativeLambdaScan.Scan(span, ownerPtrOffset, targetFieldOffset, strict);
        }
        catch
        {
            return NativeLambdaScan.Result.Inconclusive;
        }
    }

    private static List<Candidate> Filter(List<Candidate> candidates, Predicate<Candidate> keep, string key, string what)
    {
        var kept = candidates.FindAll(keep);
        if (kept.Count > 0) return kept;
        Melon<Core>.Logger.Warning(
            $"{LOG_PREFIX} '{key}' 후보 {candidates.Count}개({Names(candidates)}) 중 {what}가 없음 → 거르지 않고 진행(패치가 필요한 값을 못 읽을 수 있음)");
        return candidates;
    }

    private static bool HasMembers(Type closureType, string[] names)
    {
        foreach (string n in names)
        {
            if (closureType.GetProperty(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) == null &&
                closureType.GetField(n, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) == null)
                return false;
        }
        return true;
    }

    private static bool CanReachOwner(Candidate c, Type outerType, Type ownerType)
    {
        if (c.OnOuter) return !c.Method.IsStatic && ownerType.IsAssignableFrom(outerType);
        return PatchResilience.FindProperty(c.Method.DeclaringType!, ownerType, "__4__this", "<>4__this") != null;
    }

    private static string Names(List<Candidate> candidates)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < candidates.Count && i < 6; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append(candidates[i].Method.DeclaringType?.Name).Append('.').Append(candidates[i].Method.Name);
        }
        if (candidates.Count > 6) sb.Append(", …");
        return sb.ToString();
    }

    /// <summary>
    /// [이중래퍼] 클로저 콜백 메서드와 외부 인스턴스(__4__this) 고속 접근자를 함께 번역합니다.
    /// <paramref name="accessedOwnerFieldName"/>을 지정하면 동일 시그니처 콜백이 여러 개일 때
    /// 네이티브 기계어에서 해당 필드의 오프셋을 실제로 건드리는 람다를 찾아내므로 순서 변경에 영향받지 않습니다.
    /// </summary>
    public static MethodInfo? TranslateClosureCallback<TOwner>(
        string key,
        Type outerType,
        string methodPrefix,
        Type paramType,
        out Func<Il2CppObjectBase, TOwner?>? getOwner,
        int fallbackMatchIndex = 0,
        string? accessedOwnerFieldName = null,
        string[]? requiredClosureMembers = null)
        where TOwner : class
    {
        getOwner = null;
        var method = TranslateClosureMethod(
            key, outerType, methodPrefix, new[] { paramType }, fallbackMatchIndex, accessedOwnerFieldName,
            requiredClosureMembers, typeof(TOwner));
        if (method?.DeclaringType is Type closureType)
        {
            if (closureType == outerType || closureType.IsAssignableFrom(outerType))
            {
                // this만 잡은 람다는 바깥 형식의 인스턴스 메서드라 __instance가 곧 소유자
                getOwner = inst => inst as TOwner;
            }
            else
            {
                string? cachedOwnerField = _entries.TryGetValue(key, out var e) && !string.IsNullOrEmpty(e.OwnerFieldName)
                    ? e.OwnerFieldName
                    : null;
                getOwner = cachedOwnerField != null
                    ? PatchResilience.CreateGetter<TOwner>(closureType, cachedOwnerField, "__4__this", "<>4__this")
                    : PatchResilience.CreateGetter<TOwner>(closureType, "__4__this", "<>4__this");
            }
            if (getOwner == null)
                Melon<Core>.Logger.Warning(
                    $"{LOG_PREFIX} '{key}' {closureType.Name}.{method.Name}에서 {typeof(TOwner).Name}(소유자)을 꺼낼 필드가 없음 → 패치는 붙지만 아무것도 안 함");
        }
        return method;
    }

    /// <summary>
    /// 이미 찾은 클로저 형식 안에서 다른 람다를 찾는다(예: GotoPlayScene 번들 콜백과 같은 클로저의 TextAsset 패턴 콜백).
    /// 2026-11-14 전에는 PatchResilience.FindMethodInType이 사전 밖에서 매번 찾고 이름 문자열을 PadLeft로 정렬했다.
    /// 이제 같은 사전(RVA 대조·how 기록·안 쓰는 항목 정리)에 들어가고, 정렬은 람다 번호 기준.
    /// </summary>
    public static MethodInfo? TranslateMethodInType(string key, Type closureType, string methodPrefix, Type[] paramTypes)
    {
        _requestedKeys.Add(key);
        if (_resolvedMethods.TryGetValue(key, out var inMemory)) return inMemory;

        _stopwatch.Start();
        try
        {
            if (!_retranslating && _entries.TryGetValue(key, out var cached))
            {
                var fast = TryBindCachedMethod(closureType, cached, paramTypes, out string? why);
                if (fast != null)
                {
                    _resolvedMethods[key] = fast;
                    _fastHits++;
                    WarnIfUnverified(key, cached);
                    return fast;
                }
                Melon<Core>.Logger.Warning($"{LOG_PREFIX} '{key}' 캐시 항목({cached.ClosureTypeName}.{cached.MethodName})이 현재 바이너리와 달라 재번역합니다: {why}");
            }

            var candidates = new List<Candidate>();
            foreach (var m in closureType.GetMethods(AllFlags))
            {
                if (!MatchesPrefix(m.Name, methodPrefix) || !ParametersCompatible(m.GetParameters(), paramTypes, out bool exact)) continue;
                candidates.Add(new Candidate { Method = m, Exact = exact, Ordinal = LambdaOrdinal(m.Name) });
            }
            if (candidates.Count == 0)
            {
                Melon<Core>.Logger.Warning($"{LOG_PREFIX} '{key}' 번역 실패: {closureType.Name}에서 '{methodPrefix}' 시그니처 메서드를 찾지 못함");
                return null;
            }
            if (candidates.Exists(c => c.Exact)) candidates = candidates.FindAll(c => c.Exact);
            SortByOrdinal(candidates);

            var chosen = candidates[0].Method;
            string how = candidates.Count == 1 ? How.Single : How.Order;
            if (candidates.Count > 1)
            {
                _unverifiedCount++;
                Melon<Core>.Logger.Warning(
                    $"{LOG_PREFIX} '{key}' {closureType.Name}에서 시그니처가 같은 메서드 {candidates.Count}개({Names(candidates)}) → 번호가 가장 작은 {chosen.Name} 선택");
            }
            _resolvedMethods[key] = chosen;
            RecordTranslation(key, closureType, chosen, -1, how, candidates.Count);
            return chosen;
        }
        finally { _stopwatch.Stop(); }
    }

    private static void WarnIfUnverified(string key, LambdaEntry cached)
    {
        if (!How.Unverified(cached.How) || cached.Candidates <= 1) return;
        _unverifiedCount++;
        Melon<Core>.Logger.Warning(
            $"{LOG_PREFIX} '{key}'는 후보 {cached.Candidates}개 중 {cached.MethodName}을(를) 기계어로 확인하지 못하고 순서로 고른 결과입니다" +
            $"(how={cached.How}). 화면이 이상하면 tools/version_diff/dis.py로 본문을 확인하세요.");
    }

    /// <summary>
    /// [이중래퍼] 코루틴 상태머신(_*_d__*)의 MoveNext()와 상태(__1__state) 접근자를 번역합니다.
    /// </summary>
    public static MethodInfo? TranslateCoroutineMoveNext(
        string key,
        Type outerType,
        string coroutinePrefix,
        out Func<Il2CppObjectBase, int>? getState,
        out Action<Il2CppObjectBase, int>? setState)
    {
        _requestedKeys.Add(key);
        _stopwatch.Start();
        try { return TranslateCoroutineMoveNextCore(key, outerType, coroutinePrefix, out getState, out setState); }
        finally { _stopwatch.Stop(); }
    }

    private static MethodInfo? TranslateCoroutineMoveNextCore(
        string key,
        Type outerType,
        string coroutinePrefix,
        out Func<Il2CppObjectBase, int>? getState,
        out Action<Il2CppObjectBase, int>? setState)
    {
        getState = null;
        setState = null;

        if (_resolvedMethods.TryGetValue(key, out var inMemory) && inMemory.DeclaringType is Type memNested)
        {
            BindState(memNested, out getState, out setState);
            return inMemory;
        }

        // 1단계 래퍼 (Fast-Path)
        if (!_retranslating && _entries.TryGetValue(key, out var cached))
        {
            var nested = outerType.GetNestedType(cached.ClosureTypeName, BindingFlags.Public | BindingFlags.NonPublic);
            var moveNext = nested != null ? AccessTools.Method(nested, cached.MethodName) : null;
            string? why = moveNext == null ? "상태머신/메서드 없음" : RvaMismatch(moveNext, cached);
            if (nested != null && moveNext != null && why == null)
            {
                BindState(nested, out getState, out setState);
                _resolvedMethods[key] = moveNext;
                _fastHits++;
                return moveNext;
            }
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} '{key}' 캐시 항목({cached.ClosureTypeName})이 현재 바이너리와 달라 재번역합니다: {why}");
        }

        // 2단계 래퍼 (Smart Translator): 이름이 정확히 "접두사 + 번호"인 상태머신만(예: _WaitUpdateCheck_d__6).
        // 전에는 StartsWith라 "_WaitUpdateCheck_d__" 뒤에 다른 글자가 붙은 형식도 맞았음.
        string core = coroutinePrefix.StartsWith("_", StringComparison.Ordinal) && coroutinePrefix.EndsWith("_d__", StringComparison.Ordinal)
            ? coroutinePrefix.Substring(1, coroutinePrefix.Length - 5)
            : coroutinePrefix.TrimEnd('_');
        var nameRx = new Regex("^(?:_" + Regex.Escape(core) + "_d__|<" + Regex.Escape(core) + ">d__)(\\d+)$");

        var group = GetOrBuildGroupIndex(outerType);
        var matches = new List<(Type Type, MethodInfo MoveNext, int Ordinal)>();
        foreach (var nested in group.StateMachineTypes)
        {
            var m = nameRx.Match(nested.Name);
            if (!m.Success) continue;
            var moveNext = AccessTools.Method(nested, "MoveNext");
            if (moveNext == null) continue;
            matches.Add((nested, moveNext, int.Parse(m.Groups[1].Value)));
        }

        if (matches.Count == 0)
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} '{key}' 코루틴 번역 실패: {outerType.Name}에서 '{coroutinePrefix}' 상태머신을 찾지 못함");
            return null;
        }
        matches.Sort((a, b) => a.Ordinal.CompareTo(b.Ordinal));
        if (matches.Count > 1)
            Melon<Core>.Logger.Warning(
                $"{LOG_PREFIX} '{key}' 이름이 맞는 상태머신 {matches.Count}개(오버로드된 코루틴?) → 번호가 가장 작은 {matches[0].Type.Name} 선택");

        var chosen = matches[0];
        BindState(chosen.Type, out getState, out setState);
        if (getState == null || setState == null)
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} '{key}' {chosen.Type.Name}에 상태 필드(<>1__state)가 없음 → 패치는 붙지만 아무것도 안 함");
        _resolvedMethods[key] = chosen.MoveNext;
        RecordTranslation(key, outerType, chosen.MoveNext, -1, How.Coroutine, matches.Count);
        return chosen.MoveNext;
    }

    private static void BindState(Type stateMachine, out Func<Il2CppObjectBase, int>? getState, out Action<Il2CppObjectBase, int>? setState)
    {
        getState = PatchResilience.CreateValueGetter<int>(stateMachine, "__1__state", "<>1__state");
        setState = PatchResilience.CreateSetter<int>(stateMachine, "__1__state", "<>1__state");
    }

    private static MethodInfo? TryBindCachedMethod(Type outerType, LambdaEntry cached, Type[] paramTypes, out string? why)
    {
        why = null;
        if (string.IsNullOrEmpty(cached.ClosureTypeName) || string.IsNullOrEmpty(cached.MethodName))
        {
            why = "항목이 비어 있음";
            return null;
        }

        var nested = cached.ClosureTypeName == outerType.Name
            ? outerType // this만 잡은 람다(바깥 형식의 인스턴스 메서드)
            : outerType.GetNestedType(cached.ClosureTypeName, BindingFlags.Public | BindingFlags.NonPublic);
        if (nested == null)
        {
            why = $"형식 {cached.ClosureTypeName} 없음";
            return null;
        }

        MethodInfo? method = nested.GetMethod(cached.MethodName, AllFlags, null, paramTypes, null);
        if (method == null)
        {
            foreach (var m in nested.GetMethods(AllFlags))
            {
                if (string.Equals(m.Name, cached.MethodName, StringComparison.Ordinal) &&
                    ParametersCompatible(m.GetParameters(), paramTypes, out _))
                {
                    method = m;
                    break;
                }
            }
        }
        if (method == null)
        {
            why = $"메서드 {cached.MethodName} 없음";
            return null;
        }

        why = RvaMismatch(method, cached);
        return why == null ? method : null;
    }

    /// <summary>
    /// 지문이 같아도 네이티브 주소가 저장된 RVA와 다르면(지문 충돌, 바이너리 교체) 캐시를 믿지 않는다. 같으면 null.
    /// GameAssembly 기준 주소를 못 구해 RVA 대신 절대 주소가 저장된 경우는 ASLR로 매번 달라지므로 비교하지 않음.
    /// </summary>
    private static string? RvaMismatch(MethodInfo method, LambdaEntry cached)
    {
        if (string.IsNullOrEmpty(cached.RvaHex) || _gameAssemblyBase == IntPtr.Zero) return null;
        IntPtr code = PatchResilience.GetNativeCodePointer(method);
        if (code == IntPtr.Zero) return null;
        string now = $"0x{code.ToInt64() - _gameAssemblyBase.ToInt64():X}";
        return string.Equals(now, cached.RvaHex, StringComparison.OrdinalIgnoreCase) ? null : $"RVA {cached.RvaHex} → {now}";
    }

    private static readonly Regex OrdinalRx = new(@"b__(?:\d+_)?(\d+)$", RegexOptions.Compiled);

    /// <summary>람다 번호(<c>b__N</c> 또는 <c>b__M_N</c>의 N). 없으면 int.MaxValue.</summary>
    private static int LambdaOrdinal(string name)
    {
        var om = OrdinalRx.Match(name);
        return om.Success && int.TryParse(om.Groups[1].Value, out int n) ? n : int.MaxValue;
    }

    /// <summary>Il2CppInterop 이름(<c>_M_b__</c>)과 원래 이름(<c>&lt;M&gt;b__</c>) 둘 다 받는다.</summary>
    private static bool MatchesPrefix(string name, string methodPrefix)
    {
        if (name.StartsWith(methodPrefix, StringComparison.Ordinal)) return true;
        if (!methodPrefix.StartsWith("_", StringComparison.Ordinal)) return false;
        string altPrefix = "<" + methodPrefix.Substring(1).Replace("_b__", ">b__");
        return name.StartsWith(altPrefix, StringComparison.Ordinal);
    }

    /// <summary>리플렉션 순서(메타데이터 순서)는 보장되지 않으므로 람다 번호(Roslyn의 메서드 안 람다 순번 = 소스 순서)로 정렬.</summary>
    private static void SortByOrdinal(List<Candidate> list)
        => list.Sort((a, b) =>
        {
            int c = a.Ordinal.CompareTo(b.Ordinal);
            return c != 0 ? c : string.CompareOrdinal(a.Method.DeclaringType?.Name + a.Method.Name, b.Method.DeclaringType?.Name + b.Method.Name);
        });

    private static List<Candidate> CollectClosureCandidates(Type outerType, string methodPrefix, Type[] paramTypes)
    {
        var list = new List<Candidate>();
        var group = GetOrBuildGroupIndex(outerType);

        void Consider(MethodInfo m, bool onOuter)
        {
            if (!MatchesPrefix(m.Name, methodPrefix)) return;
            if (onOuter && m.IsStatic) return; // 바깥 형식의 정적 메서드는 람다 본문이 아님
            if (!ParametersCompatible(m.GetParameters(), paramTypes, out bool exact)) return;
            list.Add(new Candidate { Method = m, OnOuter = onOuter, Exact = exact, Ordinal = LambdaOrdinal(m.Name) });
        }

        foreach (var nested in group.ClosureTypes)
            foreach (var m in nested.GetMethods(AllFlags))
                Consider(m, false);
        // Roslyn은 this만 캡처한 람다를 바깥 형식의 인스턴스 메서드(<M>b__N_M)로 만든다. 업데이트로 람다가
        // 지역 변수를 더 이상 캡처하지 않게 되면 클로저에서 바깥 형식으로 옮겨 가므로 여기도 본다.
        foreach (var m in outerType.GetMethods(AllFlags))
            Consider(m, true);

        SortByOrdinal(list);
        return list;
    }

    private static ClosureGroupIndex GetOrBuildGroupIndex(Type outerType)
    {
        if (_typeIndexCache.TryGetValue(outerType, out var existing))
            return existing;

        var group = new ClosureGroupIndex();
        foreach (var nested in outerType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            string name = nested.Name;
            if (name.StartsWith("__c", StringComparison.Ordinal) || name.StartsWith("<>c", StringComparison.Ordinal))
                group.ClosureTypes.Add(nested);
            else if (name.Contains("_d__", StringComparison.Ordinal) || name.Contains(">d__", StringComparison.Ordinal))
                group.StateMachineTypes.Add(nested);
        }
        _typeIndexCache[outerType] = group;
        return group;
    }

    private static bool ParametersCompatible(ParameterInfo[] actual, Type[] expected, out bool exact)
    {
        exact = false;
        if (actual.Length != expected.Length) return false;
        exact = true;
        for (int i = 0; i < actual.Length; i++)
        {
            var a = actual[i].ParameterType;
            if (a == expected[i]) continue;
            exact = false;
            if (!expected[i].IsAssignableFrom(a) && !a.IsAssignableFrom(expected[i]))
                return false;
        }
        return true;
    }

    /// <summary>
    /// IL2CPP 런타임에서 <paramref name="type"/>의 <paramref name="fieldName"/> 네이티브 필드 오프셋(바이트)을 조회합니다.
    /// </summary>
    public static int GetIl2CppFieldOffset(Type type, string fieldName)
    {
        try
        {
            var genericStore = typeof(Il2CppClassPointerStore<>).MakeGenericType(type);
            var ptrField = genericStore.GetField("NativeClassPtr", BindingFlags.Public | BindingFlags.Static);
            if (ptrField?.GetValue(null) is not IntPtr classPtr || classPtr == IntPtr.Zero)
                return -1;

            IntPtr fieldPtr = IL2CPP.il2cpp_class_get_field_from_name(classPtr, fieldName);
            if (fieldPtr == IntPtr.Zero)
                fieldPtr = IL2CPP.il2cpp_class_get_field_from_name(classPtr, $"<{fieldName}>k__BackingField");
            if (fieldPtr == IntPtr.Zero) return -1;
            return (int)IL2CPP.il2cpp_field_get_offset(fieldPtr);
        }
        catch
        {
            return -1;
        }
    }

    private static int GetOwnerFieldOffsetOnClosure(Type closureType, Type outerType)
    {
        int offset = GetIl2CppFieldOffset(closureType, "<>4__this");
        if (offset > 0) return offset;
        offset = GetIl2CppFieldOffset(closureType, "__4__this");
        if (offset > 0) return offset;

        var prop = PatchResilience.FindProperty(closureType, outerType, "__4__this", "<>4__this");
        if (prop != null)
        {
            offset = GetIl2CppFieldOffset(closureType, prop.Name);
            if (offset > 0) return offset;
        }
        return 0x18; // IL2CPP 클로저의 기본 <>4__this 오프셋 폴백
    }

    private static void RecordTranslation(string key, Type outerType, MethodInfo chosen, int targetFieldOffset, string how, int candidateCount)
    {
        string closureName = chosen.DeclaringType?.Name ?? "";
        string ownerFieldName = "";
        if (chosen.DeclaringType != null && chosen.DeclaringType != outerType)
        {
            var ownerProp = PatchResilience.FindProperty(chosen.DeclaringType, outerType, "__4__this", "<>4__this");
            if (ownerProp != null) ownerFieldName = ownerProp.Name;
        }

        IntPtr codePtr = PatchResilience.GetNativeCodePointer(chosen);
        string rvaHex = "";
        if (codePtr != IntPtr.Zero && _gameAssemblyBase != IntPtr.Zero)
            rvaHex = $"0x{codePtr.ToInt64() - _gameAssemblyBase.ToInt64():X}";

        _entries[key] = new LambdaEntry
        {
            ClosureTypeName = closureName,
            MethodName = chosen.Name,
            OwnerFieldName = ownerFieldName,
            TargetFieldOffset = targetFieldOffset,
            RvaHex = rvaHex,
            How = how,
            Candidates = candidateCount,
        };
        _dirty = true;
        _translatedCount++;

        Core.DebugLog(
            $"{LOG_PREFIX} 번역됨 [{key}] -> {outerType.Name}.{closureName}.{chosen.Name} " +
            $"(RVA={rvaHex}, how={how}, 후보 {candidateCount}개{(targetFieldOffset > 0 ? $", FieldOffset=0x{targetFieldOffset:X}" : "")})");
    }

    private static readonly Regex EntryRx = new("\"(?<key>[^\"]+)\"\\s*:\\s*\\{(?<body>[^{}]*)\\}", RegexOptions.Compiled);

    private static void LoadCache()
    {
        _entries.Clear();
        _cachedBuildFingerprint = "";
        if (string.IsNullOrEmpty(_cacheFilePath) || !File.Exists(_cacheFilePath)) return;

        string json = File.ReadAllText(_cacheFilePath, Encoding.UTF8);
        var fpMatch = Regex.Match(json, "\"buildFingerprint\"\\s*:\\s*\"([^\"]*)\"");
        if (fpMatch.Success)
            _cachedBuildFingerprint = fpMatch.Groups[1].Value;

        // 항목마다 필드를 따로 읽어 순서·추가 필드(how, candidates)에 영향받지 않게 함(옛 형식도 그대로 읽힘)
        foreach (Match m in EntryRx.Matches(json))
        {
            string body = m.Groups["body"].Value;
            string closure = Str(body, "closure"), method = Str(body, "method");
            if (closure.Length == 0 || method.Length == 0) continue;
            _entries[Unescape(m.Groups["key"].Value)] = new LambdaEntry
            {
                ClosureTypeName = closure,
                MethodName = method,
                OwnerFieldName = Str(body, "ownerField"),
                TargetFieldOffset = Int(body, "fieldOffset", -1),
                RvaHex = Str(body, "rva"),
                How = Str(body, "how"),
                Candidates = Int(body, "candidates", 0),
            };
        }
    }

    private static string Str(string body, string name)
    {
        var m = Regex.Match(body, "\"" + name + "\"\\s*:\\s*\"(?<v>(?:[^\"\\\\]|\\\\.)*)\"");
        return m.Success ? Unescape(m.Groups["v"].Value) : "";
    }

    private static int Int(string body, string name, int fallback)
    {
        var m = Regex.Match(body, "\"" + name + "\"\\s*:\\s*(?<v>-?\\d+)");
        return m.Success && int.TryParse(m.Groups["v"].Value, out int v) ? v : fallback;
    }

    private static void SaveCache()
    {
        if (string.IsNullOrEmpty(_cacheFilePath)) return;

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"buildFingerprint\": \"{EscapeJson(_currentBuildFingerprint)}\",");
        sb.AppendLine($"  \"updatedAt\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",");
        sb.AppendLine("  \"entries\": {");

        var keys = new List<string>(_entries.Keys);
        keys.Sort(StringComparer.Ordinal);
        for (int i = 0; i < keys.Count; i++)
        {
            var e = _entries[keys[i]];
            string comma = i < keys.Count - 1 ? "," : "";
            sb.AppendLine(
                $"    \"{EscapeJson(keys[i])}\": {{ " +
                $"\"closure\": \"{EscapeJson(e.ClosureTypeName)}\", " +
                $"\"method\": \"{EscapeJson(e.MethodName)}\", " +
                $"\"ownerField\": \"{EscapeJson(e.OwnerFieldName)}\", " +
                $"\"fieldOffset\": {e.TargetFieldOffset}, " +
                $"\"rva\": \"{EscapeJson(e.RvaHex)}\", " +
                $"\"how\": \"{EscapeJson(e.How)}\", " +
                $"\"candidates\": {e.Candidates} }}{comma}");
        }

        sb.AppendLine("  }");
        sb.AppendLine("}");
        // 쓰는 도중 게임이 꺼져도 사전이 반쪽이 되지 않게 임시 파일에 쓰고 바꿔치기
        string tmp = _cacheFilePath + ".tmp";
        File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
        File.Move(tmp, _cacheFilePath, overwrite: true);
        _cachedBuildFingerprint = _currentBuildFingerprint;
        _retranslating = false;
        _dirty = false;
    }

    private static string EscapeJson(string s)
        => string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");

    private static string Unescape(string s)
        => s.IndexOf('\\') < 0 ? s : s.Replace("\\\"", "\"").Replace("\\\\", "\\");
}
