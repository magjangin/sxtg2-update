// 모드 본체 net6.0 컴파일 검사 (게임 DLL 불필요)
//
// 왜: CI는 순수 모듈 단위 테스트(net8.0)만 돌리고 모드 본체(net6.0 + 게임 인터롭 참조)는 빌드하지 못한다.
//     그래서 .NET 7+ 전용 API(예: Stopwatch.GetElapsedTime), 모드 내부 타입 오타, 문법 오류처럼
//     "게임 DLL과 무관한" 본체 컴파일 오류도 게임이 있는 PC에서 빌드할 때까지 드러나지 않았다.
//
// 어떻게:
//   1) mod/**/*.cs 를 SixtarGateMod.csproj와 같은 설정(LangVersion latest, Nullable, unsafe, ImplicitUsings)으로
//      net6.0 참조 어셈블리 + 공개 NuGet의 MelonLoader/HarmonyX/Il2CppInterop 에 대고 컴파일한다.
//   2) 같은 소스를 net8.0 참조 어셈블리로도 한 번 더 컴파일한다.
//   3) net6에서만 나는 오류 = .NET 7+ 전용 API 사용 → 보고.
//      양쪽 다 나는 오류 중 "심볼을 못 찾음" 계열(CS0246 등)은 게임 인터롭이 없어서 생기는 것으로 보고 거른다.
//      그 밖의 오류(모드 자체 타입의 멤버 오타, 형식 불일치, 문법 오류 등)는 보고.
//   Roslyn은 오류 형식(못 찾은 게임 타입)이 낀 식에서 연쇄 오류를 내지 않으므로, 게임 타입을 거치지 않는
//   코드만 실제로 검사된다.
//
// 한계(추정 포함):
//   - 게임 타입의 멤버 이름/시그니처가 맞는지는 볼 수 없다(인터롭이 없음). 그건 로컬 빌드와 tools/sigcheck 몫.
//   - 모드 코드가 존재하지 않는 BCL 타입을 쓰면(오타) "심볼 못 찾음"으로 분류돼 걸러질 수 있다.
//     단, net8에는 있는 이름이면 net6 전용 오류로 잡힌다.
//   - NuGet의 MelonLoader/Il2CppInterop 버전은 게임 폴더의 것과 미세하게 다를 수 있다.
//
// 사용: dotnet run --project tools/bodycheck -c Release -- [저장소 루트] [--interop <Il2CppAssemblies 폴더>] [--self-test]
//   --interop 를 주면 게임 인터롭도 참조에 넣고 모든 오류를 보고한다(로컬 전체 검사).
//   --self-test 는 일부러 깨뜨린 코드 조각을 섞어 검사기가 그것을 잡는지 확인한다.
// 종료 코드: 0 = 통과, 1 = 보고할 오류 있음, 2 = 실행 오류(참조 못 찾음 등)

using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

internal static class Program
{
    // 참조가 없어 심볼을 못 찾을 때 나는 오류. 게임 인터롭이 없는 CI에서는 이것들을 거른다.
    private static readonly HashSet<string> MissingSymbolIds = new()
    {
        "CS0246", // 형식 또는 네임스페이스 이름을 찾을 수 없음
        "CS0234", // 네임스페이스에 형식/네임스페이스가 없음 (using Il2CppSixtarGate.Play 등)
        "CS0400", // 전역 네임스페이스에서 찾을 수 없음
        "CS0012", // 참조되지 않은 어셈블리에 정의된 형식 (MelonLoader API가 UnityEngine 형식을 노출할 때)
        "CS0138", // using 지시문이 형식을 가리킴 — 못 찾은 이름과 겹칠 때
        "CS0518", // 미리 정의된 형식이 없음 (Il2CppInterop이 끌어오는 형식 등)
        "CS0616", // 특성 클래스가 아님 — 못 찾은 특성 형식에서 연쇄
    };

