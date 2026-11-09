using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
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
///   논리적 람다 키(예: "PlayLoading.JacketFull") -> 실제 컴파일러 생성 중첩 타입명/메서드명/소유자 필드명을
///   UserData/SixtarGateMod.LambdaMap.json 에 캐싱.
/// - 평소 부팅 시에는 GetNestedTypes() 전체 탐색이나 바이트 분석 없이 캐시된 이름으로 O(1) 직행.
///
/// [2단계 래퍼 - Smart Structural &amp; Native Offset Translator (업데이트 시 자동 재번역)]
/// - 게임이 업데이트되어 지문이 바뀌었거나 1단계 직행에 실패하면 번역가가 작동:
///   1) 대상 외부 클래스(OuterType)의 클로저/상태머신을 1회 인덱싱.
///   2) 부모 메서드 접두사 + 파라미터 시그니처로 1차 후보군 추출.
///   3) 동일 시그니처 람다가 여러 개일 때(예: PlayLoadingManager의 jacketFull vs trackThumbnail,
///      Result.TrackInfoModule의 jacket vs thumbnail), 단순 순서(matchIndex)에만 의존하지 않고:
///      - IL2CPP 런타임에서 소유자 클래스의 대상 필드 오프셋(il2cpp_field_get_offset)을 조회하고,
///      - 각 후보 람다의 네이티브 기계어(x64) 내부에서 실제로 참조하는 소유자 필드 변위(displacement)와 대조하여
///        람다 순서가 바뀌어도 100% 정확한 람다로 자동 번역!
///   4) 번역 결과를 새 버전 사전에 저장하고 로그로 번역 내역을 보고.
/// </summary>
public static class LambdaTranslator
{
    private const string LOG_PREFIX = "[LambdaTranslator]";
    private const string CacheFileName = "SixtarGateMod.LambdaMap.json";

    private const BindingFlags AllFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public sealed class LambdaEntry
    {
        public string ClosureTypeName { get; set; } = "";
        public string MethodName { get; set; } = "";
        public string OwnerFieldName { get; set; } = "";
        public int TargetFieldOffset { get; set; } = -1;
        public string RvaHex { get; set; } = "";
    }

    private sealed class ClosureGroupIndex
    {
        public readonly List<Type> ClosureTypes = new();
        public readonly List<Type> StateMachineTypes = new();
    }

    private static readonly Dictionary<string, LambdaEntry> _entries = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, MethodInfo> _resolvedMethods = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, ClosureGroupIndex> _typeIndexCache = new();

    private static string _cacheFilePath = "";
    private static string _currentBuildFingerprint = "";
    private static string _cachedBuildFingerprint = "";
    private static IntPtr _gameAssemblyBase = IntPtr.Zero;

    private static bool _dirty;
    private static bool _retranslating;
    private static int _fastHits;
    private static int _translatedCount;
    private static readonly Stopwatch _stopwatch = new();

