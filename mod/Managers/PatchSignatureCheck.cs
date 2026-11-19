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
                if (decl != null && !pt.IsAssignableFrom(decl) && !decl.IsAssignableFrom(pt))
                    warnings.Add($"{Describe(target)}: __instance 형식 {pt.Name} ↔ 대상 클래스 {decl.Name}");
                continue;
            }

            if (name == "__result")
            {
                if (target is not MethodInfo mi || mi.ReturnType == typeof(void))
                    return "반환값이 없는(void) 대상인데 패치가 __result를 받음";
                string? why = Compare(pt, Strip(mi.ReturnType), "__result", target, warnings);
                if (why != null) return why;
                continue;
            }

            // ___field(필드 주입), __0(위치 인자) 등 나머지 특수 형식은 대상 인자와 무관
            if (name.StartsWith("__", StringComparison.Ordinal)) continue;

            ParameterInfo? tp = null;
            foreach (var cand in targetParams)
                if (string.Equals(cand.Name, name, StringComparison.Ordinal)) { tp = cand; break; }
            if (tp == null)
                return $"대상에 '{name}' 인자가 없음 (대상 인자: {ParamList(targetParams)})";

            string? err = Compare(pt, Strip(tp.ParameterType), name, target, warnings);
            if (err != null) return err;
        }
        return null;
    }

    private static string? Compare(Type patchType, Type targetType, string name, MethodBase target, List<string> warnings)
    {
        if (patchType == targetType) return null;
        if (patchType == typeof(object)) return null; // object로 받는 패치(박싱)는 Harmony가 처리

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
            TypeCode.Boolean or TypeCode.Byte or TypeCode.SByte => 1,
            TypeCode.Int16 or TypeCode.UInt16 or TypeCode.Char => 2,
            TypeCode.Int32 or TypeCode.UInt32 => 4,
            TypeCode.Int64 or TypeCode.UInt64 => 8,
            _ => 0,
        };
    }

    private static Type Strip(Type t) => t.IsByRef || t.IsPointer ? t.GetElementType() ?? t : t;

    private static string ParamList(ParameterInfo[] ps)
    {
        if (ps.Length == 0) return "없음";
        var parts = new string[ps.Length];
        for (int i = 0; i < ps.Length; i++) parts[i] = $"{Strip(ps[i].ParameterType).Name} {ps[i].Name}";
        return string.Join(", ", parts);
    }

    private static string Describe(MethodBase m) => $"{m.DeclaringType?.Name ?? "?"}.{m.Name}";
}
