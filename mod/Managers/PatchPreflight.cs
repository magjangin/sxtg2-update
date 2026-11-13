using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;

namespace SixtarGateMod.Managers;

/// <summary>
/// 패치 적용 전 사전 JIT 검사 (2026-11-14).
///
/// 게임이 업데이트되면 MelonLoader가 인터롭 어셈블리(Il2CppAssemblies)를 새로 만든다. 모드가 쓰던 필드·메서드·속성이
/// 사라지거나 형식이 바뀌어도 Harmony 적용은 성공하고, 문제는 그 Prefix/Postfix가 <b>처음 불릴 때</b> JIT에서
/// <c>MissingFieldException</c>/<c>MissingMethodException</c>으로 터진다. IL2CPP 트램펄린이 이 예외를 잡아
/// "During invoking native->managed trampoline" 오류만 남기고 게임은 원본 코드를 반쯤 실행한 상태로 계속 간다
/// (예: 플레이 도중 판정 훅이 매번 실패).
///
/// 그래서 패치 클래스의 모든 메서드와 거기서 부르는 모드 내부 메서드(IL의 call/callvirt/newobj/ldftn을 따라감)를
/// <see cref="RuntimeHelpers.PrepareMethod(RuntimeMethodHandle)"/>로 미리 JIT한다. 사라진 멤버는 이때 예외로 드러나므로
/// 그 패치를 붙이지 않고 무엇이 없는지 로그에 남긴다. 덤으로 훅의 첫 호출 JIT(플레이 시작 직후 끊김)도 부팅으로 옮겨진다.
///
/// 실험(scratchpad, .NET 6): 라이브러리에서 필드·메서드·정적 속성을 지운 뒤 PrepareMethod → 각각 MissingFieldException,
/// MissingMethodException, MissingMethodException(get_…). 멀쩡한 메서드는 통과.
/// </summary>
public static class PatchPreflight
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>메서드별 결과(null = 통과). 여러 패치가 같은 도우미를 불러도 한 번만 JIT.</summary>
    private static readonly Dictionary<MethodBase, string?> Verdicts = new();

    /// <summary>메서드별 IL 피호출자·토큰 검사 캐시. 부팅 중 각 메서드의 IL을 한 번만 읽는다.</summary>
    private static readonly Dictionary<MethodBase, (List<MethodBase> Callees, string? ResolveError)> CalleeCache = new();

    /// <summary>서브그래프 전체가 통과한 메서드 집합(재귀·공유 도우미 재탐색 생략).</summary>
    private static readonly HashSet<MethodBase> CleanSubgraphs = new();

    private static Dictionary<short, OpCode>? _opcodes;

    public static int MethodsChecked => Verdicts.Count;

    /// <summary>
    /// 패치 클래스(중첩 형식 포함)와 그것이 부르는 모드 내부 메서드를 미리 JIT. 문제가 없으면 null, 있으면 사유.
    /// </summary>
    public static string? Check(Type patchType)
    {
        var module = patchType.Module;
        var queue = new Queue<MethodBase>();
        var seen = new HashSet<MethodBase>();
        EnqueueDeclared(patchType, queue, seen);

        while (queue.Count > 0)
        {
            var m = queue.Dequeue();
            if (CleanSubgraphs.Contains(m)) continue;

            string? why = Prepare(m);
            if (why != null) return $"{Describe(m)}: {why}";

            var (callees, resolveError) = GetOrParseCallees(m);
            if (resolveError != null) return $"{Describe(m)}: {resolveError}";
            foreach (var callee in callees)
            {
                if (callee.Module == module && !CleanSubgraphs.Contains(callee) && seen.Add(callee))
                    queue.Enqueue(callee);
            }
        }

        // 도달 가능한 모든 메서드가 통과했으므로 이번 패치에서 확인한 서브그래프 노드들은 다시 훑지 않는다.
        foreach (var m in seen)
            CleanSubgraphs.Add(m);
        return null;
    }

    private static void EnqueueDeclared(Type type, Queue<MethodBase> queue, HashSet<MethodBase> seen)
    {
        if (type.ContainsGenericParameters) return;
        foreach (var m in type.GetMethods(Declared))
            if (seen.Add(m)) queue.Enqueue(m);
        foreach (var c in type.GetConstructors(Declared))
            if (seen.Add(c)) queue.Enqueue(c);
        foreach (var nested in type.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            EnqueueDeclared(nested, queue, seen);
    }

    private static string? Prepare(MethodBase m)
    {
        if (Verdicts.TryGetValue(m, out var cached)) return cached;
        string? verdict = null;
        try
        {
            bool skip = m.IsAbstract || m.ContainsGenericParameters || m.DeclaringType?.ContainsGenericParameters == true
                        || m.GetMethodBody() == null;
            if (!skip) RuntimeHelpers.PrepareMethod(m.MethodHandle);
        }
        catch (TypeInitializationException ex)
        {
            verdict = $"정적 초기화 실패 ({ex.InnerException?.GetType().Name}: {ex.InnerException?.Message})";
        }
        catch (Exception ex)
        {
            verdict = $"{ex.GetType().Name}: {ex.Message}";
        }
        Verdicts[m] = verdict;
        return verdict;
    }

    /// <summary>IL을 명령 단위로 읽어 call/callvirt/newobj/ldftn/ldvirtftn 및 필드·타입 토큰을 검사·수집한다.</summary>
    private static (List<MethodBase> Callees, string? ResolveError) GetOrParseCallees(MethodBase m)
    {
        if (CalleeCache.TryGetValue(m, out var cached))
            return cached;

        string? resolveError = null;
        var result = new List<MethodBase>();
        byte[]? il;
        try { il = m.GetMethodBody()?.GetILAsByteArray(); }
        catch
        {
            var empty = (result, (string?)null);
            CalleeCache[m] = empty;
            return empty;
        }
        if (il == null)
        {
            var empty = (result, (string?)null);
            CalleeCache[m] = empty;
            return empty;
        }

        var ops = Opcodes();
        Type[]? typeArgs = m.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : null;
        Type[]? methodArgs = m.IsGenericMethod ? m.GetGenericArguments() : null;

        for (int i = 0; i < il.Length;)
        {
            short value = il[i] == 0xFE && i + 1 < il.Length ? (short)(0xFE00 | il[i + 1]) : il[i];
            if (!ops.TryGetValue(value, out var op)) break; // 알 수 없는 바이트: 더 읽지 않음(검사만 줄어듦)
            i += op.Size;

            if (i + 4 <= il.Length && op.OperandType is OperandType.InlineMethod or OperandType.InlineField or OperandType.InlineType or OperandType.InlineTok)
            {
                int token = BitConverter.ToInt32(il, i);
                if (TryResolveToken(m.Module, op.OperandType, token, typeArgs, methodArgs, out MethodBase? callee, ref resolveError) && callee != null)
                {
                    // 제네릭 인스턴스는 정의로 바꿔 IL을 따라감(JIT 검사는 정의에서 건너뜀)
                    if (callee is MethodInfo mi && mi.IsGenericMethod && !mi.IsGenericMethodDefinition)
                        callee = mi.GetGenericMethodDefinition();
                    result.Add(callee);
                }
            }
            i += OperandSize(op.OperandType, il, i);
        }

        var entry = (result, resolveError);
        CalleeCache[m] = entry;
        return entry;
    }

    private static bool TryResolveToken(
        Module module, OperandType opType, int token, Type[]? typeArgs, Type[]? methodArgs,
        out MethodBase? callee, ref string? resolveError)
    {
        callee = null;
        try
        {
            switch (opType)
            {
                case OperandType.InlineMethod:
                    callee = module.ResolveMethod(token, typeArgs, methodArgs);
                    return callee != null;
                case OperandType.InlineField:
                    module.ResolveField(token, typeArgs, methodArgs);
                    return false;
                case OperandType.InlineType:
                    module.ResolveType(token, typeArgs, methodArgs);
                    return false;
                case OperandType.InlineTok:
                    module.ResolveMember(token, typeArgs, methodArgs);
                    return false;
            }
        }
        catch (Exception ex) when (ex is MissingMemberException or TypeLoadException or BadImageFormatException)
        {
            resolveError ??= $"{ex.GetType().Name}: {ex.Message}";
        }
        catch { }
        return false;
    }

    private static int OperandSize(OperandType t, byte[] il, int at) => t switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => at + 4 <= il.Length ? 4 + 4 * BitConverter.ToInt32(il, at) : il.Length,
        _ => 4,
    };

    private static Dictionary<short, OpCode> Opcodes()
    {
        if (_opcodes != null) return _opcodes;
        var map = new Dictionary<short, OpCode>();
        foreach (var f in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (f.GetValue(null) is OpCode op) map[op.Value] = op;
        return _opcodes = map;
    }

    private static string Describe(MethodBase m)
    {
        string type = m.DeclaringType?.Name ?? "?";
        if (m.DeclaringType?.DeclaringType is Type outer) type = outer.Name + "." + type;
        return type + "." + m.Name;
    }
}
