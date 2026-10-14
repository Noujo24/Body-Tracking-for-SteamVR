// Read-only metadata/IL inspector for game assemblies (never writes to the input).
// Usage:
//   info  <dll>
//   types <dll> <regex>
//   dump  <dll> <typeFullName>          (fields, properties, methods, nested types)
//   il    <dll> <typeFullName> <methodRegex>
//   refs  <dll> <memberRegex>           (methods whose IL references a member matching regex)
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

var mode = args[0];
var resolver = new DefaultAssemblyResolver();
resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1])));
if (mode == "resolve")
{
    // resolve <dll> <dir1;dir2;...>: resolve every type/member reference ONLY against the given folders
    // (e.g. the game's Managed folder + BepInEx/core) to prove they exist in the real runtime libraries.
    resolver = new DefaultAssemblyResolver();
    foreach (var d in resolver.GetSearchDirectories()) resolver.RemoveSearchDirectory(d);
    foreach (var d in args[2].Split(';')) resolver.AddSearchDirectory(d);
}
var asm = AssemblyDefinition.ReadAssembly(args[1], new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Deferred });
var mod = asm.MainModule;

IEnumerable<TypeDefinition> All(IEnumerable<TypeDefinition> ts) { foreach (var t in ts) { yield return t; foreach (var n in All(t.NestedTypes)) yield return n; } }

switch (mode)
{
    case "info":
        Console.WriteLine($"{asm.FullName}  runtime={mod.RuntimeVersion}");
        foreach (var a in asm.CustomAttributes) Console.WriteLine($"  attr {a.AttributeType.Name}({string.Join(", ", a.ConstructorArguments.Select(x => x.Value))})");
        foreach (var r in mod.AssemblyReferences) Console.WriteLine($"  ref {r.FullName}");
        break;
    case "types":
    {
        var rx = new Regex(args[2]);
        foreach (var t in All(mod.Types).Where(t => rx.IsMatch(t.FullName)))
            Console.WriteLine($"{(t.IsEnum ? "enum " : t.IsValueType ? "struct " : t.IsInterface ? "interface " : "class ")}{t.FullName} : {t.BaseType?.FullName}");
        break;
    }
    case "dump":
    {
        var t = All(mod.Types).First(x => x.FullName == args[2]);
        Console.WriteLine($"{t.FullName} : {t.BaseType?.FullName}");
        if (t.IsEnum)
        {
            foreach (var f in t.Fields.Where(f => f.IsStatic)) Console.WriteLine($"  {f.Name} = {f.Constant}");
            break;
        }
        foreach (var f in t.Fields) Console.WriteLine($"  field {(f.IsPublic ? "public " : f.IsPrivate ? "private " : "internal ")}{(f.IsStatic ? "static " : "")}{f.FieldType.FullName} {f.Name}");
        foreach (var p in t.Properties) Console.WriteLine($"  prop {p.PropertyType.FullName} {p.Name} {{{(p.GetMethod != null ? (p.GetMethod.IsPublic ? "get;" : "priv get;") : "")}{(p.SetMethod != null ? (p.SetMethod.IsPublic ? "set;" : "priv set;") : "")}}}");
        foreach (var m in t.Methods) Console.WriteLine($"  method {(m.IsPublic ? "public " : m.IsPrivate ? "private " : "internal ")}{(m.IsStatic ? "static " : "")}{(m.IsVirtual ? "virtual " : "")}{m.ReturnType.FullName} {m.Name}({string.Join(", ", m.Parameters.Select(p => p.ParameterType.FullName + " " + p.Name))}){(m.HasBody ? $" [{m.Body.CodeSize}b]" : "")}");
        foreach (var n in t.NestedTypes) Console.WriteLine($"  nested {n.FullName}");
        break;
    }
    case "il":
    {
        var t = All(mod.Types).First(x => x.FullName == args[2]);
        var rx = new Regex(args[3]);
        foreach (var m in t.Methods.Where(m => rx.IsMatch(m.Name) && m.HasBody))
        {
            Console.WriteLine($"== {m.FullName}");
            foreach (var i in m.Body.Instructions)
            {
                var op = i.Operand switch
                {
                    MemberReference mr => mr.FullName,
                    Instruction ins => $"IL_{ins.Offset:x4}",
                    Instruction[] arr => string.Join(",", arr.Select(a => $"IL_{a.Offset:x4}")),
                    string s => "\"" + s + "\"",
                    null => "",
                    var o => o.ToString()
                };
                Console.WriteLine($"  IL_{i.Offset:x4}: {i.OpCode} {op}");
            }
        }
        break;
    }
    case "resolve":
    {
        int ok = 0, failed = 0;
        var seen = new HashSet<string>();
        void Try(MemberReference r)
        {
            if (r == null || !seen.Add(r.FullName)) return;
            if (r.Module == mod && r is IMemberDefinition) return;
            try
            {
                object res = r switch
                {
                    TypeReference t => t.IsGenericParameter ? (object)t : t.Resolve(),
                    MethodReference m => m.Resolve(),
                    FieldReference f => f.Resolve(),
                    _ => r
                };
                if (res == null) { failed++; Console.WriteLine("UNRESOLVED " + r.FullName); }
                else ok++;
            }
            catch (Exception e) { failed++; Console.WriteLine($"ERROR {r.FullName}: {e.GetType().Name} {e.Message}"); }
        }
        foreach (var tr in mod.GetTypeReferences()) Try(tr);
        foreach (var mr in mod.GetMemberReferences()) Try(mr);
        Console.WriteLine($"resolved {ok}, failed {failed}");
        Environment.ExitCode = failed == 0 ? 0 : 1;
        break;
    }
    case "refs":
    {
        var rx = new Regex(args[2]);
        foreach (var t in All(mod.Types))
            foreach (var m in t.Methods.Where(m => m.HasBody))
            {
                var hits = m.Body.Instructions.Select(i => i.Operand as MemberReference).Where(r => r != null && rx.IsMatch(r.FullName)).Select(r => r.FullName).Distinct().ToList();
                if (hits.Count > 0) Console.WriteLine($"{t.FullName}::{m.Name}\n    -> {string.Join("\n    -> ", hits)}");
            }
        break;
    }
}
