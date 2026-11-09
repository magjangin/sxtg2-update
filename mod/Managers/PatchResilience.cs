using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.InteropServices;
using HarmonyLib;
using Il2CppInterop.Common;
using Il2CppInterop.Runtime.InteropTypes;
using MelonLoader;

namespace SixtarGateMod.Managers;

/// <summary>
/// Update-resilience layer for IL2CPP Harmony patching:
/// 1. Dynamic structural resolution of compiler-generated closures (__c__DisplayClass*) and coroutine state machines
///    (_*_d__*) by parent method prefix + parameter signature, eliminating compile-time references to numbered nested
///    types and lambda ordinals (_b__12, DisplayClass54_0, etc.) that shift whenever methods are added or removed.
/// 2. Typo-tolerant method/property resolution for game methods containing typos (GetNoteObejct, GetSelectableCategoty,
///    ReqeustSessionPlayingCount, UnlockReqeustAchievement, CurrectionVolume, GetNoteReferance).
/// 3. Runtime Identical Code Folding (ICF) native stub guard: inspects the first bytes of the IL2CPP native function
///    pointer before patching and refuses to detour 1-to-3 byte trivial stubs (ret / ret 0 / xor al,al;ret / mov al,1;ret)
///    that MSVC /OPT:ICF shares across thousands of unrelated methods, plus <see cref="IcfScanner"/>: counts every IL2CPP
///    method sharing each hook's native address at run time and skips a patch whose target got folded with other
///    functions by a game update (allowed sharing is declared per patch with [IcfShared]).
/// 4. Compiled Expression-tree property accessors bound once at patch time (zero reflection overhead per call).
/// </summary>
public static class PatchResilience
{
    private const BindingFlags AllFlags =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly HashSet<string> BasePropertyNames = new(StringComparer.Ordinal)
    {
        nameof(Il2CppObjectBase.Pointer),
        nameof(Il2CppObjectBase.ObjectClass),
        nameof(Il2CppObjectBase.WasCollected),
    };

    /// <summary>
    /// Applies all [HarmonyPatch] classes in the mod assembly one by one with runtime ICF trivial-stub protection.
    /// </summary>
    /// <summary>
    /// [HarmonyPatch]가 없어 실행 중에 붙였다 떼는 패치 대상(NoteEffects 등). 시작할 때 ICF 검사에 함께 넣는다.
    /// </summary>
    public static readonly List<Func<MethodBase?>> DynamicPatchTargets = new();

