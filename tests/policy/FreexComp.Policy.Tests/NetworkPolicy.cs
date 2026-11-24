using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;

namespace FreexComp.Policy.Tests;

/// <summary>One call site that is able to open a connection: (assembly, outermost user type, kind) and what it uses.</summary>
internal sealed record NetworkFinding(string Assembly, string Type, string Kind, string Target);

/// <summary>What the scanner read from one assembly.</summary>
/// <param name="Name">Simple name of the assembly.</param>
/// <param name="Findings">Distinct, ordered by type, kind, target.</param>
/// <param name="ReferenceCount">Every type or member reference the scanner looked at (shows that it is not blind).</param>
/// <param name="PipeProblems">Pipe clients that are not constructed with the literal server name "." (NET-016).</param>
internal sealed record AssemblyScan(string Name, IReadOnlyList<NetworkFinding> Findings, int ReferenceCount, IReadOnlyList<string> PipeProblems);

/// <summary>
/// The static network policy check (NET-010…016): reads assemblies as metadata (nothing is loaded or run), lists every
/// reference to a connection-capable target and compares the result with the allow-list table of NETWORK-POLICY.md.
/// </summary>
internal static partial class NetworkPolicy
{
    private const string PipeClient = "System.IO.Pipes.NamedPipeClientStream";
    private const string PipeOnly = "pipe-local";
    private const string LocalOnly = "always (local only)";

    private static readonly string[] Kinds = ["dns", "http", "mail", "native", "pipe-local", "socket", "tls", "websocket"];
    private static readonly string[] Defaults = ["off", "none configured", LocalOnly];
    private static readonly string[] Columns = ["Feature", "Card", "Assembly", "Type", "Kind", "Default", "Shown and removed in"];
    private static readonly string[] Sections = ["What FreexComp never does", "Features that can open a connection", "Call sites"];

    private static readonly HashSet<string> HttpTypes = new(StringComparer.Ordinal)
    {
        "WebRequest", "HttpWebRequest", "FtpWebRequest", "WebClient", "HttpListener", "ServicePointManager",
    };

    private static readonly HashSet<string> NativeModules = new(StringComparer.OrdinalIgnoreCase)
    {
        "ws2_32", "wsock32", "mswsock", "winhttp", "wininet", "urlmon", "dnsapi", "httpapi", "webservices", "iphlpapi",
        "rasapi32", "netapi32", "mpr", "winrtc", "bits",
    };

    [GeneratedRegex(@"^(F\d\d-\d\d\d|Phase \d+)$")]
    private static partial Regex CardPattern();

    // ---------------------------------------------------------------------------------------------------------------
    // The check (NET-012, NET-013)

