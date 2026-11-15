using System.Reflection;
using System.Text;

namespace FreexComp.Tests.Shared;

/// <summary>
/// testing.md §3 <c>ApiSurfaceTests</c> (C-150): a deterministic text listing of an assembly's public surface (public and
/// protected members of exported types), compared with the checked-in <c>PublicAPI.txt</c> next to the project. Set
/// <c>UPDATE_PUBLICAPI=1</c> to rewrite the file, then review the diff: every removed or changed line is a breaking change
/// (api.md CMP-240), added lines are additive.
/// </summary>
internal static class PublicApiListing
{
    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    /// <summary>Compares (or, with <c>UPDATE_PUBLICAPI=1</c>, writes) <paramref name="relativePath"/> under the repository root.</summary>
    public static void AssertMatches(Assembly assembly, string relativePath)
    {
        var path = Path.Combine(RepositoryRoot(), relativePath);
        var actual = Generate(assembly);
        if (Environment.GetEnvironmentVariable("UPDATE_PUBLICAPI") == "1")
        {
            File.WriteAllText(path, actual, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            return;
        }

        Assert.True(File.Exists(path), $"{relativePath} is missing; generate it with UPDATE_PUBLICAPI=1 and review it.");
        var expected = File.ReadAllText(path).ReplaceLineEndings("\n");
        if (expected == actual)
        {
            return;
        }

        var expectedList = expected.Split('\n');
        var actualList = actual.Split('\n');
        var at = Enumerable.Range(0, Math.Min(expectedList.Length, actualList.Length)).FirstOrDefault(i => expectedList[i] != actualList[i], -1);
        var first = at < 0
            ? $"line counts {expectedList.Length} / {actualList.Length}"
            : $"first difference at line {at + 1}: expected '{expectedList[at]}', actual '{actualList[at]}'";
        var expectedLines = expectedList.ToHashSet(StringComparer.Ordinal);
        var actualLines = actualList.ToHashSet(StringComparer.Ordinal);
        var removed = expectedLines.Except(actualLines).Take(20).Select(l => "- " + l);
        var added = actualLines.Except(expectedLines).Take(20).Select(l => "+ " + l);
        Assert.Fail($"The public API of {assembly.GetName().Name} differs from {relativePath} (UPDATE_PUBLICAPI=1 regenerates it), {first}:\n"
                    + string.Join("\n", removed.Concat(added)));
    }

    /// <summary>One block per exported type (ordinal by full name): the type line, then its member lines (ordinal), LF, final newline.</summary>
    public static string Generate(Assembly assembly)
    {
        var builder = new StringBuilder();
        foreach (var type in assembly.GetExportedTypes().OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            builder.Append(TypeLine(type)).Append('\n');
            foreach (var member in Members(type).Order(StringComparer.Ordinal))
            {
                builder.Append("  ").Append(member).Append('\n');
            }
        }

        return builder.ToString();
    }

    private static string TypeLine(Type type)
    {
        string kind;
        if (type.IsEnum)
        {
            kind = "enum";
        }
        else if (type.IsInterface)
        {
            kind = "interface";
        }
        else if (typeof(Delegate).IsAssignableFrom(type.BaseType))
        {
            kind = "delegate";
        }
        else if (type.IsValueType)
        {
            kind = (IsReadOnly(type) ? "readonly " : "") + (IsRecord(type) ? "record struct" : "struct");
        }
        else
        {
            kind = (type.IsAbstract && type.IsSealed ? "static " : type.IsAbstract ? "abstract " : type.IsSealed ? "sealed " : "")
                   + (IsRecord(type) ? "record" : "class");
        }

        var line = kind + " " + Name(type, qualified: true);
        var bases = new List<string>();
        if (type.BaseType is { } baseType && baseType != typeof(object) && baseType != typeof(ValueType) && baseType != typeof(Enum)
            && baseType != typeof(MulticastDelegate))
        {
            bases.Add(Name(baseType, qualified: true));
        }

        if (!type.IsEnum && !typeof(Delegate).IsAssignableFrom(type.BaseType))
        {
            bases.AddRange(type.GetInterfaces().Where(i => i.IsPublic || i.IsNestedPublic).Select(i => Name(i, qualified: true))
                .Order(StringComparer.Ordinal));
        }

        return bases.Count == 0 ? line : line + " : " + string.Join(", ", bases);
    }

    private static IEnumerable<string> Members(Type type)
    {
        if (type.IsEnum)
        {
            foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                yield return $"{field.Name} = {Convert.ToInt64(field.GetRawConstantValue(), System.Globalization.CultureInfo.InvariantCulture)}";
            }

            yield break;
        }

        foreach (var member in type.GetMembers(Declared))
        {
            var line = member switch
            {
                ConstructorInfo c when Visible(c) => Access(c) + Static(c) + "ctor(" + Parameters(c.GetParameters()) + ")",
                MethodInfo m when Visible(m) && !IsAccessor(m) => Access(m) + Modifiers(m) + Name(m.ReturnType, true) + " " + m.Name
                    + (m.IsGenericMethodDefinition ? "<" + string.Join(", ", m.GetGenericArguments().Select(a => a.Name)) + ">" : "")
                    + "(" + Parameters(m.GetParameters()) + ")",
                PropertyInfo p when Accessors(p).Any(Visible) => Access(Accessors(p).Where(Visible).First())
                    + Modifiers(Accessors(p).First()) + Name(p.PropertyType, true) + " "
                    + (p.GetIndexParameters() is { Length: > 0 } index ? "this[" + Parameters(index) + "]" : p.Name)
                    + " {" + (p.GetMethod is { } g && Visible(g) ? " get;" : "")
                    + (p.SetMethod is { } s && Visible(s) ? (IsInitOnly(s) ? " init;" : " set;") : "") + " }",
                FieldInfo f when f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly => Access(f) + (f.IsLiteral ? "const " : (f.IsStatic ? "static " : "")
                    + (f.IsInitOnly ? "readonly " : "")) + Name(f.FieldType, true) + " " + f.Name
                    + (f.IsLiteral ? " = " + Literal(f.GetRawConstantValue()) : ""),
                EventInfo e when e.AddMethod is { } add && Visible(add) => Access(add) + Modifiers(add) + "event " + Name(e.EventHandlerType!, true) + " " + e.Name,
                _ => null,                                             // nested types are listed as types of their own
            };
            if (line is not null)
            {
                yield return line;
            }
        }
    }

