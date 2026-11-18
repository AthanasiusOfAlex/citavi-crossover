// citavi-patcher — applies declarative IL patches to Citavi's .NET assemblies so that
// Citavi runs under Wine/CrossOver. Rules live in a JSON file (see ../patches/rules.json),
// so adapting to a new Citavi version normally means editing that file, not this code.
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;

namespace CitaviPatcher;

public sealed class RuleSet
{
    public List<Rule> Rules { get; set; } = new();
}

public sealed class Rule
{
    public string Id { get; set; } = "";
    /// <summary>redirect-call | return-constant | set-field-constant | fill-region-as-rectangles | set-field-to-window-width | complete-character-placement | draw-trackbar-thumb | wide-character-call | return-constant-for-argument</summary>
    public string Kind { get; set; } = "";
    public string Why { get; set; } = "";
    public bool Enabled { get; set; } = true;

    // redirect-call: every call to From becomes a call to To (same declaring type + signature).
    // Format: "Namespace.Type::Method".
    public string? From { get; set; }
    public string? To { get; set; }

    // fill-region-as-rectangles: in Assembly!Type::Method, Graphics.FillRegion(brush, region) becomes
    // Graphics.FillRectangles(brush, region.GetRegionScans(identity)). Uses Assembly, Type, Method.
    // return-constant: the body of Assembly!Type::Method is replaced by "return Value".
    // Value is true/false, a string, null, or a number (for a method returning an integer or an enum).
    public string? Assembly { get; set; }
    public string? Type { get; set; }
    public string? Method { get; set; }
    public JsonElement? Value { get; set; }

    // set-field-constant: wherever a method of Assembly!Type stores the integer constant Was into Field,
    // it stores Value instead (covers field initialisers, which the compiler copies into constructors).
    // Field is matched by name whatever type declares it, so it can also be a field of a struct the
    // method fills in. Method, if given, limits the rule to methods of that name (a property getter is get_Name).
    public string? Field { get; set; }
    public int? Was { get; set; }

    // set-field-to-window-width: Assembly!Type::Method, a method of a control, begins by storing the
    // control's real width, asked from Windows (GetWindowRect), into the integer field Field. For code
    // that records its width in one window message and uses it in another, when Wine sends the two
    // in the opposite order. Uses Assembly, Type, Method, Field.

    // complete-character-placement: Assembly!Type::Method is the program's own declaration of gdi32's
    // GetCharacterPlacement. Every call of it goes through an added method that makes the call and then
    // fills in the two results Wine leaves untouched when GCP_REORDER is asked for: the caret positions
    // (running sum of the character widths) and the character classes. Uses Assembly, Type, Method.

    // draw-trackbar-thumb: Assembly!Type is a control that holds a slider (a WinForms TrackBar) in its
    // field Field. The control gets a WndProc that answers the slider's custom-draw requests and draws
    // the thumb itself: a filled pointer the size of the thumb's rectangle, blue, or grey while the
    // slider is disabled or being dragged. For a horizontal slider with its ticks below. Uses Assembly, Type, Field.

    // wide-character-call: Assembly!Type::Method is the program's own declaration of a Windows function
    // that exists in two forms, one for single-byte text (…A) and one for 16-bit text (…W), declared
    // without saying which, so that .NET calls the single-byte form. The rule makes the declaration
    // ask for the 16-bit form (CharSet.Unicode). For functions whose single-byte form misbehaves under
    // Wine. Uses Assembly, Type, Method.

    // return-constant-for-argument: Assembly!Type::Method takes a string first and returns a string.
    // It gets a new first statement: if the first argument equals When, return Value. For a method
    // that asks Windows something whose answer under Wine is known and unhelpful for one input.
    // Uses Assembly, Type, Method, When, Value (a string).
    public string? When { get; set; }
}

public sealed class Manifest
{
    public string Tool { get; set; } = "citavi-patcher";
    public string AppliedAt { get; set; } = "";
    public Dictionary<string, ManifestEntry> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ManifestEntry
{
    public string OriginalSha256 { get; set; } = "";
    public string PatchedSha256 { get; set; } = "";
    public List<string> Rules { get; set; } = new();
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(RuleSet))]
[JsonSerializable(typeof(Manifest))]
internal partial class Json : JsonSerializerContext { }

/// <summary>One place in one assembly where a rule wants to change something.</summary>
internal sealed record Hit(string File, string RuleId, string Where);

internal static class Program
{
    /// <summary>Directories holding the .NET Framework assemblies (needed by Cecil when it rewrites a module).</summary>
    static readonly List<string> FrameworkDirs = new();

    static int Main(string[] args)
    {
        try { return Run(args); }
        catch (UserError e) { Console.Error.WriteLine("error: " + e.Message); return 2; }
    }

    sealed class UserError(string m) : Exception(m);

    static int Run(string[] args)
    {
        if (args.Length < 2) return Usage();
        string cmd = args[0], bin = Path.GetFullPath(args[1]);
        string? rulesPath = null, backup = null;
        bool dryRun = false;
        string? findPattern = null;
        FrameworkDirs.Clear();
        for (int i = 2; i < args.Length; i++)
        {
            if (cmd == "find" && findPattern == null && !args[i].StartsWith("--")) { findPattern = args[i]; continue; }
            switch (args[i])
            {
                case "--rules": rulesPath = args[++i]; break;
                case "--backup": backup = Path.GetFullPath(args[++i]); break;
                case "--dry-run": dryRun = true; break;
                case "--framework": FrameworkDirs.Add(Path.GetFullPath(args[++i])); break;
                default: throw new UserError($"unknown option {args[i]}");
            }
        }
        if (!Directory.Exists(bin)) throw new UserError($"not a directory: {bin}");

        switch (cmd)
        {
            case "scan": return Scan(bin, LoadRules(rulesPath));
            case "apply":
                if (backup == null) throw new UserError("apply needs --backup <dir>");
                return Apply(bin, LoadRules(rulesPath), backup, dryRun);
            case "verify": return Verify(bin, LoadRules(rulesPath));
            case "restore":
                if (backup == null) throw new UserError("restore needs --backup <dir>");
                return Restore(bin, backup);
            case "find": return Find(bin, findPattern ?? throw new UserError("find needs a pattern"));
            case "info": return Info(bin);
            default: return Usage();
        }
    }

    static int Usage()
    {
        Console.Error.WriteLine("""
            usage:
              citavi-patcher scan    <bin-dir> --rules <rules.json>                  list what the rules would change
              citavi-patcher apply   <bin-dir> --rules <rules.json> --backup <dir>   patch in place (originals copied to <dir>)
              citavi-patcher verify  <bin-dir> --rules <rules.json>                  exit 0 when nothing is left to patch
              citavi-patcher restore <bin-dir> --backup <dir>                        put the original files back
              citavi-patcher find    <bin-dir> <Type::Method substring>              list call sites of a framework/API method
              citavi-patcher info    <bin-dir>                                       print the installed Citavi version
            options:
              --framework <dir>   folder with the .NET Framework assemblies (repeatable); required by apply
              --dry-run           with apply: report only
            """);
        return 2;
    }

    static RuleSet LoadRules(string? path)
    {
        if (path == null) throw new UserError("--rules <rules.json> is required");
        if (!File.Exists(path)) throw new UserError($"rules file not found: {path}");
        var set = JsonSerializer.Deserialize(File.ReadAllText(path), Json.Default.RuleSet) ?? new RuleSet();
        foreach (var r in set.Rules)
        {
            if (r.Id == "") throw new UserError("every rule needs an id");
            switch (r.Kind)
            {
                case "redirect-call":
                    if (Split(r.From) == null || Split(r.To) == null)
                        throw new UserError($"rule {r.Id}: from/to must look like Namespace.Type::Method");
                    break;
                case "return-constant":
                    if (r.Assembly == null || r.Type == null || r.Method == null)
                        throw new UserError($"rule {r.Id}: return-constant needs assembly, type, method (and value, unless the method is void or should return null)");
                    break;
                case "set-field-constant":
                    if (r.Assembly == null || r.Type == null || r.Field == null || r.Was == null || r.Value?.ValueKind != JsonValueKind.Number)
                        throw new UserError($"rule {r.Id}: set-field-constant needs assembly, type, field, was (old integer) and value (new integer)");
                    break;
                case "fill-region-as-rectangles":
                    if (r.Assembly == null || r.Type == null || r.Method == null)
                        throw new UserError($"rule {r.Id}: fill-region-as-rectangles needs assembly, type and method");
                    break;
                case "set-field-to-window-width":
                    if (r.Assembly == null || r.Type == null || r.Method == null || r.Field == null)
                        throw new UserError($"rule {r.Id}: set-field-to-window-width needs assembly, type, method and field");
                    break;
                case "complete-character-placement":
                    if (r.Assembly == null || r.Type == null || r.Method == null)
                        throw new UserError($"rule {r.Id}: complete-character-placement needs assembly, type and method");
                    break;
                case "draw-trackbar-thumb":
                    if (r.Assembly == null || r.Type == null || r.Field == null)
                        throw new UserError($"rule {r.Id}: draw-trackbar-thumb needs assembly, type and field");
                    break;
                case "wide-character-call":
                    if (r.Assembly == null || r.Type == null || r.Method == null)
                        throw new UserError($"rule {r.Id}: wide-character-call needs assembly, type and method");
                    break;
                case "return-constant-for-argument":
                    if (r.Assembly == null || r.Type == null || r.Method == null || r.When == null || r.Value?.ValueKind != JsonValueKind.String)
                        throw new UserError($"rule {r.Id}: return-constant-for-argument needs assembly, type, method, when (text) and value (text)");
                    break;
                default: throw new UserError($"rule {r.Id}: unknown kind '{r.Kind}'");
            }
        }
        set.Rules = set.Rules.Where(r => r.Enabled).ToList();
        return set;
    }