    public static IReadOnlyList<string> Check(IReadOnlyList<string> assemblyPaths, string allowListMarkdown)
    {
        var problems = new List<string>();
        var rows = ParseAllowList(allowListMarkdown, problems);
        var scans = assemblyPaths.Select(Scan).ToList();
        var findings = scans.SelectMany(s => s.Findings).ToList();

        var sites = findings.GroupBy(f => (f.Assembly, f.Type, f.Kind))
            .OrderBy(g => g.Key.Assembly, StringComparer.Ordinal).ThenBy(g => g.Key.Type, StringComparer.Ordinal).ThenBy(g => g.Key.Kind, StringComparer.Ordinal);
        foreach (var site in sites)
        {
            var listed = rows.Any(r => r.Assembly == site.Key.Assembly && r.Type == site.Key.Type && r.Kinds.Contains(site.Key.Kind));
            if (!listed)
                problems.Add($"unlisted endpoint: {site.Key.Assembly}: {site.Key.Type} uses {site.Select(f => f.Target).Order(StringComparer.Ordinal).First()} ({site.Key.Kind})");
        }

        foreach (var row in rows)
        {
            foreach (var kind in row.Kinds.Where(k => !findings.Any(f => f.Assembly == row.Assembly && f.Type == row.Type && f.Kind == k)))
                problems.Add($"stale allow-list row: {row.Assembly} / {row.Type} ({kind})");
        }

        problems.AddRange(scans.SelectMany(s => s.PipeProblems));
        return problems;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // The allow-list file (NET-001…004)

    private sealed record Row(string Assembly, string Type, IReadOnlyList<string> Kinds);

    private static List<Row> ParseAllowList(string markdown, List<string> problems)
    {
        void Format(string message) => problems.Add("allow-list format: " + message);
        var lines = markdown.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        var rows = new List<Row>();

        if (lines.FirstOrDefault(l => l.Length > 0) is not { } title || !title.StartsWith("# ", StringComparison.Ordinal))
            Format("the file must start with a title line '# …'");
        var headings = lines.Where(l => l.StartsWith("## ", StringComparison.Ordinal)).Select(l => l[3..]).ToList();
        if (!headings.SequenceEqual(Sections))
        {
            Format("the sections must be " + string.Join(", ", Sections.Select(s => $"'## {s}'")) + " in this order, found " +
                (headings.Count == 0 ? "none" : string.Join(", ", headings.Select(s => $"'## {s}'"))));
            return rows;
        }

        List<string> Section(string name)
        {
            var start = lines.IndexOf("## " + name) + 1;
            var end = lines.FindIndex(start, l => l.StartsWith("## ", StringComparison.Ordinal));
            return lines.GetRange(start, (end < 0 ? lines.Count : end) - start);
        }

        static bool IsTableLine(string line) => line.StartsWith('|');
        if (lines.Count(IsTableLine) != Section(Sections[2]).Count(IsTableLine))
            Format($"only the section '## {Sections[2]}' may hold a table");
        var bullets = Section(Sections[1]).Where(l => l.StartsWith("- ", StringComparison.Ordinal)).Select(l => l[2..]).ToList();

        // exactly one table: a block of consecutive table lines
        var callSites = Section(Sections[2]);
        var table = callSites.SkipWhile(l => !IsTableLine(l)).TakeWhile(IsTableLine).ToList();
        if (table.Count < 2 || callSites.Count(IsTableLine) != table.Count)
        {
            Format($"the section '## {Sections[2]}' must hold exactly one table");
            return rows;
        }

        static string[] Cells(string line) => [.. line.Trim().Trim('|').Split('|').Select(c => c.Trim())];
        if (!Cells(table[0]).SequenceEqual(Columns) || !table[0].Trim().EndsWith('|') || !Cells(table[1]).All(c => c.Length >= 3 && c.All(ch => ch == '-')) || Cells(table[1]).Length != Columns.Length)
        {
            Format("the table columns must be | " + string.Join(" | ", Columns) + " | followed by the separator line");
            return rows;
        }

        Row? previous = null;
        foreach (var line in table.Skip(2))
        {
            void Bad(string message) => Format($"{message}: {line}");
            var cells = Cells(line);
            if (cells.Length != Columns.Length || !line.TrimEnd().EndsWith('|'))
            {
                Bad($"a row must have {Columns.Length} cells");
                continue;
            }

            if (cells.Any(c => c.Length == 0))
            {
                Bad("empty cell");
                continue;
            }

            var (feature, card, assembly, type, kindCell, @default, shown) = (cells[0], cells[1], cells[2], cells[3], cells[4], cells[5], cells[6]);
            var kinds = kindCell.Split(',').Select(k => k.Trim()).ToList();
            foreach (var unknown in kinds.Where(k => !Kinds.Contains(k)))
                Bad($"unknown kind '{unknown}'");
            if (!kinds.SequenceEqual(kinds.Distinct().Order(StringComparer.Ordinal)))
                Bad("the kinds must be distinct and in alphabetical order");
            var pipeOnly = kinds is [PipeOnly];
            if (!CardPattern().IsMatch(card))
                Bad($"unknown card '{card}' (a card id such as F02-006, or 'Phase <n>')");
            if (!Defaults.Contains(@default))
                Bad($"unknown default '{@default}' (one of: {string.Join(", ", Defaults)})");
            else if (@default == LocalOnly && !pipeOnly)
                Bad($"the default '{LocalOnly}' is allowed only for a row whose kind is exactly {PipeOnly}");
            if (shown == "—" && !pipeOnly)
                Bad($"only a {PipeOnly} row may leave '{Columns[6]}' as '—'");

            var row = new Row(assembly, type, kinds);
            if (rows.Any(r => r.Assembly == assembly && r.Type == type))
                Bad($"duplicate row for {assembly} / {type}");
            else if (previous is not null && Compare(previous, row) > 0)
                Bad("rows must be in ordinal order by (Assembly, Type)");
            if (!pipeOnly && !bullets.Any(b => b.StartsWith(feature, StringComparison.Ordinal)))
                problems.Add($"missing feature bullet: {feature}");
            rows.Add(row);
            previous = row;
        }

        return rows;

        static int Compare(Row a, Row b) =>
            string.CompareOrdinal(a.Assembly, b.Assembly) is var c and not 0 ? c : string.CompareOrdinal(a.Type, b.Type);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Targets and kinds (§3.1)

    /// <summary>The kind of a referenced type (outermost type: namespace and name), or null when it is not a target.</summary>
    private static string? KindOf(string @namespace, string name) => @namespace switch
    {
        "System.Net.Http" => "http",
        _ when @namespace.StartsWith("System.Net.Http.", StringComparison.Ordinal) => "http",
        "System.Net" when HttpTypes.Contains(name) => "http",
        "System.Net" when name == "Dns" => "dns",
        "System.Net.Sockets" or "System.Net.Quic" or "System.Net.NetworkInformation" => "socket",
        "System.Net.WebSockets" => "websocket",
        "System.Net.Mail" => "mail",
        "System.Net.Security" => "tls",
        "System.IO.Pipes" when name == "NamedPipeClientStream" => PipeOnly,
        _ => null,
    };

    // ---------------------------------------------------------------------------------------------------------------
    // The scanner (NET-011, NET-016)

    private static readonly Dictionary<ushort, OpCode> OpCodeTable = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(f => f.FieldType == typeof(OpCode))
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => unchecked((ushort)o.Value));

    private readonly record struct Instruction(OpCode Code, int Token);

    public static AssemblyScan Scan(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        return new Scanner(pe, md).Run();
    }

    /// <summary>The types named by a signature: the type itself and every generic argument, element type and modifier.</summary>
    private sealed class Scanner(PEReader pe, MetadataReader md) : ISignatureTypeProvider<ImmutableArray<EntityHandle>, object?>
    {
        private readonly string _assembly = md.GetString(md.GetAssemblyDefinition().Name);
        private readonly HashSet<NetworkFinding> _findings = [];
        private readonly SortedSet<string> _pipeProblems = new(StringComparer.Ordinal);
        private int _references;
        private string _user = "";

        public AssemblyScan Run()
        {
            _user = "<assembly>";
            Attributes(md.GetAssemblyDefinition().GetCustomAttributes());
            Attributes(md.GetModuleDefinition().GetCustomAttributes());

            foreach (var handle in md.TypeDefinitions)
            {
                var type = md.GetTypeDefinition(handle);
                _user = OutermostName(handle);
                Attributes(type.GetCustomAttributes());
                Use(type.BaseType);
                foreach (var implementation in type.GetInterfaceImplementations())
                {
                    Use(md.GetInterfaceImplementation(implementation).Interface);
                    Attributes(md.GetInterfaceImplementation(implementation).GetCustomAttributes());
                }

                GenericParameters(type.GetGenericParameters());
                foreach (var fieldHandle in type.GetFields())
                {
                    var field = md.GetFieldDefinition(fieldHandle);
                    Use(field.DecodeSignature(this, null));
                    Attributes(field.GetCustomAttributes());
                }

                foreach (var propertyHandle in type.GetProperties())
                {
                    var property = md.GetPropertyDefinition(propertyHandle);
                    Use(property.DecodeSignature(this, null));
                    Attributes(property.GetCustomAttributes());
                }

                foreach (var eventHandle in type.GetEvents())
                {
                    var @event = md.GetEventDefinition(eventHandle);
                    Use(@event.Type);
                    Attributes(@event.GetCustomAttributes());
                }

                foreach (var methodHandle in type.GetMethods())
                    Method(md.GetMethodDefinition(methodHandle));
            }

            return new AssemblyScan(
                _assembly,
                [.. _findings.OrderBy(f => f.Type, StringComparer.Ordinal).ThenBy(f => f.Kind, StringComparer.Ordinal).ThenBy(f => f.Target, StringComparer.Ordinal)],
                _references,
                [.. _pipeProblems]);
        }

        private void Method(MethodDefinition method)
        {
            Use(method.DecodeSignature(this, null));
            Attributes(method.GetCustomAttributes());
            GenericParameters(method.GetGenericParameters());
            foreach (var parameter in method.GetParameters())
                Attributes(md.GetParameter(parameter).GetCustomAttributes());

            // a platform import (ImplMap table)
            var import = method.GetImport();
            if (!import.Module.IsNil)
            {
                _references++;
                var module = md.GetString(md.GetModuleReference(import.Module).Name);
                var bare = module.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? module[..^4] : module;
                if (NativeModules.Contains(bare))
                    _findings.Add(new NetworkFinding(_assembly, _user, "native", $"{bare.ToLowerInvariant()}!{md.GetString(import.Name.IsNil ? method.Name : import.Name)}"));
            }

            if (method.RelativeVirtualAddress == 0)
                return;
            var body = pe.GetMethodBody(method.RelativeVirtualAddress);
            if (!body.LocalSignature.IsNil)
            {
                foreach (var local in md.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(this, null))
                    Use(local);
            }

            foreach (var region in body.ExceptionRegions.Where(r => r.Kind == ExceptionRegionKind.Catch))
                Use(region.CatchType);

            var instructions = Instructions(body.GetILReader());
            for (var i = 0; i < instructions.Count; i++)
            {
                var (code, token) = instructions[i];
                switch (code.OperandType)
                {
                    case OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType:
                        Member(MetadataTokens.EntityHandle(token));
                        break;
                    case OperandType.InlineSig:
                        Use(md.GetStandaloneSignature((StandaloneSignatureHandle)MetadataTokens.EntityHandle(token)).DecodeMethodSignature(this, null));
                        break;
                }

                if (code == OpCodes.Newobj && IsPipeClientConstructor(MetadataTokens.EntityHandle(token)) && !ServerNameIsLocalLiteral(instructions, i))
                    _pipeProblems.Add($"pipe client with a non-literal server name: {_user}");
            }
        }

        // ----- references

        private void Attributes(CustomAttributeHandleCollection attributes)
        {
            foreach (var handle in attributes)
                Member(md.GetCustomAttribute(handle).Constructor);
        }

        private void GenericParameters(GenericParameterHandleCollection parameters)
        {
            foreach (var handle in parameters)
            {
                var parameter = md.GetGenericParameter(handle);
                Attributes(parameter.GetCustomAttributes());
                foreach (var constraint in parameter.GetConstraints())
                    Use(md.GetGenericParameterConstraint(constraint).Type);
            }
        }

        /// <summary>A member or type token of an instruction or attribute: its declaring type and every type of its signature.</summary>
        private void Member(EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification:
                    Use(handle);
                    break;
                case HandleKind.MemberReference:
                    var member = md.GetMemberReference((MemberReferenceHandle)handle);
                    if (member.Parent.Kind == HandleKind.MethodDefinition)
                        _references++;
                    else
                        Use(member.Parent);
                    if (member.GetKind() == MemberReferenceKind.Method)
                        Use(member.DecodeMethodSignature(this, null));
                    else
                        Use(member.DecodeFieldSignature(this, null));
                    break;
                case HandleKind.MethodSpecification:
                    var specification = md.GetMethodSpecification((MethodSpecificationHandle)handle);
                    Member(specification.Method);
                    foreach (var argument in specification.DecodeSignature(this, null))
                        Use(argument);
                    break;
                case HandleKind.MethodDefinition or HandleKind.FieldDefinition:
                    _references++; // a member of this assembly: its own signature is read where it is defined
                    break;
            }
        }

        private void Use(MethodSignature<ImmutableArray<EntityHandle>> signature)
        {
            Use(signature.ReturnType);
            foreach (var parameter in signature.ParameterTypes)
                Use(parameter);
        }

        private void Use(ImmutableArray<EntityHandle> types)
        {
            foreach (var type in types)
                Use(type);
        }

        private void Use(EntityHandle handle)
        {
            if (handle.IsNil)
                return;
            switch (handle.Kind)
            {
                case HandleKind.TypeSpecification:
                    Use(md.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(this, null));
                    break;
                case HandleKind.TypeReference:
                    _references++;
                    var reference = md.GetTypeReference((TypeReferenceHandle)handle);
                    var full = md.GetString(reference.Name);
                    while (reference.ResolutionScope.Kind == HandleKind.TypeReference)
                    {
                        reference = md.GetTypeReference((TypeReferenceHandle)reference.ResolutionScope);
                        full = md.GetString(reference.Name) + "+" + full;
                    }

                    var @namespace = md.GetString(reference.Namespace);
                    if (KindOf(@namespace, md.GetString(reference.Name)) is { } kind)
                        _findings.Add(new NetworkFinding(_assembly, _user, kind, @namespace + "." + full));
                    break;
                case HandleKind.TypeDefinition:
                    _references++; // a type of this assembly is never a target
                    break;
            }
        }

        private string OutermostName(TypeDefinitionHandle handle)
        {
            var type = md.GetTypeDefinition(handle);
            while (type.IsNested)
                type = md.GetTypeDefinition(type.GetDeclaringType());
            var (@namespace, name) = (md.GetString(type.Namespace), md.GetString(type.Name));
            return @namespace.Length == 0 ? name : @namespace + "." + name;
        }

        // ----- instructions

        private static List<Instruction> Instructions(BlobReader il)
        {
            var result = new List<Instruction>();
            while (il.RemainingBytes > 0)
            {
                ushort value = il.ReadByte();
                if (value == 0xFE)
                    value = (ushort)(0xFE00 | il.ReadByte());
                var code = OpCodeTable[value];
                var token = 0;
                switch (code.OperandType)
                {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar:
                        il.Offset += 1;
                        break;
                    case OperandType.InlineVar:
                        il.Offset += 2;
                        break;
                    case OperandType.InlineI8 or OperandType.InlineR:
                        il.Offset += 8;
                        break;
                    case OperandType.InlineSwitch:
                        var targets = il.ReadInt32(); // read first: the count itself moves the offset
                        il.Offset += 4 * targets;
                        break;
                    case OperandType.InlineField or OperandType.InlineMethod or OperandType.InlineTok or OperandType.InlineType
                        or OperandType.InlineSig or OperandType.InlineString:
                        token = il.ReadInt32();
                        break;
                    default: // InlineBrTarget, InlineI, ShortInlineR
                        il.Offset += 4;
                        break;
                }

                result.Add(new Instruction(code, token));
            }

            return result;
        }

        private bool IsPipeClientConstructor(EntityHandle handle)
        {
            if (handle.Kind != HandleKind.MemberReference)
                return false;
            var member = md.GetMemberReference((MemberReferenceHandle)handle);
            return md.GetString(member.Name) == ".ctor" && member.Parent.Kind == HandleKind.TypeReference
                && TypeName((TypeReferenceHandle)member.Parent) == PipeClient;
        }

        private string TypeName(TypeReferenceHandle handle)
        {
            var reference = md.GetTypeReference(handle);
            return md.GetString(reference.Namespace) + "." + md.GetString(reference.Name);
        }

        /// <summary>
        /// NET-016: the constructor called at <paramref name="index"/> takes (server name, pipe name, …) and the instruction
        /// that loads its first argument is <c>ldstr "."</c>. The arguments are found by walking back over the
        /// straight-line code that loads them; anything else (a branch among the arguments, a constructor without a
        /// server name) is not accepted.
        /// </summary>
        private bool ServerNameIsLocalLiteral(List<Instruction> instructions, int index)
        {
            var signature = md.GetMemberReference((MemberReferenceHandle)MetadataTokens.EntityHandle(instructions[index].Token))
                .DecodeMethodSignature(new PrimitiveNames(), null);
            if (signature.ParameterTypes.Length < 2 || signature.ParameterTypes[0] != "String" || signature.ParameterTypes[1] != "String")
                return false;

            var needed = signature.ParameterTypes.Length;
            for (var i = index - 1; i >= 0; i--)
            {
                var (code, token) = instructions[i];
                if (code.FlowControl is not (FlowControl.Next or FlowControl.Call or FlowControl.Meta) || StackEffect(code, token) is not { } effect)
                    return false;
                var (pops, pushes) = effect;
                if (pushes >= needed)
                {
                    return needed == 1 && pops == 0 && code == OpCodes.Ldstr
                        && md.GetUserString(MetadataTokens.UserStringHandle(token & 0x00FFFFFF)) == ".";
                }

                needed += pops - pushes;
            }

            return false;
        }

        /// <summary>Values an instruction takes from and puts on the evaluation stack; null when it cannot be told.</summary>
        private (int Pops, int Pushes)? StackEffect(OpCode code, int token)
        {
            int? pops = code.StackBehaviourPop switch
            {
                StackBehaviour.Pop0 => 0,
                StackBehaviour.Pop1 or StackBehaviour.Popi or StackBehaviour.Popref => 1,
                StackBehaviour.Pop1_pop1 or StackBehaviour.Popi_pop1 or StackBehaviour.Popi_popi or StackBehaviour.Popi_popi8
                    or StackBehaviour.Popi_popr4 or StackBehaviour.Popi_popr8 or StackBehaviour.Popref_pop1 or StackBehaviour.Popref_popi => 2,
                StackBehaviour.Popi_popi_popi or StackBehaviour.Popref_popi_popi or StackBehaviour.Popref_popi_popi8
                    or StackBehaviour.Popref_popi_popr4 or StackBehaviour.Popref_popi_popr8 or StackBehaviour.Popref_popi_popref
                    or StackBehaviour.Popref_popi_pop1 => 3,
                _ => null,
            };
            int? pushes = code.StackBehaviourPush switch
            {
                StackBehaviour.Push0 => 0,
                StackBehaviour.Push1 or StackBehaviour.Pushi or StackBehaviour.Pushi8 or StackBehaviour.Pushr4 or StackBehaviour.Pushr8
                    or StackBehaviour.Pushref => 1,
                StackBehaviour.Push1_push1 => 2,
                _ => null,
            };
            if (pops is not null && pushes is not null)
                return (pops.Value, pushes.Value);
            if (code != OpCodes.Call && code != OpCodes.Callvirt && code != OpCodes.Newobj)
                return null; // calli, ret

            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind == HandleKind.MethodSpecification)
                handle = md.GetMethodSpecification((MethodSpecificationHandle)handle).Method;
            MethodSignature<string> signature;
            switch (handle.Kind)
            {
                case HandleKind.MemberReference:
                    signature = md.GetMemberReference((MemberReferenceHandle)handle).DecodeMethodSignature(new PrimitiveNames(), null);
                    break;
                case HandleKind.MethodDefinition:
                    signature = md.GetMethodDefinition((MethodDefinitionHandle)handle).DecodeSignature(new PrimitiveNames(), null);
                    break;
                default:
                    return null;
            }

            var isNew = code == OpCodes.Newobj;
            return (signature.ParameterTypes.Length + (signature.Header.IsInstance && !isNew ? 1 : 0), isNew || signature.ReturnType != "Void" ? 1 : 0);
        }

