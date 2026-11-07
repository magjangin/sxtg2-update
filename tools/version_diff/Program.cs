// Cpp2IL dummydll 폴더를 읽어 "형식::메서드(매개변수) => RVA" 목록을 출력한다.
// 사용: dumpdiff <cpp2il_out 폴더> <출력 txt>
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

var dir = args[0];
var outPath = args[1];
var lines = new List<string>();
foreach (var file in Directory.GetFiles(dir, "*.dll"))
{
    using var fs = File.OpenRead(file);
    using var pe = new PEReader(fs);
    if (!pe.HasMetadata) continue;
    var md = pe.GetMetadataReader();
    var asm = Path.GetFileNameWithoutExtension(file);
    var prov = new SigProvider(md);
    foreach (var th in md.TypeDefinitions)
    {
        var td = md.GetTypeDefinition(th);
        var tname = FullName(md, th);
        foreach (var fh in td.GetFields())
        {
            var f = md.GetFieldDefinition(fh);
            string ft;
            try { ft = f.DecodeSignature(prov, null); } catch { ft = "?"; }
            string off = "";
            foreach (var ch in f.GetCustomAttributes())
            {
                var (n, a) = AttrArgs(md, ch);
                if (n == "FieldOffsetAttribute" && a.TryGetValue("Offset", out var o)) off = o;
            }
            lines.Add($"F {asm}|{tname}::{md.GetString(f.Name)} : {ft} @{off}");
        }
        foreach (var mh in td.GetMethods())
        {
            var m = md.GetMethodDefinition(mh);
            string sig;
            try
            {
                var s = m.DecodeSignature(prov, null);
                sig = $"{s.ReturnType} ({string.Join(", ", s.ParameterTypes)})";
            }
            catch { sig = "?"; }
            string rva = "";
            foreach (var ch in m.GetCustomAttributes())
            {
                var (n, a) = AttrArgs(md, ch);
                if (n == "AddressAttribute" && a.TryGetValue("RVA", out var r)) rva = r;
            }
            lines.Add($"M {asm}|{tname}::{md.GetString(m.Name)} {sig} => {rva}");
        }
    }
}
lines.Sort(StringComparer.Ordinal);
File.WriteAllLines(outPath, lines, new UTF8Encoding(false));
Console.WriteLine($"{lines.Count} lines -> {outPath}");

static string FullName(MetadataReader md, TypeDefinitionHandle h)
{
    var td = md.GetTypeDefinition(h);
    var name = md.GetString(td.Name);
    var decl = td.GetDeclaringType();
    if (!decl.IsNil) return FullName(md, decl) + "/" + name;
    var ns = md.GetString(td.Namespace);
    return ns.Length > 0 ? ns + "." + name : name;
}

static (string, Dictionary<string, string>) AttrArgs(MetadataReader md, CustomAttributeHandle ch)
{
    var ca = md.GetCustomAttribute(ch);
    string name = "";
    if (ca.Constructor.Kind == HandleKind.MemberReference)
    {
        var mr = md.GetMemberReference((MemberReferenceHandle)ca.Constructor);
        if (mr.Parent.Kind == HandleKind.TypeReference)
            name = md.GetString(md.GetTypeReference((TypeReferenceHandle)mr.Parent).Name);
        else if (mr.Parent.Kind == HandleKind.TypeDefinition)
            name = md.GetString(md.GetTypeDefinition((TypeDefinitionHandle)mr.Parent).Name);
    }
    else if (ca.Constructor.Kind == HandleKind.MethodDefinition)
    {
        var mdef = md.GetMethodDefinition((MethodDefinitionHandle)ca.Constructor);
        name = md.GetString(md.GetTypeDefinition(mdef.GetDeclaringType()).Name);
    }
    var d = new Dictionary<string, string>();
    if (name != "AddressAttribute" && name != "FieldOffsetAttribute") return (name, d);
    try
    {
        var v = ca.DecodeValue(new AttrProvider());
        foreach (var na in v.NamedArguments) d[na.Name ?? ""] = na.Value?.ToString() ?? "";
    }
    catch { }
    return (name, d);
}

sealed class SigProvider : ISignatureTypeProvider<string, object?>
{
    readonly MetadataReader md;
    public SigProvider(MetadataReader md) { this.md = md; }
    public string GetArrayType(string e, ArrayShape s) => e + "[" + new string(',', s.Rank - 1) + "]";
    public string GetByReferenceType(string e) => e + "&";
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
    public string GetGenericInstantiation(string g, System.Collections.Immutable.ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object? c, int i) => "!!" + i;
    public string GetGenericTypeParameter(object? c, int i) => "!" + i;
    public string GetModifiedType(string m, string u, bool r) => u;
    public string GetPinnedType(string e) => e;
    public string GetPointerType(string e) => e + "*";
    public string GetPrimitiveType(PrimitiveTypeCode t) => t.ToString();
    public string GetSZArrayType(string e) => e + "[]";
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k)
    {
        var td = r.GetTypeDefinition(h);
        var ns = r.GetString(td.Namespace);
        return (ns.Length > 0 ? ns + "." : "") + r.GetString(td.Name);
    }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k)
    {
        var tr = r.GetTypeReference(h);
        var ns = r.GetString(tr.Namespace);
        return (ns.Length > 0 ? ns + "." : "") + r.GetString(tr.Name);
    }
    public string GetTypeFromSpecification(MetadataReader r, object? c, TypeSpecificationHandle h, byte k) => "spec";
}

sealed class AttrProvider : ICustomAttributeTypeProvider<string>
{
    public string GetPrimitiveType(PrimitiveTypeCode t) => t.ToString();
    public string GetSystemType() => "Type";
    public string GetSZArrayType(string e) => e + "[]";
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => "def";
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) => "ref";
    public string GetTypeFromSerializedName(string n) => n;
    public PrimitiveTypeCode GetUnderlyingEnumType(string t) => PrimitiveTypeCode.Int32;
    public bool IsSystemType(string t) => t == "Type";
}