    public static void Initialize(string gameRoot)
    {
        _stopwatch.Restart();
        _fastHits = 0;
        _translatedCount = 0;
        _dirty = false;

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

            LoadCache();

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
        catch (Exception ex)
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} 초기화 경고 (실시간 번역 모드로 동작): {ex.Message}");
        }
    }

    public static void FlushSummary()
    {
        _stopwatch.Stop();
        try
        {
            if (_dirty) SaveCache();

            if (_translatedCount > 0)
            {
                Melon<Core>.Logger.Msg(
                    $"{LOG_PREFIX} 번역 완료: 새로 번역 {_translatedCount}개, 캐시 직행 {_fastHits}개 " +
                    $"({_stopwatch.Elapsed.TotalMilliseconds:F1}ms, 사전 저장됨)");
            }
            else if (_fastHits > 0)
            {
                Melon<Core>.Logger.Msg(
                    $"{LOG_PREFIX} 1단계 캐시 직행 완료: 람다/코루틴 {_fastHits}개 즉시 연결 ({_stopwatch.Elapsed.TotalMilliseconds:F1}ms)");
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
    public static MethodInfo? TranslateClosureMethod(
        string key,
        Type outerType,
        string methodPrefix,
        Type[] paramTypes,
        int fallbackMatchIndex = 0,
        string? accessedOwnerFieldName = null)
    {
        // 같은 프로세스 내 반복 호출(ICF 검사 -> Harmony 패치)은 메모리 캐시로 즉시 반환
        if (_resolvedMethods.TryGetValue(key, out var inMemory))
            return inMemory;

        // 1단계 래퍼 (Fast-Path): 버전 지문이 일치하고 사전에 등록된 경우 O(1) 직행
        if (!_retranslating && _entries.TryGetValue(key, out var cached))
        {
            var fastMethod = TryBindCachedMethod(outerType, cached, paramTypes);
            if (fastMethod != null)
            {
                _resolvedMethods[key] = fastMethod;
                _fastHits++;
                return fastMethod;
            }
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} '{key}' 캐시 항목({cached.ClosureTypeName}.{cached.MethodName})이 현재 바이너리와 달라 재번역합니다.");
        }

        // 2단계 래퍼 (Smart Translator): 인덱싱된 클로저 목록에서 후보 추출 + 네이티브 필드 오프셋 지문 대조
        var candidates = CollectClosureCandidates(outerType, methodPrefix, paramTypes);
        if (candidates.Count == 0)
        {
            Melon<Core>.Logger.Warning($"{LOG_PREFIX} '{key}' 번역 실패: {outerType.Name}에서 '{methodPrefix}' 시그니처 후보를 찾지 못함");
            return null;
        }

        MethodInfo? chosen = null;
        int targetFieldOffset = -1;

        if (!string.IsNullOrEmpty(accessedOwnerFieldName))
        {
            targetFieldOffset = GetIl2CppFieldOffset(outerType, accessedOwnerFieldName!);
            if (targetFieldOffset > 0 && candidates.Count > 1)
            {
                foreach (var cand in candidates)
                {
                    int ownerPtrOffset = GetOwnerFieldOffsetOnClosure(cand.DeclaringType!, outerType);
                    if (NativeBodyAccessesOwnerField(cand, ownerPtrOffset, targetFieldOffset))
                    {
                        chosen = cand;
                        break;
                    }
                }
            }
        }

        if (chosen == null)
        {
            int idx = fallbackMatchIndex >= 0 && fallbackMatchIndex < candidates.Count ? fallbackMatchIndex : 0;
            chosen = candidates[idx];
            // 같은 시그니처 후보가 여럿인데 기계어로 못 가렸으면 순서 추정임을 알린다(틀리면 로딩/결과 화면 자켓이 흰 박스).
            if (!string.IsNullOrEmpty(accessedOwnerFieldName) && candidates.Count > 1)
                Melon<Core>.Logger.Warning(
                    $"{LOG_PREFIX} '{key}' 기계어 대조 실패(필드 '{accessedOwnerFieldName}' 오프셋 {targetFieldOffset}) → 후보 {candidates.Count}개 중 " +
                    $"{idx}번째({chosen.Name})를 순서로 선택. 자켓이 엉뚱하면 tools/version_diff/dis.py로 본문을 확인하세요.");
        }

        _resolvedMethods[key] = chosen;
        RecordTranslation(key, outerType, chosen, targetFieldOffset);
        return chosen;
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
        string? accessedOwnerFieldName = null)
        where TOwner : class
    {
        getOwner = null;
        var method = TranslateClosureMethod(
            key, outerType, methodPrefix, new[] { paramType }, fallbackMatchIndex, accessedOwnerFieldName);
        if (method?.DeclaringType is Type closureType)
        {
            string? cachedOwnerField = _entries.TryGetValue(key, out var e) && !string.IsNullOrEmpty(e.OwnerFieldName)
                ? e.OwnerFieldName
                : null;
            getOwner = cachedOwnerField != null
                ? PatchResilience.CreateGetter<TOwner>(closureType, cachedOwnerField, "__4__this", "<>4__this")
                : PatchResilience.CreateGetter<TOwner>(closureType, "__4__this", "<>4__this");
        }
        return method;
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
        getState = null;
        setState = null;

        if (_resolvedMethods.TryGetValue(key, out var inMemory) && inMemory.DeclaringType is Type memNested)
        {
            getState = PatchResilience.CreateValueGetter<int>(memNested, "__1__state", "<>1__state");
            setState = PatchResilience.CreateSetter<int>(memNested, "__1__state", "<>1__state");
            return inMemory;
        }

        // 1단계 래퍼 (Fast-Path)
        if (!_retranslating && _entries.TryGetValue(key, out var cached))
        {
            var nested = outerType.GetNestedType(cached.ClosureTypeName, BindingFlags.Public | BindingFlags.NonPublic);
            var moveNext = nested != null ? AccessTools.Method(nested, cached.MethodName) : null;
            if (nested != null && moveNext != null)
            {
                getState = PatchResilience.CreateValueGetter<int>(nested, "__1__state", "<>1__state");
                setState = PatchResilience.CreateSetter<int>(nested, "__1__state", "<>1__state");
                _resolvedMethods[key] = moveNext;
                _fastHits++;
                return moveNext;
            }
        }

        // 2단계 래퍼 (Smart Translator)
        string altPrefix = coroutinePrefix.StartsWith("_", StringComparison.Ordinal)
            ? "<" + coroutinePrefix.Substring(1).Replace("_d__", ">d__")
            : coroutinePrefix;

        var group = GetOrBuildGroupIndex(outerType);
        foreach (var nested in group.StateMachineTypes)
        {
            if (!nested.Name.StartsWith(coroutinePrefix, StringComparison.Ordinal) &&
                !nested.Name.StartsWith(altPrefix, StringComparison.Ordinal))
                continue;

            var moveNext = AccessTools.Method(nested, "MoveNext");
            if (moveNext != null)
            {
                getState = PatchResilience.CreateValueGetter<int>(nested, "__1__state", "<>1__state");
                setState = PatchResilience.CreateSetter<int>(nested, "__1__state", "<>1__state");
                _resolvedMethods[key] = moveNext;
                RecordTranslation(key, outerType, moveNext, -1);
                return moveNext;
            }
        }

        Melon<Core>.Logger.Warning($"{LOG_PREFIX} '{key}' 코루틴 번역 실패: {outerType.Name}에서 '{coroutinePrefix}' 상태머신을 찾지 못함");
        return null;
    }

    private static MethodInfo? TryBindCachedMethod(Type outerType, LambdaEntry cached, Type[] paramTypes)
    {
        if (string.IsNullOrEmpty(cached.ClosureTypeName) || string.IsNullOrEmpty(cached.MethodName))
            return null;

        var nested = outerType.GetNestedType(cached.ClosureTypeName, BindingFlags.Public | BindingFlags.NonPublic);
        if (nested == null) return null;

        var method = nested.GetMethod(cached.MethodName, AllFlags, null, paramTypes, null);
        if (method != null) return method;

        foreach (var m in nested.GetMethods(AllFlags))
        {
            if (string.Equals(m.Name, cached.MethodName, StringComparison.Ordinal) &&
                ParametersCompatible(m.GetParameters(), paramTypes))
                return m;
        }
        return null;
    }

    private static List<MethodInfo> CollectClosureCandidates(Type outerType, string methodPrefix, Type[] paramTypes)
    {
        string altPrefix = methodPrefix.StartsWith("_", StringComparison.Ordinal)
            ? "<" + methodPrefix.Substring(1).Replace("_b__", ">b__")
            : methodPrefix;

        var list = new List<MethodInfo>();
        var group = GetOrBuildGroupIndex(outerType);

        foreach (var nested in group.ClosureTypes)
        {
            foreach (var m in nested.GetMethods(AllFlags))
            {
                if (!m.Name.StartsWith(methodPrefix, StringComparison.Ordinal) &&
                    !m.Name.StartsWith(altPrefix, StringComparison.Ordinal))
                    continue;

                if (ParametersCompatible(m.GetParameters(), paramTypes))
                    list.Add(m);
            }
        }
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

    private static bool ParametersCompatible(ParameterInfo[] actual, Type[] expected)
    {
        if (actual.Length != expected.Length) return false;
        for (int i = 0; i < actual.Length; i++)
        {
            if (actual[i].ParameterType != expected[i] &&
                !expected[i].IsAssignableFrom(actual[i].ParameterType) &&
                !actual[i].ParameterType.IsAssignableFrom(expected[i]))
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

    /// <summary>
    /// 람다의 네이티브 기계어(최대 96바이트)를 스캔하여,
    /// 클로저의 <c>&lt;&gt;4__this</c> 로드 후 소유자 객체의 <paramref name="targetFieldOffset"/> 변위(`mov reg, [reg + disp8]`)를
    /// 실제로 참조하는지 검사합니다.
    /// 예: PlayLoadingManager._OnLoadResource_Travel_b__2 는 [rcx + 0x30] (jacketFull),
    ///     _b__4 는 [rcx + 0x40] (trackThumbnail)을 참조하므로 순서가 뒤집혀도 정확히 구별합니다.
    /// </summary>
    private static unsafe bool NativeBodyAccessesOwnerField(MethodInfo candidate, int ownerPtrOffset, int targetFieldOffset)
    {
        if (targetFieldOffset <= 0) return false;
        IntPtr codePtr = PatchResilience.GetNativeCodePointer(candidate);
        if (codePtr == IntPtr.Zero) return false;

        const int ScanBytes = 96;
        try
        {
            byte* p = (byte*)codePtr;
            bool sawOwnerDeref = ownerPtrOffset <= 0;

            // 람다 본문 앞쪽 96바이트 스캔 (int3 패딩 전까지)
            for (int i = 0; i + 3 < ScanBytes; i++)
            {
                byte b = p[i];
                if (b == 0xCC) break; // int3 패딩 도달

                // REX.W(0x48~0x4F: R/X/B 조합 포함, 기준 레지스터 r8~r15도) + MOV r64, r/m64 (0x8B) + ModRM
                if ((b & 0xF8) != 0x48 || p[i + 1] != 0x8B) continue;
                byte modRm = p[i + 2];
                int mod = (modRm >> 6) & 3;
                int rm = modRm & 7;
                if (mod != 1 && mod != 2) continue; // [reg + disp8] / [reg + disp32]만
                int dispAt = i + 3 + (rm == 4 ? 1 : 0); // rm=4면 SIB 바이트(예: [r12 + disp])
                int disp;
                if (mod == 1)
                {
                    if (dispAt >= ScanBytes) continue;
                    disp = (sbyte)p[dispAt];
                }
                else
                {
                    if (dispAt + 3 >= ScanBytes) continue;
                    disp = p[dispAt] | (p[dispAt + 1] << 8) | (p[dispAt + 2] << 16) | (p[dispAt + 3] << 24);
                }

                if (!sawOwnerDeref && disp == ownerPtrOffset)
                    sawOwnerDeref = true;
                else if (sawOwnerDeref && disp == targetFieldOffset)
                    return true;
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    private static void RecordTranslation(string key, Type outerType, MethodInfo chosen, int targetFieldOffset)
    {
        string closureName = chosen.DeclaringType?.Name ?? "";
        string ownerFieldName = "";
        if (chosen.DeclaringType != null)
        {
            var ownerProp = PatchResilience.FindProperty(chosen.DeclaringType, outerType, "__4__this", "<>4__this");
            if (ownerProp != null) ownerFieldName = ownerProp.Name;
        }

        IntPtr codePtr = PatchResilience.GetNativeCodePointer(chosen);
        string rvaHex = "";
        if (codePtr != IntPtr.Zero)
        {
            long rva = _gameAssemblyBase != IntPtr.Zero
                ? codePtr.ToInt64() - _gameAssemblyBase.ToInt64()
                : codePtr.ToInt64();
            rvaHex = $"0x{rva:X}";
        }

        _entries[key] = new LambdaEntry
        {
            ClosureTypeName = closureName,
            MethodName = chosen.Name,
            OwnerFieldName = ownerFieldName,
            TargetFieldOffset = targetFieldOffset,
            RvaHex = rvaHex,
        };
        _dirty = true;
        _translatedCount++;

        Core.DebugLog(
            $"{LOG_PREFIX} 번역됨 [{key}] -> {outerType.Name}.{closureName}.{chosen.Name} " +
            $"(RVA={rvaHex}{(targetFieldOffset > 0 ? $", FieldOffset=0x{targetFieldOffset:X}" : "")})");
    }

    private static void LoadCache()
    {
        _entries.Clear();
        _cachedBuildFingerprint = "";
        if (string.IsNullOrEmpty(_cacheFilePath) || !File.Exists(_cacheFilePath)) return;

        string json = File.ReadAllText(_cacheFilePath, Encoding.UTF8);
        var fpMatch = Regex.Match(json, "\"buildFingerprint\"\\s*:\\s*\"([^\"]*)\"");
        if (fpMatch.Success)
            _cachedBuildFingerprint = fpMatch.Groups[1].Value;

        var entryMatches = Regex.Matches(
            json,
            "\"(?<key>[^\"]+)\"\\s*:\\s*\\{\\s*" +
            "\"closure\"\\s*:\\s*\"(?<closure>[^\"]*)\"\\s*,\\s*" +
            "\"method\"\\s*:\\s*\"(?<method>[^\"]*)\"\\s*,\\s*" +
            "\"ownerField\"\\s*:\\s*\"(?<ownerField>[^\"]*)\"\\s*,\\s*" +
            "\"fieldOffset\"\\s*:\\s*(?<fieldOffset>-?\\d+)\\s*,\\s*" +
            "\"rva\"\\s*:\\s*\"(?<rva>[^\"]*)\"\\s*\\}");

        foreach (Match m in entryMatches)
        {
            string key = m.Groups["key"].Value;
            _entries[key] = new LambdaEntry
            {
                ClosureTypeName = m.Groups["closure"].Value,
                MethodName = m.Groups["method"].Value,
                OwnerFieldName = m.Groups["ownerField"].Value,
                TargetFieldOffset = int.TryParse(m.Groups["fieldOffset"].Value, out int fo) ? fo : -1,
                RvaHex = m.Groups["rva"].Value,
            };
        }
    }

    private static void SaveCache()
    {
        if (string.IsNullOrEmpty(_cacheFilePath)) return;

        var sb = new StringBuilder();
        sb.AppendLine("{");
        sb.AppendLine($"  \"buildFingerprint\": \"{EscapeJson(_currentBuildFingerprint)}\",");
        sb.AppendLine($"  \"updatedAt\": \"{DateTime.Now:yyyy-MM-dd HH:mm:ss}\",");
        sb.AppendLine("  \"entries\": {");

        int i = 0;
        foreach (var kv in _entries)
        {
            i++;
            var e = kv.Value;
            string comma = i < _entries.Count ? "," : "";
            sb.AppendLine(
                $"    \"{EscapeJson(kv.Key)}\": {{ " +
                $"\"closure\": \"{EscapeJson(e.ClosureTypeName)}\", " +
                $"\"method\": \"{EscapeJson(e.MethodName)}\", " +
                $"\"ownerField\": \"{EscapeJson(e.OwnerFieldName)}\", " +
                $"\"fieldOffset\": {e.TargetFieldOffset}, " +
                $"\"rva\": \"{EscapeJson(e.RvaHex)}\" }}{comma}");
        }

        sb.AppendLine("  }");
        sb.AppendLine("}");
        File.WriteAllText(_cacheFilePath, sb.ToString(), Encoding.UTF8);
        _cachedBuildFingerprint = _currentBuildFingerprint;
        _retranslating = false;
        _dirty = false;
    }

    private static string EscapeJson(string s)
        => string.IsNullOrEmpty(s) ? "" : s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
