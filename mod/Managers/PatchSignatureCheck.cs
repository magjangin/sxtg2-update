using System;
using System.Collections.Generic;
using System.Reflection;

namespace SixtarGateMod.Managers;

/// <summary>
/// 패치 메서드(Prefix/Postfix/Finalizer) 인자와 훅 대상 시그니처 대조 (2026-11-19).
///
/// Harmony는 패치 인자를 <b>이름</b>으로만 대상 인자에 묶고 형식은 확인하지 않는다. 게임 업데이트로 대상 인자의
/// 형식이 바뀌면(예: <c>int</c> → <c>long</c>, <c>float</c> → 구조체, <c>Action&lt;int&gt;</c> → <c>Action&lt;long&gt;</c>)
/// 패치는 그대로 붙고, 첫 호출에서 크기가 다른 값을 읽어 쓰레기 값·스택 손상·네이티브 크래시가 난다.
/// <see cref="PatchPreflight"/>(사라진 멤버)와 ICF 검사(공유 주소)로는 잡히지 않는 경우라 따로 대조한다.
///
/// 판정:
/// - <b>오류(패치 건너뜀)</b>: 대상에 없는 인자 이름, 값 형식 불일치(같은 크기의 정수/열거형끼리는 허용),
///   정적 대상에 <c>__instance</c>, void 대상에 <c>__result</c>, 값 형식 <c>__result</c> 불일치.
/// - <b>경고(패치는 붙임)</b>: 참조 형식이 서로 대입 불가. 인터롭 래퍼는 포인터만 감싸므로 바로 크래시는 아니지만
///   잘못된 형식으로 멤버를 읽으면 엉뚱한 필드를 읽는다.
///
/// MelonLoader·IL2CPP 의존이 없는 순수 리플렉션이라 tests/에서 일반 .NET 형식으로 시험한다.
/// </summary>
public static class PatchSignatureCheck
{
    private static readonly string[] PatchKinds = { "Prefix", "Postfix", "Finalizer" };

    private const BindingFlags StaticDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Harmony가 이름으로 따로 채워 주는 특수 인자(대상 시그니처와 무관).</summary>
    private static readonly HashSet<string> SpecialNames = new(StringComparer.Ordinal)
    {
        "__state", "__exception", "__originalMethod", "__runOriginal", "__args",
    };

    /// <summary>
    /// 패치 클래스의 Prefix/Postfix/Finalizer를 모든 대상과 대조한다. 오류가 있으면 첫 사유를, 없으면 null.
    /// 경고는 <paramref name="warnings"/>에 모은다.
    /// </summary>
    public static string? Check(Type patchType, IReadOnlyList<MethodBase> targets, List<string> warnings)
    {
        foreach (var kind in PatchKinds)
        {
            foreach (var patch in patchType.GetMethods(StaticDeclared))
            {
                if (!string.Equals(patch.Name, kind, StringComparison.Ordinal)) continue;
                foreach (var target in targets)
                {
                    string? error = CheckMethod(target, patch, warnings);
                    if (error != null) return $"{kind} ↔ {Describe(target)}: {error}";
                }
            }
        }
        return null;
    }

    /// <summary>
    /// 업데이트로 같은 이름의 오버로드가 생겨 인자 형식 없는 <c>[HarmonyPatch(typeof(X), "M")]</c>가 모호해졌을 때,
    /// 패치 자신의 인자 목록을 시그니처 단서로 써서 대조를 통과하는 오버로드를 고른다(2026-11-19).
    /// 오류 없이 통과하는 후보가 하나면 그것, 여럿이면 경고까지 없는 후보가 하나일 때만 그것, 아니면 null(고르지 않음).
    /// 패치가 대상 인자를 하나도 받지 않으면 모든 후보가 통과하므로 null이 된다.
    /// </summary>
    public static MethodInfo? PickOverload(Type patchType, IReadOnlyList<MethodInfo> candidates, out string note)
    {
        var passing = new List<MethodInfo>();
        var clean = new List<MethodInfo>();
        foreach (var c in candidates)
        {
            var w = new List<string>();
            if (Check(patchType, new MethodBase[] { c }, w) != null) continue;
            passing.Add(c);
            if (w.Count == 0) clean.Add(c);
        }

        MethodInfo? pick = passing.Count == 1 ? passing[0] : clean.Count == 1 ? clean[0] : null;
        note = pick != null
            ? $"오버로드 {candidates.Count}개 중 패치 인자와 맞는 ({ParamList(pick.GetParameters())})를 고름"
            : $"오버로드 {candidates.Count}개 중 패치 인자와 맞는 것이 {passing.Count}개라 고르지 않음";
        return pick;
    }