    static readonly JsonElement JsonNull = JsonDocument.Parse("null").RootElement;
    /// <summary>A rule's value; an omitted or null value means "return null" (or nothing, for void).</summary>
    static JsonElement ValueOf(Rule r) => r.Value ?? JsonNull;

    static (string Type, string Method)? Split(string? s)
    {
        if (s == null) return null;
        int i = s.IndexOf("::", StringComparison.Ordinal);
        return i <= 0 || i + 2 >= s.Length ? null : (s[..i], s[(i + 2)..]);
    }

    // ---------------------------------------------------------------- discovery

    static IEnumerable<string> ManagedCandidates(string bin) =>
        Directory.EnumerateFiles(bin, "*", SearchOption.TopDirectoryOnly)
            .Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

    static bool ContainsAscii(byte[] haystack, string needle) =>
        haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(needle)) >= 0;

    /// <summary>
    /// Whether a file is the one a rule's "assembly" names. A * in the name stands for any text, for
    /// libraries that carry their version in the file name (Infragistics4.Win.v*.dll).
    /// </summary>
    static bool IsAssembly(string fileName, string? pattern)
    {
        if (pattern == null) return false;
        var parts = pattern.Split('*');
        if (parts.Length == 1) return string.Equals(fileName, pattern, StringComparison.OrdinalIgnoreCase);
        if (!fileName.StartsWith(parts[0], StringComparison.OrdinalIgnoreCase)) return false;
        int at = parts[0].Length;
        for (int i = 1; i < parts.Length - 1; i++)
        {
            int found = fileName.IndexOf(parts[i], at, StringComparison.OrdinalIgnoreCase);
            if (found < 0) return false;
            at = found + parts[i].Length;
        }
        string last = parts[parts.Length - 1];
        return fileName.Length - at >= last.Length && fileName.EndsWith(last, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A MISSING hit for every rule whose assembly is not in Citavi's bin folder at all.</summary>
    static List<Hit> RulesWithoutFile(string bin, RuleSet rules)
    {
        var names = ManagedCandidates(bin).Select(f => Path.GetFileName(f)).ToList();
        return rules.Rules
            .Where(r => r.Assembly != null && !names.Any(n => IsAssembly(n, r.Assembly)))
            .Select(r => new Hit(r.Assembly!, r.Id, $"MISSING no file named {r.Assembly} in Citavi's bin folder"))
            .ToList();
    }

    /// <summary>Cheap pre-filter so we only parse assemblies that can possibly match a rule.</summary>
    static bool MightMatch(string file, byte[] bytes, RuleSet rules)
    {
        foreach (var r in rules.Rules)
        {
            if (r.Kind == "redirect-call" && ContainsAscii(bytes, Split(r.From)!.Value.Method)) return true;
            if (r.Kind != "redirect-call" && IsAssembly(Path.GetFileName(file), r.Assembly)) return true;
        }
        return false;
    }

    static ModuleDefinition? TryRead(string file, byte[] bytes, string bin)
    {
        try
        {
            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(bin);
            foreach (var d in FrameworkDirs) if (Directory.Exists(d)) resolver.AddSearchDirectory(d);
            return ModuleDefinition.ReadModule(new MemoryStream(bytes),
                new ReaderParameters { AssemblyResolver = resolver, InMemory = true, ReadingMode = ReadingMode.Deferred });
        }
        catch (BadImageFormatException) { return null; } // native DLL
    }

    static IEnumerable<MethodDefinition> AllMethods(ModuleDefinition m)
    {
        foreach (var t in m.GetTypes())
            foreach (var md in t.Methods)
                yield return md;
    }

    static bool IsRef(MethodReference mr, (string Type, string Method) target) =>
        mr.Name == target.Method && mr.DeclaringType.FullName.Replace('/', '+') == target.Type;

    /// <summary>
    /// Runs every rule against one module. When <paramref name="mutate"/> is false nothing is changed
    /// and the returned hits describe what would be done.
    /// </summary>
    static List<Hit> Process(string file, ModuleDefinition module, RuleSet rules, bool mutate)
    {
        var hits = new List<Hit>();
        string name = Path.GetFileName(file);
        foreach (var rule in rules.Rules)
        {
            if (rule.Kind == "redirect-call")
            {
                var from = Split(rule.From)!.Value; var to = Split(rule.To)!.Value;
                if (!module.GetMemberReferences().OfType<MethodReference>().Any(mr => IsRef(mr, from))) continue;
                foreach (var md in AllMethods(module))
                {
                    if (!md.HasBody) continue;
                    foreach (var ins in md.Body.Instructions)
                    {
                        if (ins.Operand is not MethodReference mr || !IsRef(mr, from)) continue;
                        hits.Add(new Hit(name, rule.Id, $"{md.DeclaringType.FullName}::{md.Name}"));
                        if (!mutate) continue;
                        if (to.Type != from.Type)
                            throw new UserError($"rule {rule.Id}: redirect-call currently requires the same declaring type");
                        var repl = new MethodReference(to.Method, mr.ReturnType, mr.DeclaringType)
                        { HasThis = mr.HasThis, ExplicitThis = mr.ExplicitThis, CallingConvention = mr.CallingConvention };
                        foreach (var p in mr.Parameters) repl.Parameters.Add(new ParameterDefinition(p.ParameterType));
                        ins.Operand = module.ImportReference(repl);
                    }
                }
            }
            else if (rule.Kind == "return-constant")
            {
                if (!IsAssembly(name, rule.Assembly)) continue;
                var type = module.GetTypes().FirstOrDefault(t => t.FullName.Replace('/', '+') == rule.Type);
                var methods = type?.Methods.Where(m => m.Name == rule.Method && m.HasBody).ToList() ?? new();
                if (methods.Count == 0)
                {
                    hits.Add(new Hit(name, rule.Id, $"MISSING {rule.Type}::{rule.Method}"));
                    continue;
                }
                foreach (var md in methods)
                {
                    if (IsAlreadyConstant(md, ValueOf(rule))) continue;
                    hits.Add(new Hit(name, rule.Id, $"{md.DeclaringType.FullName}::{md.Name}"));
                    if (mutate) ReplaceWithConstant(md, rule);
                }
            }
            else if (rule.Kind == "set-field-constant")
            {
                if (!IsAssembly(name, rule.Assembly)) continue;
                var type = module.GetTypes().FirstOrDefault(t => t.FullName.Replace('/', '+') == rule.Type);
                int was = rule.Was!.Value, now = rule.Value!.Value.GetInt32();
                bool found = false;
                foreach (var md in type?.Methods.Where(m => m.HasBody) ?? Enumerable.Empty<MethodDefinition>())
                {
                    if (rule.Method != null && md.Name != rule.Method) continue;
                    var il = md.Body.Instructions;
                    for (int i = 0; i + 1 < il.Count; i++)
                    {
                        if (il[i + 1].OpCode != OpCodes.Stfld && il[i + 1].OpCode != OpCodes.Stsfld) continue;
                        if (il[i + 1].Operand is not FieldReference fr || fr.Name != rule.Field) continue;
                        int? c = IntConstant(il[i]);
                        if (c == now) { found = true; continue; }
                        if (c != was) continue;
                        found = true;
                        hits.Add(new Hit(name, rule.Id, $"{md.DeclaringType.FullName}::{md.Name}  ({rule.Field} = {was} -> {now})"));
                        if (mutate) { il[i].OpCode = OpCodes.Ldc_I4; il[i].Operand = now; }
                    }
                }
                if (!found) hits.Add(new Hit(name, rule.Id, $"MISSING {rule.Type}: {(rule.Method == null ? "no method" : rule.Method + " no longer")} stores {was} into {rule.Field}"));
            }
            else if (rule.Kind == "fill-region-as-rectangles")
            {
                // g.FillRegion(brush, region)  becomes  g.FillRectangles(brush, region.GetRegionScans(new Matrix()))
                if (!IsAssembly(name, rule.Assembly)) continue;
                var type = module.GetTypes().FirstOrDefault(t => t.FullName.Replace('/', '+') == rule.Type);
                bool found = false;
                foreach (var md in type?.Methods.Where(m => m.Name == rule.Method && m.HasBody).ToList() ?? new())
                {
                    var calls = md.Body.Instructions
                        .Where(i => i.Operand is MethodReference mr && mr.DeclaringType.FullName == "System.Drawing.Graphics").ToList();
                    if (calls.Any(i => ((MethodReference)i.Operand).Name == "FillRectangles"
                                       && i.Previous?.Operand is MethodReference prev && prev.Name == "GetRegionScans")) found = true;
                    foreach (var ins in calls)
                    {
                        var fill = (MethodReference)ins.Operand;
                        if (fill.Name != "FillRegion" || fill.Parameters.Count != 2) continue;
                        found = true;
                        hits.Add(new Hit(name, rule.Id, $"{md.DeclaringType.FullName}::{md.Name}"));
                        if (mutate) FillRegionAsRectangles(module, md, ins, fill);
                    }
                }
                if (!found) hits.Add(new Hit(name, rule.Id, $"MISSING {rule.Type}::{rule.Method} no longer calls Graphics.FillRegion"));
            }
            else if (rule.Kind == "set-field-to-window-width")
            {
                // the method gets a new first statement:  this._field = this.CitaviCrossover_WindowWidth();
                if (!IsAssembly(name, rule.Assembly)) continue;
                var type = module.GetTypes().FirstOrDefault(t => t.FullName.Replace('/', '+') == rule.Type);
                var field = type?.Fields.FirstOrDefault(f => f.Name == rule.Field && !f.IsStatic && f.FieldType.FullName == "System.Int32");
                var methods = type?.Methods.Where(m => m.Name == rule.Method && m.HasBody && !m.IsStatic).ToList() ?? new();
                if (field == null || methods.Count == 0)
                {
                    hits.Add(new Hit(name, rule.Id, $"MISSING {rule.Type}::{(field == null ? rule.Field + " (an integer field)" : rule.Method)}"));
                    continue;
                }
                foreach (var md in methods)
                {
                    if (md.Body.Instructions.Any(i => i.Operand is MethodReference mr && mr.Name == WindowWidthName)) continue;
                    hits.Add(new Hit(name, rule.Id, $"{md.DeclaringType.FullName}::{md.Name}  ({rule.Field} = real window width)"));
                    if (!mutate) continue;
                    var helper = WindowWidthHelper(module, type!, field, rule.Id);
                    md.Body.SimplifyMacros(); // short branches become long ones, so the added instructions cannot push a target out of reach
                    var il = md.Body.GetILProcessor();
                    var first = md.Body.Instructions[0];
                    il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                    il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg_0));
                    il.InsertBefore(first, Instruction.Create(OpCodes.Call, helper));
                    il.InsertBefore(first, Instruction.Create(OpCodes.Stfld, field));
                    md.Body.OptimizeMacros();
                }
            }
            else if (rule.Kind == "complete-character-placement")
            {
                // every call of the program's GetCharacterPlacement goes to CitaviCrossover_GetCharacterPlacement
                if (!IsAssembly(name, rule.Assembly)) continue;
                var type = module.GetTypes().FirstOrDefault(t => t.FullName.Replace('/', '+') == rule.Type);
                var native = type?.Methods.FirstOrDefault(m => m.Name == rule.Method && m.IsPInvokeImpl);
                string? shape = native == null ? "is not there" : PlacementShapeProblem(native);
                if (shape != null)
                {
                    hits.Add(new Hit(name, rule.Id, $"MISSING {rule.Type}::{rule.Method} {shape}"));
                    continue;
                }
                MethodDefinition? wrapper = type!.Methods.FirstOrDefault(m => m.Name == PlacementName);
                foreach (var md in AllMethods(module).Where(m => m.HasBody && m.Name != PlacementName).ToList())
                {
                    foreach (var ins in md.Body.Instructions)
                    {
                        if (ins.OpCode != OpCodes.Call || !ReferenceEquals(ins.Operand, native)) continue; // a call within the module names the definition itself
                        hits.Add(new Hit(name, rule.Id, $"{md.DeclaringType.FullName}::{md.Name}"));
                        if (!mutate) continue;
                        wrapper ??= PlacementHelper(module, type, native!);
                        ins.Operand = wrapper;
                    }
                }
            }
            else if (rule.Kind == "draw-trackbar-thumb")
            {
                // the control gets a WndProc that draws the slider's thumb when the slider asks (NM_CUSTOMDRAW)
                if (!IsAssembly(name, rule.Assembly)) continue;
                var type = module.GetTypes().FirstOrDefault(t => t.FullName.Replace('/', '+') == rule.Type);
                var field = type?.Fields.FirstOrDefault(f => f.Name == rule.Field && !f.IsStatic && f.FieldType.FullName == "System.Windows.Forms.TrackBar");
                if (field == null)
                {
                    hits.Add(new Hit(name, rule.Id, $"MISSING {rule.Type}::{rule.Field} (a TrackBar field)"));
                    continue;
                }
                if (type!.Methods.Any(m => m.Name == ThumbName)) continue; // already there
                if (type.Methods.Any(m => m.Name == "WndProc"))
                {
                    hits.Add(new Hit(name, rule.Id, $"MISSING {rule.Type} now has a WndProc of its own; the rule can only add one"));
                    continue;
                }
                hits.Add(new Hit(name, rule.Id, $"{type.FullName}  (draws the thumb of {rule.Field})"));
                if (mutate) AddThumbDrawing(module, type, field, rule.Id);
            }
            else if (rule.Kind == "wide-character-call")
            {
                // the declaration of a Windows function asks for its 16-bit-text form
                if (!IsAssembly(name, rule.Assembly)) continue;
                var type = module.GetTypes().FirstOrDefault(t => t.FullName.Replace('/', '+') == rule.Type);
                var natives = type?.Methods.Where(m => m.Name == rule.Method && m.IsPInvokeImpl && m.PInvokeInfo != null).ToList() ?? new();
                if (natives.Count == 0)
                {
                    hits.Add(new Hit(name, rule.Id, $"MISSING {rule.Type}::{rule.Method} (a declaration of a Windows function)"));
                    continue;
                }
                foreach (var native in natives)
                {
                    if (native.PInvokeInfo.IsCharSetUnicode) continue; // already so
                    hits.Add(new Hit(name, rule.Id, $"{type!.FullName}::{native.Name}  (calls {native.PInvokeInfo.EntryPoint}W)"));
                    if (!mutate) continue;
                    native.PInvokeInfo.IsCharSetUnicode = true; // replaces "ansi" or "not specified"; the runtime then looks for <name>W
                }
            }
            else if (rule.Kind == "return-constant-for-argument")
            {
                // the method gets a new first statement:  if (firstArgument == when) return value;
                if (!IsAssembly(name, rule.Assembly)) continue;
                var type = module.GetTypes().FirstOrDefault(t => t.FullName.Replace('/', '+') == rule.Type);
                var methods = type?.Methods.Where(m => m.Name == rule.Method && m.HasBody && m.ReturnType.FullName == "System.String"
                    && m.Parameters.Count > 0 && m.Parameters[0].ParameterType.FullName == "System.String").ToList() ?? new();
                if (methods.Count == 0)
                {
                    hits.Add(new Hit(name, rule.Id, $"MISSING {rule.Type}::{rule.Method} (taking a string first and returning a string)"));
                    continue;
                }
                string value = rule.Value!.Value.GetString()!;
                foreach (var md in methods)
                {
                    var start = md.Body.Instructions;
                    bool there = false; // the same test may follow an earlier rule's, so look at every ldarg, ldstr, call, brfalse, ldstr, ret at the start
                    for (int i = 0; i + 5 < start.Count && start[i + 1].OpCode == OpCodes.Ldstr && start[i + 5].OpCode == OpCodes.Ret; i += 6)
                        if ((string)start[i + 1].Operand == rule.When && start[i + 4].OpCode == OpCodes.Ldstr && (string)start[i + 4].Operand == value) there = true;
                    if (there) continue;
                    hits.Add(new Hit(name, rule.Id, $"{md.DeclaringType.FullName}::{md.Name}  (\"{rule.When}\" gives \"{value}\")"));
                    if (!mutate) continue;
                    var equality = new MethodReference("op_Equality", module.TypeSystem.Boolean, module.TypeSystem.String);
                    equality.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
                    equality.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));
                    md.Body.SimplifyMacros(); // short branches become long ones, so the added instructions cannot push a target out of reach
                    var il = md.Body.GetILProcessor();
                    var first = md.Body.Instructions[0];
                    il.InsertBefore(first, Instruction.Create(OpCodes.Ldarg, md.Parameters[0]));
                    il.InsertBefore(first, Instruction.Create(OpCodes.Ldstr, rule.When));
                    il.InsertBefore(first, Instruction.Create(OpCodes.Call, module.ImportReference(equality)));
                    il.InsertBefore(first, Instruction.Create(OpCodes.Brfalse, first));
                    il.InsertBefore(first, Instruction.Create(OpCodes.Ldstr, value));
                    il.InsertBefore(first, Instruction.Create(OpCodes.Ret));
                    md.Body.OptimizeMacros();
                }
            }
        }
        return hits;
    }

    /// <summary>
    /// Replaces one call of Graphics.FillRegion(Brush, Region) by three instructions that leave the
    /// stack as that call does: new Matrix(), Region.GetRegionScans(Matrix), Graphics.FillRectangles(Brush, RectangleF[]).
    /// </summary>
    static void FillRegionAsRectangles(ModuleDefinition module, MethodDefinition md, Instruction call, MethodReference fillRegion)
    {
        var graphics = fillRegion.DeclaringType;
        var brush = fillRegion.Parameters[0].ParameterType;
        var region = fillRegion.Parameters[1].ParameterType;
        var matrix = new TypeReference("System.Drawing.Drawing2D", "Matrix", module, graphics.Scope);
        var scans = new ArrayType(new TypeReference("System.Drawing", "RectangleF", module, graphics.Scope, valueType: true));

        var newMatrix = new MethodReference(".ctor", module.TypeSystem.Void, matrix) { HasThis = true };
        var getScans = new MethodReference("GetRegionScans", scans, region) { HasThis = true };
        getScans.Parameters.Add(new ParameterDefinition(matrix));
        var fillRectangles = new MethodReference("FillRectangles", module.TypeSystem.Void, graphics) { HasThis = true };
        fillRectangles.Parameters.Add(new ParameterDefinition(brush));
        fillRectangles.Parameters.Add(new ParameterDefinition(scans));

        md.Body.SimplifyMacros(); // short branches become long ones, so the two added instructions cannot push a target out of reach
        var il = md.Body.GetILProcessor();
        var second = Instruction.Create(OpCodes.Callvirt, module.ImportReference(getScans));
        var third = Instruction.Create(OpCodes.Callvirt, module.ImportReference(fillRectangles));
        call.OpCode = OpCodes.Newobj; call.Operand = module.ImportReference(newMatrix); // same instruction object: branches to it stay valid
        il.InsertAfter(call, second);
        il.InsertAfter(second, third);
        md.Body.OptimizeMacros();
    }

    const string PlacementName = "CitaviCrossover_GetCharacterPlacement";

    /// <summary>
    /// Null when a declaration of gdi32's GetCharacterPlacement has the shape the added method relies on:
    /// (IntPtr dc, char[] text, int count, int maxExtent, ref RESULTS results, int flags) returning int,
    /// RESULTS being a struct with the pointers lpDx, lpCaretPos, lpClass and the integer nGlyphs.
    /// </summary>
    static string? PlacementShapeProblem(MethodDefinition native)
    {
        var p = native.Parameters;
        if (!native.IsStatic || native.ReturnType.FullName != "System.Int32" || p.Count != 6
            || p[0].ParameterType.FullName != "System.IntPtr" || p[1].ParameterType.FullName != "System.Char[]"
            || p[2].ParameterType.FullName != "System.Int32" || p[3].ParameterType.FullName != "System.Int32"
            || p[4].ParameterType is not ByReferenceType || p[5].ParameterType.FullName != "System.Int32")
            return "no longer has the parameters (IntPtr, char[], int, int, ref results, int)";
        var results = ((ByReferenceType)p[4].ParameterType).ElementType.Resolve();
        foreach (var (field, fieldType) in new[] { ("lpDx", "System.IntPtr"), ("lpCaretPos", "System.IntPtr"), ("lpClass", "System.IntPtr"), ("nGlyphs", "System.Int32") })
            if (results?.Fields.FirstOrDefault(f => f.Name == field && !f.IsStatic)?.FieldType.FullName != fieldType)
                return $"has a results structure without the field {field}";
        return null;
    }

    /// <summary>
    /// Adds, next to the program's declaration of gdi32's GetCharacterPlacement, the method the rule
    /// kind complete-character-placement sends every call to:
    ///   static int CitaviCrossover_GetCharacterPlacement(IntPtr dc, char[] text, int count, int maxExtent, ref RESULTS results, int flags) {
    ///       int result = GetCharacterPlacement(dc, text, count, maxExtent, ref results, flags);
    ///       if ((flags &amp; 2) == 0) return result;                 // 2 = GCP_REORDER
    ///       int n = Math.Min(results.nGlyphs, Math.Min(count, text.Length)), position = 0;
    ///       for (int i = 0; i &lt; n; i++) {
    ///           if (results.lpCaretPos != 0) Marshal.WriteInt32(results.lpCaretPos, 4 * i, position);
    ///           if (results.lpDx != 0) position += Marshal.ReadInt32(results.lpDx, 4 * i);
    ///           if (results.lpClass != 0) Marshal.WriteByte(results.lpClass, i, CitaviCrossover_CharacterClass(text[i]));
    ///       }
    ///       return result;
    ///   }
    ///   static byte CitaviCrossover_CharacterClass(char c)      // 2 = GCPCLASS_HEBREW and _ARABIC, 1 = GCPCLASS_LATIN
    ///       => c is in U+0590..U+08FF, U+FB1D..U+FDFF or U+FE70..U+FEFC (Hebrew, Arabic and their neighbours) ? 2 : 1;
    /// With GCP_REORDER Wine fills in the widths (lpDx) and the order but neither the caret positions
    /// nor the classes, which stay whatever the memory held. Without it Wine computes the caret positions
    /// itself, as this running sum.
    /// </summary>
    static MethodDefinition PlacementHelper(ModuleDefinition module, TypeDefinition type, MethodDefinition native)
    {
        var results = ((ByReferenceType)native.Parameters[4].ParameterType).ElementType.Resolve();
        FieldReference Field(string n) => module.ImportReference(results.Fields.First(f => f.Name == n && !f.IsStatic));
        FieldReference lpDx = Field("lpDx"), lpCaretPos = Field("lpCaretPos"), lpClass = Field("lpClass"), nGlyphs = Field("nGlyphs");
        var marshal = new TypeReference("System.Runtime.InteropServices", "Marshal", module, module.TypeSystem.CoreLibrary);
        MethodReference MarshalMethod(string n, TypeReference returns, params TypeReference[] parameters)
        {
            var m = new MethodReference(n, returns, marshal);
            foreach (var t in parameters) m.Parameters.Add(new ParameterDefinition(t));
            return module.ImportReference(m);
        }
        var ts = module.TypeSystem;
        var writeInt32 = MarshalMethod("WriteInt32", ts.Void, ts.IntPtr, ts.Int32, ts.Int32);
        var readInt32 = MarshalMethod("ReadInt32", ts.Int32, ts.IntPtr, ts.Int32);
        var writeByte = MarshalMethod("WriteByte", ts.Void, ts.IntPtr, ts.Int32, ts.Byte);

        var charClass = new MethodDefinition("CitaviCrossover_CharacterClass", MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig, ts.Byte);
        charClass.Parameters.Add(new ParameterDefinition("c", ParameterAttributes.None, ts.Char));
        {
            var il = charClass.Body.GetILProcessor();
            var latin = Instruction.Create(OpCodes.Ldc_I4_1);
            var rightToLeft = Instruction.Create(OpCodes.Ldc_I4_2);
            foreach (var (first, last) in new[] { (0x0590, 0x08FF), (0xFB1D, 0xFDFF), (0xFE70, 0xFEFC) })
            {
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4, first); il.Emit(OpCodes.Blt, latin);   // the ranges ascend
                il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldc_I4, last); il.Emit(OpCodes.Ble, rightToLeft);
            }
            il.Append(latin); il.Emit(OpCodes.Ret);
            il.Append(rightToLeft); il.Emit(OpCodes.Ret);
        }
        type.Methods.Add(charClass);

        var helper = new MethodDefinition(PlacementName, MethodAttributes.Assembly | MethodAttributes.Static | MethodAttributes.HideBySig, ts.Int32);
        foreach (var p in native.Parameters) helper.Parameters.Add(new ParameterDefinition(p.Name, ParameterAttributes.None, p.ParameterType));
        ParameterDefinition text = helper.Parameters[1], count = helper.Parameters[2], res = helper.Parameters[4], flags = helper.Parameters[5];
        helper.Body.InitLocals = true;
        VariableDefinition Local() { var v = new VariableDefinition(ts.Int32); helper.Body.Variables.Add(v); return v; }
        VariableDefinition result = Local(), n = Local(), i = Local(), position = Local();
        {
            var il = helper.Body.GetILProcessor();
            var done = Instruction.Create(OpCodes.Ldloc, result);
            var condition = Instruction.Create(OpCodes.Ldloc, i);
            foreach (var p in helper.Parameters) il.Emit(OpCodes.Ldarg, p);
            il.Emit(OpCodes.Call, native); il.Emit(OpCodes.Stloc, result);
            il.Emit(OpCodes.Ldarg, flags); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.And); il.Emit(OpCodes.Brfalse, done);
            // n = min(results.nGlyphs, count, text.Length)
            il.Emit(OpCodes.Ldarg, res); il.Emit(OpCodes.Ldfld, nGlyphs); il.Emit(OpCodes.Stloc, n);
            var countChecked = Instruction.Create(OpCodes.Ldloc, n);
            il.Emit(OpCodes.Ldloc, n); il.Emit(OpCodes.Ldarg, count); il.Emit(OpCodes.Ble, countChecked);
            il.Emit(OpCodes.Ldarg, count); il.Emit(OpCodes.Stloc, n);
            il.Append(countChecked); il.Emit(OpCodes.Ldarg, text); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Ble, condition);
            il.Emit(OpCodes.Ldarg, text); il.Emit(OpCodes.Ldlen); il.Emit(OpCodes.Conv_I4); il.Emit(OpCodes.Stloc, n);
            il.Emit(OpCodes.Br, condition);
            // loop body
            var body = Instruction.Create(OpCodes.Ldarg, res);
            var afterCaret = Instruction.Create(OpCodes.Ldarg, res);
            var afterDx = Instruction.Create(OpCodes.Ldarg, res);
            var next = Instruction.Create(OpCodes.Ldloc, i);
            il.Append(body); il.Emit(OpCodes.Ldfld, lpCaretPos); il.Emit(OpCodes.Brfalse, afterCaret);
            il.Emit(OpCodes.Ldarg, res); il.Emit(OpCodes.Ldfld, lpCaretPos);
            il.Emit(OpCodes.Ldloc, i); il.Emit(OpCodes.Ldc_I4_4); il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Ldloc, position); il.Emit(OpCodes.Call, writeInt32);
            il.Append(afterCaret); il.Emit(OpCodes.Ldfld, lpDx); il.Emit(OpCodes.Brfalse, afterDx);
            il.Emit(OpCodes.Ldloc, position);
            il.Emit(OpCodes.Ldarg, res); il.Emit(OpCodes.Ldfld, lpDx);
            il.Emit(OpCodes.Ldloc, i); il.Emit(OpCodes.Ldc_I4_4); il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Call, readInt32); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, position);
            il.Append(afterDx); il.Emit(OpCodes.Ldfld, lpClass); il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldarg, res); il.Emit(OpCodes.Ldfld, lpClass); il.Emit(OpCodes.Ldloc, i);
            il.Emit(OpCodes.Ldarg, text); il.Emit(OpCodes.Ldloc, i); il.Emit(OpCodes.Ldelem_U2); il.Emit(OpCodes.Call, charClass);
            il.Emit(OpCodes.Call, writeByte);
            il.Append(next); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Add); il.Emit(OpCodes.Stloc, i);
            il.Append(condition); il.Emit(OpCodes.Ldloc, n); il.Emit(OpCodes.Blt, body);
            il.Append(done); il.Emit(OpCodes.Ret);
            helper.Body.OptimizeMacros();
        }
        type.Methods.Add(helper);
        return helper;
    }

    const string ThumbName = "CitaviCrossover_DrawThumb";

    /// <summary>
    /// Adds to a control class the two methods of the rule kind draw-trackbar-thumb:
    ///   protected override void WndProc(ref Message m) {
    ///       if (m.Msg == 0x4E &amp;&amp; CitaviCrossover_DrawThumb(ref m)) return;      // WM_NOTIFY
    ///       base.WndProc(ref m);
    ///   }
    ///   bool CitaviCrossover_DrawThumb(ref Message m) {                // true: the message is answered
    ///       TrackBar bar = field; IntPtr p = m.LParam; int size = IntPtr.Size;        // p: NMCUSTOMDRAW
    ///       if (bar == null || !bar.IsHandleCreated || ReadIntPtr(p) != bar.Handle || ReadInt32(p, 2 * size) != -12) return false;   // NM_CUSTOMDRAW
    ///       int stage = ReadInt32(p, 3 * size);
    ///       if (stage == 1) { m.Result = 0x20; return true; }           // CDDS_PREPAINT: answer CDRF_NOTIFYITEMDRAW
    ///       if (stage != 0x10001 || ReadInt32(p, 5 * size + 16) != 2) return false;  // CDDS_ITEMPREPAINT of TBCD_THUMB
    ///       float left, top, right, bottom = the four integers at p + 5 * size;       // the thumb's rectangle
    ///       float shoulder = bottom - (right - left) / 2;
    ///       bool grey = !bar.Enabled || (ReadInt32(p, 6 * size + 16) &amp; 0x40) != 0;  // CDIS_HOT: Wine sets it while dragging
    ///       try {
    ///           Graphics g = Graphics.FromHdc(ReadIntPtr(p, 4 * size));
    ///           SolidBrush brush = new SolidBrush(grey ? Color.FromArgb(204, 204, 204) : Color.FromArgb(0, 120, 215));
    ///           g.SmoothingMode = SmoothingMode.AntiAlias; g.PixelOffsetMode = PixelOffsetMode.Half;
    ///           g.FillPolygon(brush, new[] { (left, top), (right, top), (right, shoulder), ((left + right) / 2, bottom), (left, shoulder) });
    ///           brush.Dispose(); g.Dispose();
    ///       } catch (Exception) { return false; }                      // the slider then draws its own thumb
    ///       m.Result = 4; return true;                                  // CDRF_SKIPDEFAULT
    ///   }
    /// The offsets hold for 32 and 64 bits: NMCUSTOMDRAW is three pointer-sized fields (the last an
    /// integer, padded), the stage, the drawing surface, the rectangle, the item and its state.
    /// The colours are those of Windows 10's slider.
    /// </summary>
    static void AddThumbDrawing(ModuleDefinition module, TypeDefinition type, FieldDefinition field, string ruleId)
    {
        AssemblyNameReference Uses(string assembly) => module.AssemblyReferences.FirstOrDefault(a => a.Name == assembly)
            ?? throw new UserError($"rule {ruleId}: {module.Name} does not use {assembly}");
        var forms = Uses("System.Windows.Forms");
        var drawing = Uses("System.Drawing");
        var ts = module.TypeSystem;
        MethodReference Method(TypeReference declaring, string name, TypeReference returns, bool instance, params TypeReference[] parameters)
        {
            var m = new MethodReference(name, returns, declaring) { HasThis = instance };
            foreach (var t in parameters) m.Parameters.Add(new ParameterDefinition(t));
            return module.ImportReference(m);
        }
        var message = new TypeReference("System.Windows.Forms", "Message", module, forms, valueType: true);
        var messageRef = new ByReferenceType(message);
        var control = new TypeReference("System.Windows.Forms", "Control", module, forms);
        var marshal = new TypeReference("System.Runtime.InteropServices", "Marshal", module, ts.CoreLibrary);
        var exception = new TypeReference("System", "Exception", module, ts.CoreLibrary);
        var graphics = new TypeReference("System.Drawing", "Graphics", module, drawing);
        var brush = new TypeReference("System.Drawing", "Brush", module, drawing);
        var solidBrush = new TypeReference("System.Drawing", "SolidBrush", module, drawing);
        var color = new TypeReference("System.Drawing", "Color", module, drawing, valueType: true);
        var pointF = new TypeReference("System.Drawing", "PointF", module, drawing, valueType: true);
        var smoothingMode = new TypeReference("System.Drawing.Drawing2D", "SmoothingMode", module, drawing, valueType: true);
        var pixelOffsetMode = new TypeReference("System.Drawing.Drawing2D", "PixelOffsetMode", module, drawing, valueType: true);

        var getMsg = Method(message, "get_Msg", ts.Int32, true);
        var getLParam = Method(message, "get_LParam", ts.IntPtr, true);
        var setResult = Method(message, "set_Result", ts.Void, true, ts.IntPtr);
        var isHandleCreated = Method(control, "get_IsHandleCreated", ts.Boolean, true);
        var getHandle = Method(control, "get_Handle", ts.IntPtr, true);
        var getEnabled = Method(control, "get_Enabled", ts.Boolean, true);
        var pointerSize = Method(ts.IntPtr, "get_Size", ts.Int32, false);
        var readPointer = Method(marshal, "ReadIntPtr", ts.IntPtr, false, ts.IntPtr);
        var readPointerAt = Method(marshal, "ReadIntPtr", ts.IntPtr, false, ts.IntPtr, ts.Int32);
        var readInt32 = Method(marshal, "ReadInt32", ts.Int32, false, ts.IntPtr, ts.Int32);
        var fromHdc = Method(graphics, "FromHdc", graphics, false, ts.IntPtr);
        var setSmoothing = Method(graphics, "set_SmoothingMode", ts.Void, true, smoothingMode);
        var setPixelOffset = Method(graphics, "set_PixelOffsetMode", ts.Void, true, pixelOffsetMode);
        var fillPolygon = Method(graphics, "FillPolygon", ts.Void, true, brush, new ArrayType(pointF));
        var disposeGraphics = Method(graphics, "Dispose", ts.Void, true);
        var disposeBrush = Method(brush, "Dispose", ts.Void, true);
        var fromArgb = Method(color, "FromArgb", color, false, ts.Int32, ts.Int32, ts.Int32);
        var newBrush = Method(solidBrush, ".ctor", ts.Void, true, color);
        var newPoint = Method(pointF, ".ctor", ts.Void, true, ts.Single, ts.Single);
        // the nearest WndProc above the class; the runtime looks it up from the base class upwards
        var baseWndProc = Method(type.BaseType, "WndProc", ts.Void, true, messageRef);

        var draw = new MethodDefinition(ThumbName, MethodAttributes.Private | MethodAttributes.HideBySig, ts.Boolean);
        draw.Parameters.Add(new ParameterDefinition("m", ParameterAttributes.None, messageRef));
        draw.Body.InitLocals = true;
        VariableDefinition Local(TypeReference t) { var v = new VariableDefinition(t); draw.Body.Variables.Add(v); return v; }
        VariableDefinition bar = Local(field.FieldType), p = Local(ts.IntPtr), size = Local(ts.Int32), stage = Local(ts.Int32),
            left = Local(ts.Single), top = Local(ts.Single), right = Local(ts.Single), bottom = Local(ts.Single), shoulder = Local(ts.Single),
            grey = Local(ts.Int32), g = Local(graphics), fill = Local(solidBrush);
        {
            var il = draw.Body.GetILProcessor();
            var no = Instruction.Create(OpCodes.Ldc_I4_0);
            // pushes the 32-bit integer at p + pointers * size + extra
            void Int32At(int pointers, int extra)
            {
                il.Emit(OpCodes.Ldloc, p); il.Emit(OpCodes.Ldc_I4, pointers); il.Emit(OpCodes.Ldloc, size); il.Emit(OpCodes.Mul);
                if (extra != 0) { il.Emit(OpCodes.Ldc_I4, extra); il.Emit(OpCodes.Add); }
                il.Emit(OpCodes.Call, readInt32);
            }
            void SetResult(int value) { il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Ldc_I4, value); il.Emit(OpCodes.Conv_I); il.Emit(OpCodes.Call, setResult); }

            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Stloc, bar);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, getLParam); il.Emit(OpCodes.Stloc, p);
            il.Emit(OpCodes.Call, pointerSize); il.Emit(OpCodes.Stloc, size);
            // only the slider's own NM_CUSTOMDRAW
            il.Emit(OpCodes.Ldloc, bar); il.Emit(OpCodes.Brfalse, no);
            il.Emit(OpCodes.Ldloc, bar); il.Emit(OpCodes.Callvirt, isHandleCreated); il.Emit(OpCodes.Brfalse, no);
            il.Emit(OpCodes.Ldloc, p); il.Emit(OpCodes.Call, readPointer);
            il.Emit(OpCodes.Ldloc, bar); il.Emit(OpCodes.Callvirt, getHandle); il.Emit(OpCodes.Bne_Un, no);
            Int32At(2, 0); il.Emit(OpCodes.Ldc_I4, -12); il.Emit(OpCodes.Bne_Un, no);
            // before painting: ask to be told about each part
            var notPrepaint = Instruction.Create(OpCodes.Ldloc, stage);
            Int32At(3, 0); il.Emit(OpCodes.Stloc, stage);
            il.Emit(OpCodes.Ldloc, stage); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Bne_Un, notPrepaint);
            SetResult(0x20); il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
            // only the thumb, before it is drawn
            il.Append(notPrepaint); il.Emit(OpCodes.Ldc_I4, 0x10001); il.Emit(OpCodes.Bne_Un, no);
            Int32At(5, 16); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Bne_Un, no);
            int offset = 0;
            foreach (var edge in new[] { left, top, right, bottom })
            {
                Int32At(5, offset); il.Emit(OpCodes.Conv_R4); il.Emit(OpCodes.Stloc, edge);
                offset += 4;
            }
            il.Emit(OpCodes.Ldloc, bottom); il.Emit(OpCodes.Ldloc, right); il.Emit(OpCodes.Ldloc, left); il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Ldc_R4, 2f); il.Emit(OpCodes.Div); il.Emit(OpCodes.Sub); il.Emit(OpCodes.Stloc, shoulder);
            // grey = !bar.Enabled || (state & CDIS_HOT) != 0
            var greyKnown = Instruction.Create(OpCodes.Ldloc, p);
            il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Stloc, grey);
            il.Emit(OpCodes.Ldloc, bar); il.Emit(OpCodes.Callvirt, getEnabled); il.Emit(OpCodes.Brfalse, greyKnown);
            Int32At(6, 16); il.Emit(OpCodes.Ldc_I4, 0x40); il.Emit(OpCodes.And); il.Emit(OpCodes.Brtrue, greyKnown);
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Stloc, grey);
            // try { draw }
            var greyColour = Instruction.Create(OpCodes.Ldc_I4, 204);
            var makeBrush = Instruction.Create(OpCodes.Newobj, newBrush);
            var caught = Instruction.Create(OpCodes.Pop);
            var drawn = Instruction.Create(OpCodes.Ldarg_1);
            il.Append(greyKnown); il.Emit(OpCodes.Ldc_I4_4); il.Emit(OpCodes.Ldloc, size); il.Emit(OpCodes.Mul);
            il.Emit(OpCodes.Call, readPointerAt); il.Emit(OpCodes.Call, fromHdc); il.Emit(OpCodes.Stloc, g);
            il.Emit(OpCodes.Ldloc, grey); il.Emit(OpCodes.Brtrue, greyColour);
            il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldc_I4, 120); il.Emit(OpCodes.Ldc_I4, 215); il.Emit(OpCodes.Call, fromArgb);
            il.Emit(OpCodes.Br, makeBrush);
            il.Append(greyColour); il.Emit(OpCodes.Ldc_I4, 204); il.Emit(OpCodes.Ldc_I4, 204); il.Emit(OpCodes.Call, fromArgb);
            il.Append(makeBrush); il.Emit(OpCodes.Stloc, fill);
            il.Emit(OpCodes.Ldloc, g); il.Emit(OpCodes.Ldc_I4_4); il.Emit(OpCodes.Callvirt, setSmoothing);    // SmoothingMode.AntiAlias
            il.Emit(OpCodes.Ldloc, g); il.Emit(OpCodes.Ldc_I4_4); il.Emit(OpCodes.Callvirt, setPixelOffset);  // PixelOffsetMode.Half
            il.Emit(OpCodes.Ldloc, g); il.Emit(OpCodes.Ldloc, fill);
            il.Emit(OpCodes.Ldc_I4_5); il.Emit(OpCodes.Newarr, pointF);
            void Corner(int index, Action x, VariableDefinition y)
            {
                il.Emit(OpCodes.Dup); il.Emit(OpCodes.Ldc_I4, index); x(); il.Emit(OpCodes.Ldloc, y);
                il.Emit(OpCodes.Newobj, newPoint); il.Emit(OpCodes.Stelem_Any, pointF);
            }
            Corner(0, () => il.Emit(OpCodes.Ldloc, left), top);
            Corner(1, () => il.Emit(OpCodes.Ldloc, right), top);
            Corner(2, () => il.Emit(OpCodes.Ldloc, right), shoulder);
            Corner(3, () => { il.Emit(OpCodes.Ldloc, left); il.Emit(OpCodes.Ldloc, right); il.Emit(OpCodes.Add); il.Emit(OpCodes.Ldc_R4, 2f); il.Emit(OpCodes.Div); }, bottom);
            Corner(4, () => il.Emit(OpCodes.Ldloc, left), shoulder);
            il.Emit(OpCodes.Callvirt, fillPolygon);
            il.Emit(OpCodes.Ldloc, fill); il.Emit(OpCodes.Callvirt, disposeBrush);
            il.Emit(OpCodes.Ldloc, g); il.Emit(OpCodes.Callvirt, disposeGraphics);
            il.Emit(OpCodes.Leave, drawn);
            // catch (Exception) { return false; }
            il.Append(caught); il.Emit(OpCodes.Leave, no);
            il.Append(drawn); il.Emit(OpCodes.Ldc_I4_4); il.Emit(OpCodes.Conv_I); il.Emit(OpCodes.Call, setResult);
            il.Emit(OpCodes.Ldc_I4_1); il.Emit(OpCodes.Ret);
            il.Append(no); il.Emit(OpCodes.Ret);
            draw.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch)
            {
                TryStart = greyKnown, TryEnd = caught, HandlerStart = caught, HandlerEnd = drawn, CatchType = module.ImportReference(exception),
            });
            draw.Body.OptimizeMacros();
        }
        type.Methods.Add(draw);

        var wndProc = new MethodDefinition("WndProc", MethodAttributes.Family | MethodAttributes.Virtual | MethodAttributes.HideBySig, ts.Void);
        wndProc.Parameters.Add(new ParameterDefinition("m", ParameterAttributes.None, messageRef));
        {
            var il = wndProc.Body.GetILProcessor();
            var pass = Instruction.Create(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, getMsg); il.Emit(OpCodes.Ldc_I4, 0x4E); il.Emit(OpCodes.Bne_Un, pass);
            il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, draw); il.Emit(OpCodes.Brfalse, pass);
            il.Emit(OpCodes.Ret);
            il.Append(pass); il.Emit(OpCodes.Ldarg_1); il.Emit(OpCodes.Call, baseWndProc); il.Emit(OpCodes.Ret);
            wndProc.Body.OptimizeMacros();
        }
        type.Methods.Add(wndProc);
    }

    const string WindowWidthName = "CitaviCrossover_WindowWidth";

    /// <summary>
    /// Adds to a control class (once) the method the rule kind set-field-to-window-width calls:
    ///   int CitaviCrossover_WindowWidth() {
    ///       int[] rect = new int[4];                          // left, top, right, bottom
    ///       return IsHandleCreated &amp;&amp; CitaviCrossover_GetWindowRect(Handle, rect) != 0 ? rect[2] - rect[0] : field;
    ///   }
    /// with CitaviCrossover_GetWindowRect being user32's GetWindowRect. Without a window, or if the
    /// call fails, the field keeps the value it has.
    /// </summary>
    static MethodDefinition WindowWidthHelper(ModuleDefinition module, TypeDefinition type, FieldReference field, string ruleId)
    {
        var existing = type.Methods.FirstOrDefault(m => m.Name == WindowWidthName);
        if (existing != null) return existing;
        var forms = module.AssemblyReferences.FirstOrDefault(a => a.Name == "System.Windows.Forms")
            ?? throw new UserError($"rule {ruleId}: {module.Name} does not use System.Windows.Forms");
        var ints = new ArrayType(module.TypeSystem.Int32);

        var user32 = module.ModuleReferences.FirstOrDefault(m => string.Equals(m.Name, "user32.dll", StringComparison.OrdinalIgnoreCase));
        if (user32 == null) { user32 = new ModuleReference("user32.dll"); module.ModuleReferences.Add(user32); }
        var getWindowRect = new MethodDefinition("CitaviCrossover_GetWindowRect",
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig | MethodAttributes.PInvokeImpl,
            module.TypeSystem.Int32)
        { ImplAttributes = MethodImplAttributes.PreserveSig };
        getWindowRect.Parameters.Add(new ParameterDefinition("window", ParameterAttributes.None, module.TypeSystem.IntPtr));
        getWindowRect.Parameters.Add(new ParameterDefinition("rect", ParameterAttributes.In | ParameterAttributes.Out, ints));
        getWindowRect.PInvokeInfo = new PInvokeInfo(PInvokeAttributes.CallConvWinapi, "GetWindowRect", user32);
        type.Methods.Add(getWindowRect);

        var control = new TypeReference("System.Windows.Forms", "Control", module, forms);
        var getHandle = new MethodReference("get_Handle", module.TypeSystem.IntPtr, control) { HasThis = true };
        var isHandleCreated = new MethodReference("get_IsHandleCreated", module.TypeSystem.Boolean, control) { HasThis = true };
        var helper = new MethodDefinition(WindowWidthName, MethodAttributes.Private | MethodAttributes.HideBySig, module.TypeSystem.Int32);
        helper.Body.InitLocals = true;
        helper.Body.Variables.Add(new VariableDefinition(ints));
        var il = helper.Body.GetILProcessor();
        var failed = Instruction.Create(OpCodes.Ldarg_0);
        il.Emit(OpCodes.Ldc_I4_4); il.Emit(OpCodes.Newarr, module.TypeSystem.Int32); il.Emit(OpCodes.Stloc_0);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, module.ImportReference(isHandleCreated));
        il.Emit(OpCodes.Brfalse, failed);
        il.Emit(OpCodes.Ldarg_0); il.Emit(OpCodes.Call, module.ImportReference(getHandle));
        il.Emit(OpCodes.Ldloc_0); il.Emit(OpCodes.Call, getWindowRect);
        il.Emit(OpCodes.Brfalse, failed);
        il.Emit(OpCodes.Ldloc_0); il.Emit(OpCodes.Ldc_I4_2); il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Ldloc_0); il.Emit(OpCodes.Ldc_I4_0); il.Emit(OpCodes.Ldelem_I4);
        il.Emit(OpCodes.Sub); il.Emit(OpCodes.Ret);
        il.Append(failed); il.Emit(OpCodes.Ldfld, field); il.Emit(OpCodes.Ret);
        type.Methods.Add(helper);
        return helper;
    }

    /// <summary>The integer an ldc.i4* instruction pushes, or null for any other instruction.</summary>
    static int? IntConstant(Instruction ins)
    {
        var code = ins.OpCode.Code;
        if (code == Code.Ldc_I4) return (int)ins.Operand;
        if (code == Code.Ldc_I4_S) return (sbyte)ins.Operand;
        if (code == Code.Ldc_I4_M1) return -1;
        if (code >= Code.Ldc_I4_0 && code <= Code.Ldc_I4_8) return code - Code.Ldc_I4_0;
        return null;
    }

    static IEnumerable<Instruction> ConstantBody(MethodDefinition md, JsonElement value, string ruleId)
    {
        var rt = md.ReturnType.FullName;
        if (rt == "System.Void") { yield return Instruction.Create(OpCodes.Ret); yield break; }
        switch (value.ValueKind)
        {
            case JsonValueKind.True or JsonValueKind.False when rt == "System.Boolean":
                yield return Instruction.Create(value.GetBoolean() ? OpCodes.Ldc_I4_1 : OpCodes.Ldc_I4_0); break;
            case JsonValueKind.Number when IsSmallInteger(md.ReturnType):
                yield return Instruction.Create(OpCodes.Ldc_I4, value.GetInt32()); break;
            case JsonValueKind.String when rt == "System.String":
                yield return Instruction.Create(OpCodes.Ldstr, value.GetString()!); break;
            case JsonValueKind.Null when !md.ReturnType.IsValueType:
                yield return Instruction.Create(OpCodes.Ldnull); break;
            default:
                throw new UserError($"rule {ruleId}: value {value.GetRawText()} does not fit return type {rt}");
        }
        yield return Instruction.Create(OpCodes.Ret);
    }

    /// <summary>True for the integer types a 32-bit constant can be returned as, and for enums built on one of them.</summary>
    static bool IsSmallInteger(TypeReference type)
    {
        if (type.FullName is "System.Int32" or "System.UInt32" or "System.Int16" or "System.Byte") return true;
        TypeDefinition? def;
        try { def = type.Resolve(); } catch (AssemblyResolutionException) { return false; }
        if (def == null || !def.IsEnum) return false;
        var underlying = def.Fields.FirstOrDefault(f => !f.IsStatic)?.FieldType;
        return underlying != null && IsSmallInteger(underlying);
    }

    static bool IsAlreadyConstant(MethodDefinition md, JsonElement value)
    {
        try
        {
            var want = ConstantBody(md, value, "").ToList();
            var have = md.Body.Instructions;
            if (have.Count != want.Count) return false;
            for (int i = 0; i < want.Count; i++)
                if (have[i].OpCode != want[i].OpCode || !Equals(have[i].Operand, want[i].Operand)) return false;
            return true;
        }
        catch (UserError) { return false; }
    }

    static void ReplaceWithConstant(MethodDefinition md, Rule rule)
    {
        var body = ConstantBody(md, ValueOf(rule), rule.Id).ToList();
        md.Body.Instructions.Clear();
        md.Body.ExceptionHandlers.Clear();
        md.Body.Variables.Clear();
        foreach (var i in body) md.Body.Instructions.Add(i);
    }

    // ---------------------------------------------------------------- commands

    static List<Hit> Pending(string bin, RuleSet rules, Action<string>? warn = null)
    {
        var all = RulesWithoutFile(bin, rules);
        foreach (var file in ManagedCandidates(bin))
        {
            var bytes = File.ReadAllBytes(file);
            if (!MightMatch(file, bytes, rules)) continue;
            using var module = TryRead(file, bytes, bin);
            if (module == null) continue;
            try { all.AddRange(Process(file, module, rules, mutate: false)); }
            catch (Exception e) when (e is not UserError) { warn?.Invoke($"{Path.GetFileName(file)}: {e.Message}"); }
        }
        return all;
    }

    static void Print(IEnumerable<Hit> hits)
    {
        foreach (var g in hits.GroupBy(h => (h.RuleId, h.File)).OrderBy(g => g.Key.RuleId).ThenBy(g => g.Key.File))
        {
            Console.WriteLine($"  [{g.Key.RuleId}] {g.Key.File}: {g.Count()} site(s)");
            foreach (var w in g.Select(h => h.Where).Distinct().Take(12)) Console.WriteLine($"      {w}");
            if (g.Select(h => h.Where).Distinct().Count() > 12) Console.WriteLine("      ...");
        }
    }

    static int Scan(string bin, RuleSet rules)
    {
        var hits = Pending(bin, rules, w => Console.WriteLine("  warning: " + w));
        if (hits.Count == 0) { Console.WriteLine("Nothing to patch: every rule is already satisfied."); return 0; }
        Console.WriteLine($"{hits.Count} site(s) would be patched:");
        Print(hits);
        return 0;
    }

    static int Verify(string bin, RuleSet rules)
    {
        var hits = Pending(bin, rules);
        var missing = hits.Where(h => h.Where.StartsWith("MISSING ")).ToList();
        var todo = hits.Except(missing).ToList();
        if (missing.Count > 0)
        {
            Console.WriteLine("Rules whose target no longer exists (Citavi changed; see docs/ADAPTING.md):");
            Print(missing);
        }
        if (todo.Count > 0) { Console.WriteLine($"NOT PATCHED: {todo.Count} site(s) still need patching:"); Print(todo); }
        if (missing.Count == 0 && todo.Count == 0) Console.WriteLine("OK: all patch rules are satisfied.");
        return todo.Count > 0 ? 1 : missing.Count > 0 ? 3 : 0;
    }

    static string Sha(byte[] b)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(b)).Replace("-", "").ToLowerInvariant();
    }

    static int Apply(string bin, RuleSet rules, string backup, bool dryRun)
    {
        Directory.CreateDirectory(backup);
        string manifestPath = Path.Combine(backup, "manifest.json");
        var manifest = File.Exists(manifestPath)
            ? JsonSerializer.Deserialize(File.ReadAllText(manifestPath), Json.Default.Manifest) ?? new Manifest()
            : new Manifest();

        int changedFiles = 0, sites = 0, failed = 0; var missing = RulesWithoutFile(bin, rules);
        void SaveManifest()
        {
            manifest.AppliedAt = DateTime.UtcNow.ToString("o");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, Json.Default.Manifest));
        }
        foreach (var file in ManagedCandidates(bin))
        {
            var bytes = File.ReadAllBytes(file);
            if (!MightMatch(file, bytes, rules)) continue;
            using var module = TryRead(file, bytes, bin);
            if (module == null) continue;
            string name = Path.GetFileName(file);

            var hits = Process(file, module, rules, mutate: !dryRun);
            missing.AddRange(hits.Where(h => h.Where.StartsWith("MISSING ")));
            hits = hits.Where(h => !h.Where.StartsWith("MISSING ")).ToList();
            if (hits.Count == 0) continue;

            if ((module.Attributes & ModuleAttributes.ILOnly) == 0)
            {
                Console.WriteLine($"  SKIPPED {name}: mixed-mode assembly cannot be rewritten ({hits.Count} site(s) left unpatched)");
                continue;
            }
            Print(hits);
            sites += hits.Count; changedFiles++;
            if (dryRun) continue;

            byte[] patched;
            try
            {
                using var ms = new MemoryStream();
                module.Write(ms); // strong-name signature is dropped; .NET 4 full-trust apps do not verify it
                patched = ms.ToArray();
            }
            catch (Exception e) when (e is not UserError)
            {
                Console.WriteLine($"  FAILED {name}: {e.GetType().Name}: {e.Message}");
                failed++; sites -= hits.Count; changedFiles--;
                continue;
            }

            // Keep the pristine original exactly once per original content.
            string origSha = Sha(bytes);
            bool isOurOutput = manifest.Files.TryGetValue(name, out var prev) && prev.PatchedSha256 == origSha;
            if (!isOurOutput)
            {
                File.WriteAllBytes(Path.Combine(backup, name), bytes);
                prev = new ManifestEntry { OriginalSha256 = origSha };
                manifest.Files[name] = prev;
            }
            prev!.PatchedSha256 = Sha(patched);
            foreach (var id in hits.Select(h => h.RuleId).Distinct())
                if (!prev.Rules.Contains(id)) prev.Rules.Add(id);
            SaveManifest(); // before touching the file, so a crash can never leave an untracked patched file

            string tmp = file + ".citavi-patcher.tmp";
            File.WriteAllBytes(tmp, patched);
            File.Delete(file);
            File.Move(tmp, file);
        }

        if (failed > 0) Console.WriteLine($"{failed} file(s) could not be rewritten; pass --framework <dir> for the bottle's .NET Framework folders.");
        if (missing.Count > 0)
        {
            Console.WriteLine("Rules whose target no longer exists (Citavi changed; see docs/ADAPTING.md):");
            Print(missing);
        }
        Console.WriteLine(sites == 0
            ? "Nothing to patch: every rule is already satisfied."
            : $"{(dryRun ? "Would patch" : "Patched")} {sites} site(s) in {changedFiles} file(s).{(dryRun ? "" : $" Originals: {backup}")}");
        return failed > 0 ? 4 : missing.Count > 0 ? 3 : 0;
    }

    static int Restore(string bin, string backup)
    {
        string manifestPath = Path.Combine(backup, "manifest.json");
        if (!File.Exists(manifestPath)) throw new UserError($"no manifest.json in {backup}");
        var manifest = JsonSerializer.Deserialize(File.ReadAllText(manifestPath), Json.Default.Manifest)!;
        int n = 0;
        foreach (var pair in manifest.Files)
        {
            string name = pair.Key; var entry = pair.Value;
            string src = Path.Combine(backup, name), dst = Path.Combine(bin, name);
            if (!File.Exists(src) || !File.Exists(dst)) continue;
            // Only undo our own output: if Citavi's updater replaced the file, leave the new one alone.
            if (Sha(File.ReadAllBytes(dst)) != entry.PatchedSha256) { Console.WriteLine($"  kept {name} (changed since patching)"); continue; }
            File.Copy(src, dst, overwrite: true); n++;
            Console.WriteLine($"  restored {name}");
        }
        File.Delete(manifestPath);
        Console.WriteLine($"Restored {n} file(s).");
        return 0;
    }

    /// <summary>Prints the file version of Citavi.exe (used to label backups and decompiler output).</summary>
    static int Info(string bin)
    {
        string exe = Path.Combine(bin, "Citavi.exe");
        if (!File.Exists(exe)) throw new UserError($"Citavi.exe not found in {bin}");
        using var module = TryRead(exe, File.ReadAllBytes(exe), bin) ?? throw new UserError("Citavi.exe is not a .NET assembly");
        string? fileVersion = module.Assembly.CustomAttributes
            .FirstOrDefault(a => a.AttributeType.FullName == "System.Reflection.AssemblyFileVersionAttribute")
            ?.ConstructorArguments.FirstOrDefault().Value as string;
        Console.WriteLine(fileVersion ?? module.Assembly.Name.Version.ToString());
        return 0;
    }

    /// <summary>Investigation aid: which methods call something matching the pattern?</summary>
    static int Find(string bin, string pattern)
    {
        string methodPart = pattern.Contains("::") ? pattern[(pattern.IndexOf("::", StringComparison.Ordinal) + 2)..] : pattern;
        foreach (var file in ManagedCandidates(bin))
        {
            var bytes = File.ReadAllBytes(file);
            if (!ContainsAscii(bytes, methodPart)) continue;
            using var module = TryRead(file, bytes, bin);
            if (module == null) continue;
            foreach (var md in AllMethods(module))
            {
                if (!md.HasBody) continue;
                foreach (var ins in md.Body.Instructions)
                    if (ins.Operand is MethodReference mr && $"{mr.DeclaringType.FullName}::{mr.Name}".IndexOf(pattern, StringComparison.Ordinal) >= 0)
                        Console.WriteLine($"{Path.GetFileName(file)}: {md.DeclaringType.FullName}::{md.Name} -> {mr.DeclaringType.FullName}::{mr.Name}");
            }
        }
        return 0;
    }
}