    public static void ApplyAllPatches(HarmonyLib.Harmony harmony, MelonLogger.Instance logger, string gameRoot)
    {
        int applied = 0, failed = 0, skippedIcf = 0;
        var criticalMissing = new List<string>();

        // 1) 패치 클래스별 대상과 네이티브 주소를 먼저 모두 구한다(LambdaTranslator가 결과를 기억하므로 Harmony가
        //    다시 TargetMethod를 불러도 같은 값).
        var plan = new List<(Type Type, List<(MethodBase Method, IntPtr Code)> Targets)>();
        var addresses = new HashSet<IntPtr>();
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Core).Assembly))
        {
            if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
            var targets = new List<(MethodBase, IntPtr)>();
            foreach (var target in ResolveDeclaredTargets(type))
            {
                if (target == null) continue;
                IntPtr code = GetNativeCodePointer(target);
                targets.Add((target, code));
                if (code != IntPtr.Zero) addresses.Add(code);
            }
            plan.Add((type, targets));
        }
        foreach (var resolve in DynamicPatchTargets)
        {
            try
            {
                if (resolve() is { } m && GetNativeCodePointer(m) is var code && code != IntPtr.Zero)
                    addresses.Add(code);
            }
            catch { }
        }

        // 2) 그 주소들을 다른 메서드가 같이 쓰는지 런타임에서 센다(같은 빌드면 캐시).
        IcfScanner.Prepare(addresses, gameRoot);

        // 3) 대상별로 검사하고 패치
        foreach (var (type, targets) in plan)
        {
            try
            {
                if (ShouldSkipForIcf(type, targets, logger))
                {
                    skippedIcf++;
                    NoteCritical(type, criticalMissing);
                    continue;
                }

                harmony.CreateClassProcessor(type).Patch();
                applied++;
            }
            catch (Exception ex)
            {
                failed++;
                logger.Error($"[Harmony] 패치 '{type.Name}' 실패: {ex.Message}");
                NoteCritical(type, criticalMissing);
            }
        }

        if (failed == 0 && skippedIcf == 0)
            logger.Msg($"[Harmony] 패치 {applied}개 모두 적용 완료");
        else
            logger.Warning($"[Harmony] 패치 {applied}개 적용, ICF 보호로 {skippedIcf}개 건너뜀, {failed}개 실패");
        foreach (var line in criticalMissing)
            logger.Error($"[Harmony] 필수 패치 빠짐: {line}");

        LambdaTranslator.FlushSummary();
    }

    private static void NoteCritical(Type patchType, List<string> criticalMissing)
    {
        if (patchType.GetCustomAttribute<CriticalPatchAttribute>() is { } critical)
            criticalMissing.Add($"{patchType.Name} — {critical.Consequence}");
    }

    private static bool ShouldSkipForIcf(Type patchType, List<(MethodBase Method, IntPtr Code)> targets, MelonLogger.Instance logger)
    {
        int allowed = patchType.GetCustomAttribute<IcfSharedAttribute>()?.MaxShared ?? 1;
        foreach (var (target, code) in targets)
        {
            if (code == IntPtr.Zero) continue;
            if (!IsSafeNativeTarget(target, code, allowed, out string reason))
            {
                logger.Warning($"[Harmony/ICFGuard] '{patchType.Name}' ({target.DeclaringType?.Name}.{target.Name}) 건너뜀: {reason}");
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 패치해도 되는 네이티브 주소인지: 빈 스텁이 아니고, 런타임 검사로 센 공유 개수가 <paramref name="allowed"/> 이하.
    /// </summary>
    public static bool IsSafeNativeTarget(MethodBase target, IntPtr code, int allowed, out string reason)
    {
        reason = "";
        if (code == IntPtr.Zero) return true;
        if (IsTrivialNativeStub(code, out string stubDesc))
        {
            reason = $"네이티브 주소 {IcfScanner.Rva(code)}가 여러 함수가 공유하는 빈 스텁({stubDesc})임";
            return false;
        }
        int shared = IcfScanner.GetShareCount(code, out string sharers);
        if (shared > allowed)
        {
            reason = $"네이티브 주소 {IcfScanner.Rva(code)}를 메서드 {shared}개가 공유함(허용 {allowed}개, 예: {sharers}). " +
                     "게임 업데이트로 코드가 접혔으므로 패치하면 다른 클래스의 this로도 실행됨 — 다른 대상으로 옮겨야 함";
            return false;
        }
        return true;
    }

    /// <summary>실행 중에 붙이는 패치(NoteEffects 등)용: 대상이 안전한지 확인.</summary>
    public static bool IsSafeToPatchNow(MethodBase target, out string reason)
        => IsSafeNativeTarget(target, GetNativeCodePointer(target), 1, out reason);

    private static List<MethodBase?> ResolveDeclaredTargets(Type patchType)
    {
        var list = new List<MethodBase?>();
        try
        {
            var tm = patchType.GetMethod("TargetMethod", AllFlags);
            if (tm != null && tm.IsStatic && tm.GetParameters().Length == 0)
            {
                list.Add(tm.Invoke(null, null) as MethodBase);
                return list;
            }

            var tms = patchType.GetMethod("TargetMethods", AllFlags);
            if (tms != null && tms.IsStatic && tms.GetParameters().Length == 0)
            {
                if (tms.Invoke(null, null) is System.Collections.IEnumerable seq)
                {
                    foreach (var item in seq)
                        if (item is MethodBase mb) list.Add(mb);
                }
                return list;
            }

            Type? declType = null;
            string? methodName = null;
            MethodType methodType = MethodType.Normal;
            Type[]? argTypes = null;

            foreach (HarmonyPatch attr in patchType.GetCustomAttributes(typeof(HarmonyPatch), false))
            {
                var info = attr.info;
                if (info == null) continue;
                if (info.declaringType != null) declType = info.declaringType;
                if (!string.IsNullOrEmpty(info.methodName)) methodName = info.methodName;
                if (info.methodType.HasValue) methodType = info.methodType.Value;
                if (info.argumentTypes != null) argTypes = info.argumentTypes;
            }

            if (declType != null && !string.IsNullOrEmpty(methodName))
            {
                MethodBase? resolved = methodType switch
                {
                    MethodType.Getter => AccessTools.PropertyGetter(declType, methodName),
                    MethodType.Setter => AccessTools.PropertySetter(declType, methodName),
                    _ => AccessTools.Method(declType, methodName, argTypes),
                };
                list.Add(resolved);
            }
        }
        catch
        {
            // Let Harmony report any resolution error during CreateClassProcessor.
        }
        return list;
    }

    /// <summary>
    /// Reads the native function pointer (Il2CppMethodInfo::methodPointer) for an Il2CppInterop generated method.
    /// Returns IntPtr.Zero when the method has not been initialized yet or is not an IL2CPP method.
    /// </summary>
    public static IntPtr GetNativeCodePointer(MethodBase method)
    {
        try
        {
            var field = Il2CppInteropUtils.GetIl2CppMethodInfoPointerFieldForGeneratedMethod(method);
            if (field == null) return IntPtr.Zero;
            if (field.GetValue(null) is not IntPtr methodInfoPtr || methodInfoPtr == IntPtr.Zero)
                return IntPtr.Zero;
            return Marshal.ReadIntPtr(methodInfoPtr);
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    /// <summary>
    /// Detects 1-to-3 byte x64 trivial stubs that MSVC /OPT:ICF folds across thousands of unrelated IL2CPP methods:
    ///   C3          -> ret               (empty void method)
    ///   C2 00 00    -> ret 0             (empty void method, 0x450BA0 ICF=5192 in Build 26218612)
    ///   32 C0 C3    -> xor al, al; ret   (constant return false, 0x46CC00 ICF=2817 in Build 26218612)
    ///   33 C0 C3    -> xor eax, eax; ret (constant return 0 / null)
    ///   B0 01 C3    -> mov al, 1; ret    (constant return true, 0x457740 ICF=1037 in Build 26218612)
    /// </summary>
    public static unsafe bool IsTrivialNativeStub(IntPtr codePtr, out string description)
    {
        description = "";
        if (codePtr == IntPtr.Zero) return false;
        try
        {
            byte* p = (byte*)codePtr;
            if (p[0] == 0xC3)
            {
                description = "ret (1-byte empty stub)";
                return true;
            }
            if (p[0] == 0xC2 && p[1] == 0x00 && p[2] == 0x00)
            {
                description = "ret 0 (3-byte empty stub)";
                return true;
            }
            if (p[0] == 0x32 && p[1] == 0xC0 && p[2] == 0xC3)
            {
                description = "xor al,al; ret (3-byte false stub)";
                return true;
            }
            if (p[0] == 0x33 && p[1] == 0xC0 && p[2] == 0xC3)
            {
                description = "xor eax,eax; ret (3-byte zero/null stub)";
                return true;
            }
            if (p[0] == 0xB0 && p[1] == 0x01 && p[2] == 0xC3)
            {
                description = "mov al,1; ret (3-byte true stub)";
                return true;
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    /// <summary>
    /// Resolves a method on <paramref name="type"/> trying each candidate name in order (for game methods with typos
    /// that may be fixed in a future update). Falls back to unambiguous name match if parameter types shifted.
    /// </summary>
    public static MethodInfo? ResolveMethod(Type type, Type[]? paramTypes, params string[] candidateNames)
    {
        foreach (string name in candidateNames)
        {
            if (paramTypes != null)
            {
                var exact = AccessTools.Method(type, name, paramTypes);
                if (exact != null) return exact;
            }
            else
            {
                var m = AccessTools.Method(type, name);
                if (m != null) return m;
            }
        }

        // Fallback if parameter types changed slightly in an update: match by name and parameter count.
        foreach (string name in candidateNames)
        {
            MethodInfo? singleMatch = null;
            int matches = 0;
            foreach (var m in type.GetMethods(AllFlags))
            {
                if (!string.Equals(m.Name, name, StringComparison.Ordinal)) continue;
                if (paramTypes != null && m.GetParameters().Length != paramTypes.Length) continue;
                singleMatch = m;
                matches++;
            }
            if (matches == 1) return singleMatch;
        }

        return null;
    }

    public static MethodInfo? ResolveMethod(Type type, params string[] candidateNames)
        => ResolveMethod(type, null, candidateNames);

    public static MethodInfo? ResolvePropertySetter(Type type, params string[] candidateNames)
    {
        foreach (string name in candidateNames)
        {
            var setter = AccessTools.PropertySetter(type, name);
            if (setter != null) return setter;
            var backingSetter = AccessTools.PropertySetter(type, $"_{name}_k__BackingField");
            if (backingSetter != null) return backingSetter;
        }
        return null;
    }

    /// <summary>
    /// Finds a lambda method inside any compiler-generated closure nested in <paramref name="outerType"/>
    /// via the Double-Wrapper <see cref="LambdaTranslator"/> (O(1) cached dictionary lookup on matching builds,
    /// structural + native field-offset translation when the game updates).
    /// </summary>
    public static MethodInfo? FindClosureMethod(
        Type outerType,
        string methodPrefix,
        int matchIndex,
        string? accessedOwnerFieldName,
        params Type[] paramTypes)
    {
        string paramSig = string.Join(",", Array.ConvertAll(paramTypes, p => p.Name));
        string key = $"{outerType.FullName ?? outerType.Name}:{methodPrefix}({paramSig})#{matchIndex}:{accessedOwnerFieldName ?? "*"}";
        return LambdaTranslator.TranslateClosureMethod(
            key, outerType, methodPrefix, paramTypes, matchIndex, accessedOwnerFieldName);
    }

    public static MethodInfo? FindClosureMethod(Type outerType, string methodPrefix, int matchIndex, params Type[] paramTypes)
        => FindClosureMethod(outerType, methodPrefix, matchIndex, null, paramTypes);

    public static MethodInfo? FindClosureMethod(Type outerType, string methodPrefix, params Type[] paramTypes)
        => FindClosureMethod(outerType, methodPrefix, 0, null, paramTypes);

    /// <summary>
    /// Finds a closure callback method and compiles a fast getter for its outer class reference (__4__this).
    /// When <paramref name="accessedOwnerFieldName"/> is supplied, the translator inspects the x64 native body of
    /// candidate lambdas against the IL2CPP field offset on <typeparamref name="TOwner"/> so lambda order changes
    /// across game updates never confuse same-signature callbacks (e.g. jacketFull vs trackThumbnail).
    /// </summary>
    public static MethodInfo? FindClosureCallback<TOwner>(
        Type outerType,
        string methodPrefix,
        Type paramType,
        out Func<Il2CppObjectBase, TOwner?>? getOwner,
        int matchIndex = 0,
        string? accessedOwnerFieldName = null)
        where TOwner : class
    {
        string key = $"{outerType.FullName ?? outerType.Name}:{methodPrefix}({paramType.Name})#{matchIndex}:{accessedOwnerFieldName ?? "*"}";
        return LambdaTranslator.TranslateClosureCallback(
            key, outerType, methodPrefix, paramType, out getOwner, matchIndex, accessedOwnerFieldName);
    }

    /// <summary>
    /// Finds a method inside a known closure type by prefix and parameter types.
    /// </summary>
    public static MethodInfo? FindMethodInType(Type type, string methodPrefix, params Type[] paramTypes)
    {
        string altPrefix = methodPrefix.StartsWith("_", StringComparison.Ordinal)
            ? "<" + methodPrefix.Substring(1).Replace("_b__", ">b__")
            : methodPrefix;

        foreach (var m in type.GetMethods(AllFlags))
        {
            if (!m.Name.StartsWith(methodPrefix, StringComparison.Ordinal) &&
                !m.Name.StartsWith(altPrefix, StringComparison.Ordinal))
                continue;
            if (ParametersMatch(m.GetParameters(), paramTypes))
                return m;
        }
        return null;
    }

    /// <summary>
    /// Finds the MoveNext() method of a compiler-generated coroutine state machine nested in <paramref name="outerType"/>
    /// (e.g. "_WaitUpdateCheck_d__") via <see cref="LambdaTranslator"/> without hardcoding the state machine's ordinal number.
    /// </summary>
    public static MethodInfo? FindCoroutineMoveNext(
        Type outerType,
        string coroutinePrefix,
        out Func<Il2CppObjectBase, int>? getState,
        out Action<Il2CppObjectBase, int>? setState)
    {
        string key = $"{outerType.FullName ?? outerType.Name}:{coroutinePrefix}.MoveNext";
        return LambdaTranslator.TranslateCoroutineMoveNext(key, outerType, coroutinePrefix, out getState, out setState);
    }

    private static bool ParametersMatch(ParameterInfo[] actual, Type[] expected)
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
    /// Finds a property on <paramref name="targetType"/> by preferred names first, falling back to a unique property
    /// assignable to <paramref name="propertyType"/> if a field was renamed in a game update.
    /// </summary>
    public static PropertyInfo? FindProperty(Type targetType, Type propertyType, params string[] preferredNames)
    {
        foreach (string name in preferredNames)
        {
            var p = targetType.GetProperty(name, AllFlags);
            if (p != null && p.GetMethod != null && propertyType.IsAssignableFrom(p.PropertyType))
                return p;
        }

        PropertyInfo? singleMatch = null;
        int count = 0;
        foreach (var p in targetType.GetProperties(AllFlags))
        {
            if (BasePropertyNames.Contains(p.Name) || p.GetMethod == null) continue;
            if (p.PropertyType == propertyType ||
                (propertyType != typeof(Il2CppObjectBase) && propertyType.IsAssignableFrom(p.PropertyType)))
            {
                singleMatch = p;
                count++;
            }
        }
        return count == 1 ? singleMatch : null;
    }

    /// <summary>
    /// Compiles a zero-reflection reference-type getter: (Il2CppObjectBase inst) =&gt; (T)((TargetType)inst).Prop.
    /// </summary>
    public static Func<Il2CppObjectBase, T?>? CreateGetter<T>(Type targetType, params string[] preferredNames)
        where T : class
    {
        var prop = FindProperty(targetType, typeof(T), preferredNames);
        if (prop?.GetMethod == null) return null;
        try
        {
            var instParam = Expression.Parameter(typeof(Il2CppObjectBase), "inst");
            var castInst = Expression.Convert(instParam, targetType);
            var propAccess = Expression.Property(castInst, prop);
            var castResult = Expression.Convert(propAccess, typeof(T));
            return Expression.Lambda<Func<Il2CppObjectBase, T?>>(castResult, instParam).Compile();
        }
        catch
        {
            return inst => prop.GetValue(inst) as T;
        }
    }

    /// <summary>
    /// Compiles a zero-reflection value-type getter: (Il2CppObjectBase inst) =&gt; ((TargetType)inst).Prop.
    /// </summary>
    public static Func<Il2CppObjectBase, T>? CreateValueGetter<T>(Type targetType, params string[] preferredNames)
        where T : struct
    {
        var prop = FindProperty(targetType, typeof(T), preferredNames);
        if (prop?.GetMethod == null) return null;
        try
        {
            var instParam = Expression.Parameter(typeof(Il2CppObjectBase), "inst");
            var castInst = Expression.Convert(instParam, targetType);
            var propAccess = Expression.Property(castInst, prop);
            return Expression.Lambda<Func<Il2CppObjectBase, T>>(propAccess, instParam).Compile();
        }
        catch
        {
            return inst => (T)prop.GetValue(inst)!;
        }
    }

    /// <summary>
    /// Compiles a zero-reflection property setter: (Il2CppObjectBase inst, T val) =&gt; ((TargetType)inst).Prop = val.
    /// Checks both normal property names and Il2CppInterop auto-property backing fields (_Name_k__BackingField).
    /// </summary>
    public static Action<Il2CppObjectBase, T>? CreateSetter<T>(Type targetType, params string[] preferredNames)
    {
        PropertyInfo? prop = null;
        foreach (string name in preferredNames)
        {
            var candidate = targetType.GetProperty(name, AllFlags)
                ?? targetType.GetProperty($"_{name}_k__BackingField", AllFlags);
            if (candidate?.SetMethod != null && candidate.PropertyType.IsAssignableFrom(typeof(T)))
            {
                prop = candidate;
                break;
            }
        }
        if (prop?.SetMethod == null) return null;

        try
        {
            var instParam = Expression.Parameter(typeof(Il2CppObjectBase), "inst");
            var valParam = Expression.Parameter(typeof(T), "val");
            var castInst = Expression.Convert(instParam, targetType);
            var propAccess = Expression.Property(castInst, prop);
            var assign = Expression.Assign(propAccess, Expression.Convert(valParam, prop.PropertyType));
            return Expression.Lambda<Action<Il2CppObjectBase, T>>(assign, instParam, valParam).Compile();
        }
        catch
        {
            return (inst, val) => prop.SetValue(inst, val);
        }
    }

    /// <summary>
    /// Compiles a zero-reflection single-argument instance method call: (Il2CppObjectBase inst, TArg arg) =&gt; ((TargetType)inst).Method(arg).
    /// </summary>
    public static Action<Il2CppObjectBase, TArg>? CreateAction<TArg>(Type targetType, MethodInfo method)
    {
        try
        {
            var instParam = Expression.Parameter(typeof(Il2CppObjectBase), "inst");
            var argParam = Expression.Parameter(typeof(TArg), "arg");
            var castInst = Expression.Convert(instParam, targetType);
            var call = Expression.Call(castInst, method, argParam);
            return Expression.Lambda<Action<Il2CppObjectBase, TArg>>(call, instParam, argParam).Compile();
        }
        catch
        {
            return (inst, arg) => method.Invoke(inst, new object?[] { arg });
        }
    }

    /// <summary>
    /// Compiles a fast property-by-property cloner for an IL2CPP class type so any new fields added to a data model
    /// in future game updates are automatically copied from the template object.
    /// </summary>
    public static Action<T, T> CreatePropertyCloner<T>() where T : class
    {
        var actions = new List<Action<T, T>>();
        foreach (var prop in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (BasePropertyNames.Contains(prop.Name)) continue;
            if (prop.GetMethod == null || prop.SetMethod == null) continue;
            if (prop.GetIndexParameters().Length > 0) continue;

            try
            {
                var srcParam = Expression.Parameter(typeof(T), "src");
                var dstParam = Expression.Parameter(typeof(T), "dst");
                var assign = Expression.Assign(Expression.Property(dstParam, prop), Expression.Property(srcParam, prop));
                actions.Add(Expression.Lambda<Action<T, T>>(assign, srcParam, dstParam).Compile());
            }
            catch
            {
                // Ignore properties that cannot be assigned directly.
            }
        }

        var copyArray = actions.ToArray();
        return (src, dst) =>
        {
            if (src == null || dst == null) return;
            for (int i = 0; i < copyArray.Length; i++)
            {
                try { copyArray[i](src, dst); }
                catch { }
            }
        };
    }
}

/// <summary>
/// Marks a patch the game cannot run without in offline mode. If it fails or is skipped, the summary prints an error
/// naming what breaks, instead of the patch being one more line in the failure count.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CriticalPatchAttribute : Attribute
{
    public string Consequence { get; }
    public CriticalPatchAttribute(string consequence) => Consequence = consequence;
}