    private static IEnumerable<MethodInfo> Accessors(PropertyInfo p) => new[] { p.GetMethod, p.SetMethod }.OfType<MethodInfo>();

    private static bool Visible(MethodBase m) => m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly;

    private static bool IsAccessor(MethodInfo m) =>
        m.IsSpecialName && (m.Name.StartsWith("get_", StringComparison.Ordinal) || m.Name.StartsWith("set_", StringComparison.Ordinal)
                            || m.Name.StartsWith("add_", StringComparison.Ordinal) || m.Name.StartsWith("remove_", StringComparison.Ordinal));

    private static string Access(MethodBase m) => m.IsPublic ? "public " : "protected ";

    private static string Access(FieldInfo f) => f.IsPublic ? "public " : "protected ";

    private static string Static(MethodBase m) => m.IsStatic ? "static " : "";

    private static string Modifiers(MethodInfo m)
    {
        if (m.IsStatic)
        {
            return "static ";
        }

        if (m.DeclaringType!.IsInterface)
        {
            return m.IsAbstract ? "" : "default ";
        }

        if (m.IsAbstract)
        {
            return "abstract ";
        }

        if (m.GetBaseDefinition().DeclaringType != m.DeclaringType)
        {
            return m.IsFinal ? "sealed override " : "override ";
        }

        return m.IsVirtual && !m.IsFinal ? "virtual " : "";
    }

    private static string Parameters(ParameterInfo[] parameters) => string.Join(", ", parameters.Select(p =>
    {
        var type = p.ParameterType;
        var prefix = "";
        if (type.IsByRef)
        {
            prefix = p.IsOut ? "out " : p.IsIn ? "in " : "ref ";
            type = type.GetElementType()!;
        }

        if (p.IsDefined(typeof(ParamArrayAttribute)))
        {
            prefix = "params ";
        }

        var text = prefix + Name(type, true) + " " + p.Name;
        return !p.HasDefaultValue ? text
            : text + " = " + (p.RawDefaultValue is null && type.IsValueType && Nullable.GetUnderlyingType(type) is null ? "default" : Literal(p.RawDefaultValue));
    }));

    private static string Literal(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal) + "\"",
        bool b => b ? "true" : "false",
        char c => "'" + c + "'",
        IFormattable f => f.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static string Name(Type type, bool qualified)
    {
        if (type.IsByRef)
        {
            return Name(type.GetElementType()!, qualified) + "&";
        }

        if (type.IsArray)
        {
            return Name(type.GetElementType()!, qualified) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if (type.IsPointer)
        {
            return Name(type.GetElementType()!, qualified) + "*";
        }

        if (type.IsGenericParameter)
        {
            return type.Name;
        }

        var name = type.Name;
        var tick = name.IndexOf('`', StringComparison.Ordinal);
        if (tick >= 0)
        {
            name = name[..tick];
        }

        var prefix = type.IsNested ? Name(type.DeclaringType!, qualified) + "." : qualified && type.Namespace is { } ns ? ns + "." : "";
        if (!type.IsGenericType)
        {
            return prefix + name;
        }

        // only the arguments that belong to this type (not to the declaring type of a nested generic)
        var all = type.GetGenericArguments();
        var own = type.IsNested ? all.Skip(type.DeclaringType!.GetGenericArguments().Length).ToArray() : all;
        return own.Length == 0 ? prefix + name : prefix + name + "<" + string.Join(", ", own.Select(a => Name(a, true))) + ">";
    }

    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null
        || (type.IsValueType && type.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance) is not null);

    private static bool IsReadOnly(Type type) =>
        type.GetCustomAttributesData().Any(a => a.AttributeType.FullName == "System.Runtime.CompilerServices.IsReadOnlyAttribute");

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter.GetRequiredCustomModifiers().Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");

    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "FreexComp.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("FreexComp.sln not found above " + AppContext.BaseDirectory);
    }
}
