using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace FreexComp.Ide.TestSupport;

/// <summary>One type or member an assembly's code uses: who uses it, and what (declaring type full name + member name).</summary>
/// <param name="UserNamespace">Namespace of the outermost type containing the use (closures and state machines count for their owner).</param>
/// <param name="UserType">Full name of the type containing the use.</param>
/// <param name="TargetType">Full name of the used type (generic arity suffix kept, type arguments dropped), e.g. <c>System.Threading.Tasks.Task`1</c>.</param>
/// <param name="Member">The used member, or null when the type itself is used (signature, base type, <c>ldtoken</c>, …).</param>
public sealed record IlReference(string UserNamespace, string UserType, string TargetType, string? Member);

/// <summary>
/// Architecture-test helper (IDE-ARCH-081, IDE-ARCH-093): reads an assembly's metadata without loading it and lists
/// every type and member used by its method bodies, field and method signatures, base types and interfaces.
/// </summary>
public static class IlReferences
{
    public static IReadOnlyList<IlReference> Read(string assemblyPath)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var names = new TypeNames(md);
        var result = new List<IlReference>();
        foreach (var handle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(handle);
            var userType = names.Definition(handle);
            var userNamespace = OuterNamespace(md, handle);
            void Use(string target, string? member) => result.Add(new IlReference(userNamespace, userType, target, member));

            if (!type.BaseType.IsNil)
                Use(names.Of(type.BaseType), null);
            foreach (var implementation in type.GetInterfaceImplementations())
                Use(names.Of(md.GetInterfaceImplementation(implementation).Interface), null);
            foreach (var fieldHandle in type.GetFields())
                foreach (var used in names.InSignature(md.GetFieldDefinition(fieldHandle).DecodeSignature(names, null)))
                    Use(used, null);

            foreach (var methodHandle in type.GetMethods())
            {
                var method = md.GetMethodDefinition(methodHandle);
                var signature = method.DecodeSignature(names, null);
                foreach (var used in names.InSignature([signature.ReturnType, .. signature.ParameterTypes]))
                    Use(used, null);
                if (method.RelativeVirtualAddress == 0)
                    continue;
                foreach (var token in Tokens(pe.GetMethodBody(method.RelativeVirtualAddress).GetILReader()))
                {
                    if (names.Member(MetadataTokens.EntityHandle(token)) is { } used)
                        Use(used.Type, used.Member);
                }
            }
        }