    /// <summary>
    /// 인자 형식을 지정한 <c>[HarmonyPatch(typeof(X), "M", new[] { ... })]</c>의 대상이 업데이트로 시그니처가 바뀌어
    /// 정확히 맞는 메서드가 없을 때(예: <c>GotoPlayScene(ITrack, ELevels, EPlayStyle)</c>에 인자가 하나 늘어남)의 대체 해석
    /// (2026-11-23). 같은 이름 메서드가 <b>하나뿐</b>이고 그 메서드가 패치 인자 대조를 오류 없이 통과할 때만 고른다.
    /// 같은 이름이 여럿 남아 있으면 지정했던 오버로드만 사라지고 뜻이 다른 형제가 남은 것일 수 있어 고르지 않는다
    /// (예: <c>Resume(channel)</c>이 없어지고 <c>Resume(time, channel)</c>만 남음).
    /// </summary>
    public static MethodInfo? PickChangedSignature(Type patchType, IReadOnlyList<MethodInfo> candidates, out string note)
    {
        if (candidates.Count == 0)
        {
            note = "같은 이름의 메서드가 없음(이름이 바뀌었거나 삭제됨)";
            return null;
        }
        if (candidates.Count > 1)
        {
            note = $"같은 이름 메서드가 {candidates.Count}개라 고르지 않음(지정한 오버로드만 사라졌을 수 있음)";
            return null;
        }
        var only = candidates[0];
        var w = new List<string>();
        string? error = Check(patchType, new MethodBase[] { only }, w);
        if (error != null)
        {
            note = $"남은 메서드 ({ParamList(only.GetParameters())})가 패치 인자와 맞지 않음: {error}";
            return null;
        }
        note = $"인자 형식이 바뀐 메서드 ({ParamList(only.GetParameters())})를 패치 인자로 확인해 고름" +
               (w.Count > 0 ? $" (경고 {w.Count}개)" : "");
        return only;
    }

    /// <summary>
    /// <paramref name="declType"/>부터 기반 형식으로 올라가며 이름이 같은 비제네릭 메서드를 모은다(처음 찾은 형식에서 멈춤).
    /// 부팅 시 해석(<c>PatchResilience</c>)과 오프라인 대조(<c>tools/sigcheck</c>)가 같은 후보를 쓰도록 여기 둔다.
    /// </summary>
    public static List<MethodInfo> SameNameMethods(Type declType, string methodName)
    {
        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                 BindingFlags.Static | BindingFlags.DeclaredOnly;
        var candidates = new List<MethodInfo>();
        for (Type? t = declType; t != null && candidates.Count == 0; t = t.BaseType)
            foreach (var m in t.GetMethods(all))
                if (string.Equals(m.Name, methodName, StringComparison.Ordinal) && !m.IsGenericMethodDefinition)
                    candidates.Add(m);
        return candidates;
    }

    /// <summary>패치 메서드 하나를 대상 하나와 대조. 오류 사유 또는 null.</summary>
    public static string? CheckMethod(MethodBase target, MethodInfo patch, List<string> warnings)
    {
        ParameterInfo[] targetParams = target.GetParameters();
        foreach (var p in patch.GetParameters())
        {
            string name = p.Name ?? "";
            Type pt = Strip(p.ParameterType);

            if (SpecialNames.Contains(name)) continue;

            if (name == "__instance")
            {
                if (target.IsStatic) return "정적 메서드인데 패치가 __instance를 받음";
                Type? decl = target.DeclaringType;
                if (decl != null)
                {
                    if (pt.IsValueType != decl.IsValueType)
                        return $"__instance 형식이 바뀜: 패치 {pt.Name} ↔ 대상 클래스 {decl.Name} (값/참조 형식 불일치)";
                    if (!pt.IsAssignableFrom(decl) && !decl.IsAssignableFrom(pt))
                        warnings.Add($"{Describe(target)}: __instance 형식 {pt.Name} ↔ 대상 클래스 {decl.Name}");
                }
                continue;
            }

            if (name == "__result")
            {
                if (target is not MethodInfo mi || mi.ReturnType == typeof(void))
                    return "반환값이 없는(void) 대상인데 패치가 __result를 받음";
                string? why = Compare(pt, Strip(mi.ReturnType), p.ParameterType.IsByRef, "__result", target, warnings);
                if (why != null) return why;
                continue;
            }

            // ___field(필드 주입): 대상 클래스에 해당 필드/프로퍼티가 존재하는지 확인
            if (name.StartsWith("___", StringComparison.Ordinal))
            {
                string fieldName = name.Substring(3);
                if (fieldName.Length == 0)
                    return "빈 필드 주입 인자 '___'";
                Type? decl = target.DeclaringType;
                if (decl != null)
                {
                    Type? memberType = FindInjectedMemberType(decl, fieldName);
                    if (memberType == null)
                        return $"필드 주입 '{name}'의 대상 멤버 '{fieldName}'가 {decl.Name}에 없음";
                    string? fieldErr = Compare(pt, memberType, p.ParameterType.IsByRef, name, target, warnings);
                    if (fieldErr != null) return fieldErr;
                }
                continue;
            }

            // __0, __1 등 위치 인자: 대상의 idx번째 인자 형식과 대조
            if (name.StartsWith("__", StringComparison.Ordinal))
            {
                if (int.TryParse(name.AsSpan(2), out int idx) && idx >= 0)
                {
                    if (idx >= targetParams.Length)
                        return $"위치 인자 '{name}'가 대상 인자 수({targetParams.Length})를 벗어남 (대상 인자: {ParamList(targetParams)})";
                    string? posErr = Compare(pt, Strip(targetParams[idx].ParameterType), p.ParameterType.IsByRef, name, target, warnings);
                    if (posErr != null) return posErr;
                }
                else
                {
                    warnings.Add($"{Describe(target)}: 알 수 없는 Harmony 특수 인자 '{name}' (오타 확인 필요)");
                }
                continue;
            }

            ParameterInfo? tp = null;
            foreach (var cand in targetParams)
                if (string.Equals(cand.Name, name, StringComparison.Ordinal)) { tp = cand; break; }
            if (tp == null)
                return $"대상에 '{name}' 인자가 없음 (대상 인자: {ParamList(targetParams)})";

            string? err = Compare(pt, Strip(tp.ParameterType), p.ParameterType.IsByRef, name, target, warnings);
            if (err != null) return err;
        }
        return null;
    }

