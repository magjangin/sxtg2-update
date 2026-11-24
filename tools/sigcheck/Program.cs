// 사용: dotnet run --project tools/sigcheck -- [SixtarGateMod.dll] [게임 폴더]
// 종료 코드: 0 = 시그니처 오류 없음, 1 = 오류(부팅 시 그 패치는 건너뛰어짐), 2 = 실행 실패.
// TargetMethod가 LambdaTranslator(네이티브 기계어 대조)를 쓰는 클로저 패치는 IL2CPP 런타임 없이 대상을 못 구해
// "대상 미확인"으로 센다(부팅 시에는 검사됨).
using System.Reflection;
using HarmonyLib;

string repo = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
string modDll = args.Length > 0 ? args[0] : Path.Combine(repo, "mod", "bin", "Release", "net6.0", "SixtarGateMod.dll");
string game = args.Length > 1 ? args[1]
    : Environment.GetEnvironmentVariable("SIXTAR_GAME_DIR") is { Length: > 0 } env ? env
    : @"H:\steam\steamapps\common\Sixtar Gate STARTRAIL";
if (!File.Exists(modDll)) { Console.Error.WriteLine($"모드 DLL 없음: {modDll} (먼저 dotnet build mod/SixtarGateMod.csproj -c Release)"); return 2; }
string interop = Path.Combine(game, "MelonLoader", "Il2CppAssemblies");
if (!Directory.Exists(interop)) { Console.Error.WriteLine($"인터롭 폴더 없음: {interop} (게임을 한 번 실행해 MelonLoader가 만들게 하세요)"); return 2; }

string[] dirs = { Path.GetDirectoryName(Path.GetFullPath(modDll))!, Path.Combine(game, "MelonLoader", "net6"), interop };
AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
{
    string file = new AssemblyName(e.Name).Name + ".dll";
    foreach (var d in dirs) { var p = Path.Combine(d, file); if (File.Exists(p)) return Assembly.LoadFrom(p); }
    return null;
};

var asm = Assembly.LoadFrom(modDll);
var check = asm.GetType("SixtarGateMod.Managers.PatchSignatureCheck")?.GetMethod("Check");
if (check == null) { Console.Error.WriteLine("이 모드 DLL에는 PatchSignatureCheck가 없습니다."); return 2; }
var pickOverload = check.DeclaringType!.GetMethod("PickOverload");
var pickChanged = check.DeclaringType!.GetMethod("PickChangedSignature");
var sameName = check.DeclaringType!.GetMethod("SameNameMethods");

Type[] types;
try { types = asm.GetTypes(); }
catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }

const BindingFlags Flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
int ok = 0, errors = 0, warnings = 0, unresolved = 0, overloaded = 0, changed = 0;
foreach (var t in types.OrderBy(t => t.FullName, StringComparer.Ordinal))
{
    if (t.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
    var targets = new List<MethodBase>();
    try
    {
        var tm = t.GetMethod("TargetMethod", Flags);
        var tms = tm == null ? t.GetMethod("TargetMethods", Flags) : null;
        if (tm != null)
        {
            if (tm.Invoke(null, null) is MethodBase mb) targets.Add(mb);
        }
        else if (tms != null)
        {
            if (tms.Invoke(null, null) is System.Collections.IEnumerable seq)
                foreach (var item in seq)
                    if (item is MethodBase mb) targets.Add(mb);
        }
        else
        {
            Type? decl = null; string? name = null; MethodType kind = MethodType.Normal; Type[]? argTypes = null;
            foreach (HarmonyPatch a in t.GetCustomAttributes(typeof(HarmonyPatch), false))
            {
                var i = a.info;
                if (i.declaringType != null) decl = i.declaringType;
                if (!string.IsNullOrEmpty(i.methodName)) name = i.methodName;
                if (i.methodType.HasValue) kind = i.methodType.Value;
                if (i.argumentTypes != null) argTypes = i.argumentTypes;
            }
            MethodBase? r;
            try
            {
                r = decl == null ? null : kind switch
                {
                    MethodType.Constructor => AccessTools.Constructor(decl, argTypes),
                    MethodType.Getter => name == null ? null : AccessTools.PropertyGetter(decl, name),
                    MethodType.Setter => name == null ? null : AccessTools.PropertySetter(decl, name),
                    _ => name == null ? null : AccessTools.Method(decl, name, argTypes),
                };
            }
            catch (AmbiguousMatchException) when (kind == MethodType.Normal && argTypes == null && decl != null && name != null && pickOverload != null && sameName != null)
            {
                // 부팅 시와 같은 해석: 패치 인자 목록으로 오버로드를 고름(PatchResilience.PickOverloadForPatch, 기반 형식까지 탐색)
                var candidates = (List<MethodInfo>)sameName.Invoke(null, new object[] { decl, name })!;
                var args2 = new object?[] { t, candidates, null };
                r = (MethodInfo?)pickOverload.Invoke(null, args2);
                Console.WriteLine($"  오버로드     {t.Name}: {args2[2]}");
                if (r != null) overloaded++;
            }
            // 부팅 시와 같은 해석(PatchResilience.PickChangedSignatureForPatch): 지정한 인자 형식의 메서드가 없으면
            // 같은 이름이 하나뿐이고 패치 인자와 맞을 때 그것을 고름(2026-11-23)
            if (r == null && kind == MethodType.Normal && argTypes != null && decl != null && name != null
                && pickChanged != null && sameName != null)
            {
                var cands = (List<MethodInfo>)sameName.Invoke(null, new object[] { decl, name })!;
                var args3 = new object?[] { t, cands, null };
                r = (MethodInfo?)pickChanged.Invoke(null, args3);
                Console.WriteLine($"  시그니처변경 {t.Name}: {decl.Name}.{name}({string.Join(", ", argTypes.Select(a => a.Name))}) 없음 — {args3[2]}");
                if (r != null) changed++;
            }
            if (r != null) targets.Add(r);
        }
    }
    catch (Exception ex)
    {
        var inner = ex is TargetInvocationException { InnerException: { } ie } ? ie : ex;
        Console.WriteLine($"  대상 미확인  {t.Name}: {inner.GetType().Name}");
        unresolved++;
        continue;
    }
    if (targets.Count == 0) { Console.WriteLine($"  대상 없음    {t.Name} (부팅 시 '대상 메서드를 찾지 못해 건너뜀')"); errors++; continue; }

    var warn = new List<string>();
    string? result;
    try { result = (string?)check.Invoke(null, new object[] { t, targets, warn }); }
    catch (Exception ex)
    {
        var inner = ex is TargetInvocationException { InnerException: { } ie } ? ie : ex;
        Console.WriteLine($"  오류        {t.Name}: 형식 로드 실패 {inner.GetType().Name}: {inner.Message}");
        errors++;
        continue;
    }
    foreach (var w in warn) { Console.WriteLine($"  경고        {t.Name}: {w}"); warnings++; }
    if (result != null) { Console.WriteLine($"  오류        {t.Name}: {result}"); errors++; }
    else ok++;
}

Console.WriteLine($"패치 시그니처 대조: 통과 {ok}, 오류 {errors}, 경고 {warnings}, 대상 미확인(클로저·런타임 필요) {unresolved}, 오버로드로 고름 {overloaded}, 바뀐 시그니처로 고름 {changed}");
return errors > 0 ? 1 : 0;