        // ----- ISignatureTypeProvider: a signature is the list of the type handles it names

        public ImmutableArray<EntityHandle> GetArrayType(ImmutableArray<EntityHandle> elementType, ArrayShape shape) => elementType;
        public ImmutableArray<EntityHandle> GetByReferenceType(ImmutableArray<EntityHandle> elementType) => elementType;
        public ImmutableArray<EntityHandle> GetFunctionPointerType(MethodSignature<ImmutableArray<EntityHandle>> signature) =>
            [.. signature.ReturnType, .. signature.ParameterTypes.SelectMany(p => p)];
        public ImmutableArray<EntityHandle> GetGenericInstantiation(ImmutableArray<EntityHandle> genericType, ImmutableArray<ImmutableArray<EntityHandle>> typeArguments) =>
            [.. genericType, .. typeArguments.SelectMany(a => a)];
        public ImmutableArray<EntityHandle> GetGenericMethodParameter(object? genericContext, int index) => [];
        public ImmutableArray<EntityHandle> GetGenericTypeParameter(object? genericContext, int index) => [];
        public ImmutableArray<EntityHandle> GetModifiedType(ImmutableArray<EntityHandle> modifier, ImmutableArray<EntityHandle> unmodifiedType, bool isRequired) =>
            [.. modifier, .. unmodifiedType];
        public ImmutableArray<EntityHandle> GetPinnedType(ImmutableArray<EntityHandle> elementType) => elementType;
        public ImmutableArray<EntityHandle> GetPointerType(ImmutableArray<EntityHandle> elementType) => elementType;
        public ImmutableArray<EntityHandle> GetPrimitiveType(PrimitiveTypeCode typeCode) => [];
        public ImmutableArray<EntityHandle> GetSZArrayType(ImmutableArray<EntityHandle> elementType) => elementType;
        public ImmutableArray<EntityHandle> GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => [handle];
        public ImmutableArray<EntityHandle> GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => [handle];
        public ImmutableArray<EntityHandle> GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => [handle];
    }

    /// <summary>Signature decoder for the stack walk: only the primitive names "String" and "Void" matter; every other type is "?".</summary>
    private sealed class PrimitiveNames : ISignatureTypeProvider<string, object?>
    {
        public string GetArrayType(string elementType, ArrayShape shape) => "?";
        public string GetByReferenceType(string elementType) => "?";
        public string GetFunctionPointerType(MethodSignature<string> signature) => "?";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) => "?";
        public string GetGenericMethodParameter(object? genericContext, int index) => "?";
        public string GetGenericTypeParameter(object? genericContext, int index) => "?";
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => "?";
        public string GetPointerType(string elementType) => "?";
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => typeCode.ToString();
        public string GetSZArrayType(string elementType) => "?";
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => "?";
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => "?";
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) => "?";
    }
}