    private static Type? FindInjectedMemberType(Type decl, string name)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
        for (Type? t = decl; t != null; t = t.BaseType)
        {
            var f = t.GetField(name, flags | BindingFlags.DeclaredOnly);
            if (f != null) return f.FieldType;
            // Il2CppInterop은 네이티브 필드를 프로퍼티로 노출한다
            var prop = t.GetProperty(name, flags | BindingFlags.DeclaredOnly)
                    ?? t.GetProperty($"_{name}_k__BackingField", flags | BindingFlags.DeclaredOnly);
            if (prop != null) return prop.PropertyType;
        }
        return null;
    }

    private static string? Compare(Type patchType, Type targetType, bool patchByRef, string name, MethodBase target, List<string> warnings)
    {
        if (patchType == targetType) return null;
        if (patchType == typeof(object))
        {
            if (patchByRef && targetType.IsValueType)
                return $"'{name}' ref object로 값 형식 {targetType.Name}을 받을 수 없음";
            return null; // 값 전달 object(박싱) 또는 참조 형식 object는 Harmony가 처리
        }

        if (patchType.IsPointer || targetType.IsPointer)
            return $"'{name}' 포인터 형식이 바뀜: 패치 {patchType.Name} ↔ 대상 {targetType.Name}";

        if (patchType.IsValueType || targetType.IsValueType)
        {
            if (SameSizeInteger(patchType, targetType)) return null;
            return $"'{name}' 형식이 바뀜: 패치 {patchType.Name} ↔ 대상 {targetType.Name} (값 형식 크기·배치가 달라 잘못된 값을 읽음)";
        }

        if (!patchType.IsAssignableFrom(targetType) && !targetType.IsAssignableFrom(patchType))
            warnings.Add($"{Describe(target)}: '{name}' 참조 형식 불일치 (패치 {patchType.Name} ↔ 대상 {targetType.Name})");
        return null;
    }

    /// <summary>열거형·정수형끼리 크기가 같으면 같은 값으로 본다(예: 열거형 인자를 int로 받는 패치).</summary>
    private static bool SameSizeInteger(Type a, Type b)
    {
        int sa = IntegerSize(a), sb = IntegerSize(b);
        return sa > 0 && sa == sb;
    }

    private static int IntegerSize(Type t)
    {
        if (t.IsEnum) t = Enum.GetUnderlyingType(t);
        return Type.GetTypeCode(t) switch
        {
            TypeCode.Byte or TypeCode.SByte => 1,
            TypeCode.Int16 or TypeCode.UInt16 => 2,
            TypeCode.Int32 or TypeCode.UInt32 => 4,
            TypeCode.Int64 or TypeCode.UInt64 => 8,
            _ => 0,
        };
    }

    private static Type Strip(Type t) => t.IsByRef ? t.GetElementType() ?? t : t;

    private static string ParamList(ParameterInfo[] ps)
    {
        if (ps.Length == 0) return "없음";
        var parts = new string[ps.Length];
        for (int i = 0; i < ps.Length; i++) parts[i] = $"{Strip(ps[i].ParameterType).Name} {ps[i].Name}";
        return string.Join(", ", parts);
    }

    private static string Describe(MethodBase m) => $"{m.DeclaringType?.Name ?? "?"}.{m.Name}";
}