        return result;
    }

    private static string OuterNamespace(MetadataReader md, TypeDefinitionHandle handle)
    {
        var type = md.GetTypeDefinition(handle);
        while (type.IsNested)
            type = md.GetTypeDefinition(type.GetDeclaringType());
        return md.GetString(type.Namespace);
    }

    /// <summary>The metadata-token operands of an IL stream (ECMA-335 III; one- and two-byte opcodes).</summary>
    private static IEnumerable<int> Tokens(BlobReader il)
    {
        var tokens = new List<int>();
        while (il.RemainingBytes > 0)
        {
            int code = il.ReadByte();
            if (code == 0xFE)
                code = 0xFE00 | il.ReadByte();
            switch (OperandSize(code))
            {
                case -1: // switch: count + targets
                    var count = il.ReadInt32();
                    il.Offset += 4 * count;
                    break;
                case -4: // a metadata token (user strings of ldstr are not entities)
                    var token = il.ReadInt32();
                    if ((token >>> 24) != 0x70)
                        tokens.Add(token);
                    break;
                case var size:
                    il.Offset += size;
                    break;
            }
        }

        return tokens;
    }

    /// <summary>Operand bytes; -4 = a metadata token, -1 = switch table.</summary>
    private static int OperandSize(int code) => code switch
    {
        0x45 => -1, // switch
        // ldfld … stsfld, call, callvirt, newobj, calli, jmp, castclass, isinst, box, unbox(.any), ldobj, stobj, cpobj,
        // newarr, ldelema, ldelem/stelem <T>, refanyval, mkrefany, ldtoken, ldstr, initobj, constrained., sizeof, ldftn, ldvirtftn
        0x7B or 0x7C or 0x7D or 0x7E or 0x7F or 0x80 or 0x28 or 0x6F or 0x73 or 0x29 or 0x27 or 0x74 or 0x75 or 0x8C
            or 0x79 or 0xA5 or 0x71 or 0x81 or 0x70 or 0x8D or 0x8F or 0xA3 or 0xA4 or 0xC2 or 0xC6 or 0xD0 or 0x72
            or 0xFE15 or 0xFE16 or 0xFE1C or 0xFE06 or 0xFE07 => -4,
        0x20 or 0x22 or 0x38 or 0x39 or 0x3A or 0x3B or 0x3C or 0x3D or 0x3E or 0x3F or 0x40 or 0x41 or 0x42 or 0x43 or 0x44
            or 0xDD => 4, // ldc.i4, ldc.r4, long branches, leave
        0x21 or 0x23 => 8, // ldc.i8, ldc.r8
        0x0E or 0x0F or 0x10 or 0x11 or 0x12 or 0x13 or 0x1F or 0x2B or 0x2C or 0x2D or 0x2E or 0x2F or 0x30 or 0x31 or 0x32
            or 0x33 or 0x34 or 0x35 or 0x36 or 0x37 or 0xDE or 0xFE12 or 0xFE19 => 1, // short forms, unaligned., no.
        0xFE09 or 0xFE0A or 0xFE0B or 0xFE0C or 0xFE0D or 0xFE0E => 2, // ldarg/ldarga/starg/ldloc/ldloca/stloc
        _ => 0,
    };

    /// <summary>Type names for handles and signatures (generic arguments are dropped: only the used type counts).</summary>
    private sealed class TypeNames(MetadataReader md) : ISignatureTypeProvider<string, object?>
    {
        private const string ArgumentsMarker = "\u0001";

        public string Definition(TypeDefinitionHandle handle)
        {
            var type = md.GetTypeDefinition(handle);
            var name = md.GetString(type.Name);
            return type.IsNested ? Definition(type.GetDeclaringType()) + "+" + name : Join(md.GetString(type.Namespace), name);
        }

        public string Reference(TypeReferenceHandle handle)
        {
            var type = md.GetTypeReference(handle);
            var name = md.GetString(type.Name);
            return type.ResolutionScope.Kind == HandleKind.TypeReference
                ? Reference((TypeReferenceHandle)type.ResolutionScope) + "+" + name
                : Join(md.GetString(type.Namespace), name);
        }

        public string Of(EntityHandle handle) => handle.Kind switch
        {
            HandleKind.TypeDefinition => Definition((TypeDefinitionHandle)handle),
            HandleKind.TypeReference => Reference((TypeReferenceHandle)handle),
            HandleKind.TypeSpecification => Strip(md.GetTypeSpecification((TypeSpecificationHandle)handle).DecodeSignature(this, null)),
            _ => "?",
        };

        /// <summary>The declaring type and member name of a token operand; types give a null member; strings nothing.</summary>
        public (string Type, string? Member)? Member(EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition or HandleKind.TypeReference or HandleKind.TypeSpecification:
                    return (Of(handle), null);
                case HandleKind.MethodDefinition:
                    var method = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                    return (Definition(method.GetDeclaringType()), md.GetString(method.Name));
                case HandleKind.FieldDefinition:
                    var field = md.GetFieldDefinition((FieldDefinitionHandle)handle);
                    return (Definition(field.GetDeclaringType()), md.GetString(field.Name));
                case HandleKind.MemberReference:
                    var member = md.GetMemberReference((MemberReferenceHandle)handle);
                    var parent = member.Parent.Kind == HandleKind.MethodDefinition
                        ? Definition(md.GetMethodDefinition((MethodDefinitionHandle)member.Parent).GetDeclaringType())
                        : Of(member.Parent);
                    return (parent, md.GetString(member.Name));
                case HandleKind.MethodSpecification:
                    return Member(md.GetMethodSpecification((MethodSpecificationHandle)handle).Method);
                default:
                    return null; // user strings, signatures
            }
        }

        /// <summary>Every type named in decoded signature strings (generic arguments are listed after the marker).</summary>
        public IEnumerable<string> InSignature(IEnumerable<string> types) =>
            types.SelectMany(t => t.Split(ArgumentsMarker, StringSplitOptions.RemoveEmptyEntries)).Select(Strip);

        public IEnumerable<string> InSignature(string type) => InSignature([type]);

        private static string Strip(string type) => type.Split(ArgumentsMarker)[0].TrimEnd('[', ']', '*', '&');

        private static string Join(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;

        public string GetArrayType(string elementType, ArrayShape shape) => elementType;
        public string GetByReferenceType(string elementType) => elementType;
        public string GetFunctionPointerType(MethodSignature<string> signature) => "fnptr";
        public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments) =>
            string.Join(ArgumentsMarker, [genericType, .. typeArguments]);
        public string GetGenericMethodParameter(object? genericContext, int index) => "!!" + index;
        public string GetGenericTypeParameter(object? genericContext, int index) => "!" + index;
        public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired) => unmodifiedType;
        public string GetPinnedType(string elementType) => elementType;
        public string GetPointerType(string elementType) => elementType;
        public string GetPrimitiveType(PrimitiveTypeCode typeCode) => "System." + typeCode;
        public string GetSZArrayType(string elementType) => elementType;
        public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => Definition(handle);
        public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) => Reference(handle);
        public string GetTypeFromSpecification(MetadataReader reader, object? genericContext, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, genericContext);
    }
}