    private static readonly string[] ImplicitUsings =
    {
        "System", "System.Collections.Generic", "System.IO", "System.Linq",
        "System.Net.Http", "System.Threading", "System.Threading.Tasks",
    };

    private static int Main(string[] args)
    {
        try { return Run(args); }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[bodycheck] 실행 오류: " + ex);
            return 2;
        }
    }

    private static int Run(string[] args)
    {
        string? repo = null, interop = null;
        bool selfTest = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--interop" && i + 1 < args.Length) interop = args[++i];
            else if (args[i] == "--self-test") selfTest = true;
            else repo = args[i];
        }
        repo ??= FindRepoRoot();
        string modDir = Path.Combine(repo, "mod");
        if (!Directory.Exists(modDir)) throw new DirectoryNotFoundException("mod 폴더 없음: " + modDir);

        string sep = Path.DirectorySeparatorChar.ToString();
        var files = Directory.EnumerateFiles(modDir, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains(sep + "obj" + sep) && !f.Contains(sep + "bin" + sep))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        var sources = files.Select(f => (Path: Path.GetRelativePath(repo, f).Replace('\\', '/'), Text: File.ReadAllText(f))).ToList();
        sources.Add(("<implicit-usings>", string.Join("\n", ImplicitUsings.Select(u => $"global using global::{u};"))));
        if (selfTest) sources.AddRange(SelfTestSources);

        string pkgRoot = NuGetPackageRoot();
        var thirdParty = new List<string>
        {
            PackageDll(pkgRoot, "lavagang.melonloader", "0.7.3", "MelonLoader.dll"),
            PackageDll(pkgRoot, "harmonyx", "2.10.2", "0Harmony.dll"),
            PackageDll(pkgRoot, "il2cppinterop.runtime", "1.5.1", "Il2CppInterop.Runtime.dll"),
            PackageDll(pkgRoot, "il2cppinterop.common", "1.5.1", "Il2CppInterop.Common.dll"),
        };
        if (interop != null)
        {
            if (!Directory.Exists(interop)) throw new DirectoryNotFoundException("인터롭 폴더 없음: " + interop);
            thirdParty.AddRange(Directory.GetFiles(interop, "*.dll"));
        }

        var net6 = Compile(sources, RefPack(pkgRoot, "6.0.36"), thirdParty, "NET6_0");
        var net8 = Compile(sources, RefPack(pkgRoot, "8.0.11"), thirdParty, "NET8_0");

        var net8Keys = new HashSet<string>(net8.Errors.Select(Key));
        var models = new Dictionary<SyntaxTree, SemanticModel>();
        int tainted = 0;
        var report = new List<(string Kind, Diagnostic D)>();
        int filtered = 0;
        var missingNames = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var d in net6.Errors)
        {
            if (!net8Keys.Contains(Key(d))) { report.Add(("net6 전용(.NET 7+ API)", d)); continue; }
            if (interop == null && MissingSymbolIds.Contains(d.Id))
            {
                filtered++;
                if (d.Id is "CS0246" or "CS0234" or "CS0103") missingNames.Add(FirstQuoted(d.GetMessage()));
                continue;
            }
            if (interop == null && IsUnknownTypeReceiver(d))
            {
                // `SixtarGlobal.X` 처럼 못 찾은 이름이 멤버 접근의 왼쪽이면 게임 정적 형식으로 본다(CS0103으로 나옴).
                filtered++;
                missingNames.Add(FirstQuoted(d.GetMessage()));
                continue;
            }
            if (interop == null && IsTaintedByMissingSymbol(net6.Comp, models, d))
            {
                // 못 찾은 게임 형식이 낀 문장에서 나는 연쇄 오류(예: 'Text?' == null, 오류 형식의 .Count가 LINQ 메서드 그룹으로 잡힘)
                tainted++;
                continue;
            }
            report.Add(("컴파일 오류", d));
        }

        Console.WriteLine($"[bodycheck] 소스 {files.Count}개, 참조: net6.0 + MelonLoader/HarmonyX/Il2CppInterop" +
                          (interop != null ? " + 게임 인터롭" : " (게임 인터롭 없음)"));
        if (interop == null)
            Console.WriteLine($"[bodycheck] 게임 인터롭이 없어 거른 오류: '심볼 못 찾음' {filtered}건(이름 {missingNames.Count}종), 게임 형식이 낀 문장의 연쇄 오류 {tainted}건");

        foreach (var (kind, d) in report.OrderBy(r => r.D.Location.GetMappedLineSpan().Path).ThenBy(r => r.D.Location.GetMappedLineSpan().StartLinePosition.Line))
            Console.WriteLine($"  [{kind}] {Where(d)}: {d.Id} {d.GetMessage()}");

        if (selfTest)
        {
            // 자체 검사 조각이 기대대로 잡혔는지만 본다. 실제 소스의 오류는 위에 그대로 출력된다.
            bool net7Caught = report.Any(r => r.D.Location.GetMappedLineSpan().Path == SelfTestNet7Path && r.Kind.StartsWith("net6"));
            bool typoCaught = report.Any(r => r.D.Location.GetMappedLineSpan().Path == SelfTestTypoPath && r.Kind == "컴파일 오류");
            bool gameIgnored = !report.Any(r => r.D.Location.GetMappedLineSpan().Path == SelfTestGamePath);
            Console.WriteLine($"[bodycheck] 자체 검사: .NET 7+ API {(net7Caught ? "잡음" : "놓침")}, 모드 타입 오타 {(typoCaught ? "잡음" : "놓침")}, 게임 타입 사용 {(gameIgnored ? "거름" : "잘못 보고")}");
            int realErrors = report.Count(r => !r.D.Location.GetMappedLineSpan().Path.StartsWith("<self-test"));
            return net7Caught && typoCaught && gameIgnored && realErrors == 0 ? 0 : 1;
        }

        if (report.Count == 0)
        {
            Console.WriteLine("[bodycheck] 통과: 게임 DLL과 무관한 본체 컴파일 오류 없음");
            return 0;
        }
        Console.WriteLine($"[bodycheck] 실패: 오류 {report.Count}건");
        return 1;
    }

    private static (CSharpCompilation Comp, ImmutableArray<Diagnostic> Errors) Compile(List<(string Path, string Text)> sources, IEnumerable<string> refPack, IEnumerable<string> thirdParty, string tfmSymbol)
    {
        var symbols = new List<string> { "RELEASE", "NET", "NETCOREAPP", tfmSymbol };
        // NETx_0_OR_GREATER 계열: net6이면 NET5_0/NET6_0, net8이면 NET7_0/NET8_0까지
        int major = tfmSymbol == "NET8_0" ? 8 : 6;
        for (int v = 5; v <= major; v++) symbols.Add($"NET{v}_0_OR_GREATER");
        symbols.Add("NETCOREAPP3_1_OR_GREATER");

        var parse = new CSharpParseOptions(LanguageVersion.Latest, preprocessorSymbols: symbols);
        var trees = sources.Select(s => CSharpSyntaxTree.ParseText(s.Text, parse, s.Path)).ToList();
        var refs = refPack.Concat(thirdParty).Select(p => (MetadataReference)MetadataReference.CreateFromFile(p)).ToList();
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
            nullableContextOptions: NullableContextOptions.Enable,
            allowUnsafe: true,
            optimizationLevel: OptimizationLevel.Release);
        var comp = CSharpCompilation.Create("SixtarGateMod", trees, refs, options);
        return (comp, comp.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToImmutableArray());
    }

    private static bool IsUnknownTypeReceiver(Diagnostic d)
    {
        if (d.Id != "CS0103" || d.Location.SourceTree is not { } tree) return false;
        var node = tree.GetRoot().FindNode(d.Location.SourceSpan, getInnermostNodeForTie: true);
        return node is IdentifierNameSyntax id && id.Parent is MemberAccessExpressionSyntax ma && ma.Expression == id
               && id.Identifier.ValueText.Length > 0 && char.IsUpper(id.Identifier.ValueText[0]);
    }

    // 오류 위치를 감싸는 가장 작은 문장(중첩 문장 본문은 제외)이나 멤버 머리 부분에 못 찾은 형식이 하나라도 있으면 true.
    // 이런 문장은 게임 인터롭 없이는 바르게 검사할 수 없으므로 거른다. 같은 문장 안의 다른 실수도 함께 가려진다(한계).
    private static bool IsTaintedByMissingSymbol(CSharpCompilation comp, Dictionary<SyntaxTree, SemanticModel> models, Diagnostic d)
    {
        var tree = d.Location.SourceTree;
        if (tree == null) return false;
        if (!models.TryGetValue(tree, out var model)) models[tree] = model = comp.GetSemanticModel(tree);
        var node = tree.GetRoot().FindNode(d.Location.SourceSpan, getInnermostNodeForTie: true);

        SyntaxNode anchor = node.AncestorsAndSelf().FirstOrDefault(n => n is StatementSyntax
                               || n is ArrowExpressionClauseSyntax || n is EqualsValueClauseSyntax
                               || n is AttributeSyntax || n is ParameterSyntax || n is BaseTypeSyntax
                               || n is MemberDeclarationSyntax) ?? node;

        IEnumerable<SyntaxNode> scope = anchor.DescendantNodesAndSelf(n => n == anchor
            || !(n is StatementSyntax || n is BlockSyntax || n is ArrowExpressionClauseSyntax || n is MemberDeclarationSyntax || n is AccessorListSyntax));

        // 오류가 난 식 자신과 그 바깥 식은 당연히 오류 형식이므로 제외하고, 나머지(수신 식·인자·선언 형식 등)만 본다.
        var own = new HashSet<SyntaxNode>(node.AncestorsAndSelf().TakeWhile(n => n != anchor));
        foreach (var n in scope)
        {
            if (own.Contains(n)) continue;
            if (n is not (ExpressionSyntax or TypeSyntax or VariableDeclaratorSyntax or ParameterSyntax)) continue;
            if (n is ExpressionSyntax or TypeSyntax)
            {
                var ti = model.GetTypeInfo(n);
                if (ContainsError(ti.Type) || ContainsError(ti.ConvertedType)) return true;
                var si = model.GetSymbolInfo(n);
                if (SymbolContainsError(si.Symbol) || si.CandidateSymbols.Any(SymbolContainsError)) return true;
            }
            else if (SymbolContainsError(model.GetDeclaredSymbol(n))) return true;
        }
        // 멤버 머리(override/base 형식)에서 나는 오류: 바깥 형식의 상속 계층에 못 찾은 형식이 있으면 거른다.
        var containing = model.GetEnclosingSymbol(d.Location.SourceSpan.Start)?.ContainingType ?? model.GetDeclaredSymbol(anchor) as INamedTypeSymbol;
        for (var t = containing; t != null; t = t.ContainingType)
            for (var b = t.BaseType; b != null; b = b.BaseType)
                if (ContainsError(b)) return true;
        return false;
    }

    private static bool SymbolContainsError(ISymbol? s) => s switch
    {
        null => false,
        IMethodSymbol m => ContainsError(m.ReturnType) || ContainsError(m.ReceiverType) || m.Parameters.Any(p => ContainsError(p.Type)) || m.TypeArguments.Any(x => ContainsError(x)),
        IPropertySymbol p => ContainsError(p.Type) || ContainsError(p.ContainingType),
        IFieldSymbol f => ContainsError(f.Type) || ContainsError(f.ContainingType),
        ILocalSymbol l => ContainsError(l.Type),
        IParameterSymbol p => ContainsError(p.Type),
        IEventSymbol e => ContainsError(e.Type),
        ITypeSymbol t => ContainsError(t),
        _ => false,
    };

    private static bool ContainsError(ITypeSymbol? t, int depth = 0)
    {
        if (t == null || depth > 8) return false;
        if (t.TypeKind == TypeKind.Error || t is IErrorTypeSymbol) return true;
        return t switch
        {
            IArrayTypeSymbol a => ContainsError(a.ElementType, depth + 1),
            IPointerTypeSymbol p => ContainsError(p.PointedAtType, depth + 1),
            INamedTypeSymbol n => n.TypeArguments.Any(x => ContainsError(x, depth + 1)) || ContainsError(n.ContainingType, depth + 1),
            _ => false,
        };
    }

    private static string Key(Diagnostic d) => d.Id + "|" + Where(d);

    private static string Where(Diagnostic d)
    {
        var span = d.Location.GetMappedLineSpan();
        return $"{span.Path}:{span.StartLinePosition.Line + 1}:{span.StartLinePosition.Character + 1}";
    }

    private static string FirstQuoted(string message)
    {
        int a = message.IndexOf('\''), b = a >= 0 ? message.IndexOf('\'', a + 1) : -1;
        return a >= 0 && b > a ? message.Substring(a + 1, b - a - 1) : message;
    }

    private static string NuGetPackageRoot()
    {
        string? root = typeof(Program).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(a => a.Key == "NuGetPackageRoot")?.Value;
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            root = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
                   ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        return root;
    }

    private static IEnumerable<string> RefPack(string pkgRoot, string version)
    {
        string dir = Path.Combine(pkgRoot, "microsoft.netcore.app.ref", version, "ref", "net" + version.Substring(0, 3));
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException("참조 어셈블리 없음(복원 필요): " + dir);
        return Directory.GetFiles(dir, "*.dll");
    }

    private static string PackageDll(string pkgRoot, string id, string version, string dll)
    {
        string lib = Path.Combine(pkgRoot, id, version, "lib");
        if (!Directory.Exists(lib)) throw new DirectoryNotFoundException("패키지 없음(복원 필요): " + lib);
        // net6.0 > netstandard2.1 > netstandard2.0 순으로 고른다.
        foreach (string tfm in new[] { "net6.0", "netstandard2.1", "netstandard2.0" })
        {
            string p = Path.Combine(lib, tfm, dll);
            if (File.Exists(p)) return p;
        }
        var any = Directory.GetFiles(lib, dll, SearchOption.AllDirectories).FirstOrDefault();
        return any ?? throw new FileNotFoundException($"{id} {version}에 {dll} 없음");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "mod")) && Directory.Exists(Path.Combine(dir.FullName, "tools"))))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("저장소 루트를 찾지 못함. 경로를 인자로 주세요.");
    }

    private const string SelfTestNet7Path = "<self-test:net7-api>";
    private const string SelfTestTypoPath = "<self-test:typo>";
    private const string SelfTestGamePath = "<self-test:game-type>";

    private static readonly (string Path, string Text)[] SelfTestSources =
    {
        // .NET 7+ 전용 API (STARGAZER Dead End 17과 같은 실수)
        (SelfTestNet7Path, "namespace BodyCheckSelfTest { static class A { static double M(long t) => System.Diagnostics.Stopwatch.GetElapsedTime(t).TotalMilliseconds; } }"),
        // 모드 자체 타입의 없는 멤버 호출
        (SelfTestTypoPath, "namespace BodyCheckSelfTest { static class B { static int Real() => 1; static int M() => Reall(); } }"),
        // 게임 타입 사용은 걸러져야 한다
        (SelfTestGamePath, "using Il2CppSixtarGate.Play; namespace BodyCheckSelfTest { static class C { static object M(JudgeProcessor p) => p.Something(); } }"),
    };
}
