using System.Reflection;
using System.Runtime.CompilerServices;

namespace FreexComp.Debugger.Tests;

/// <summary>
/// The public surface equals the fixture copied from the debug-session interface document (sections 2 and 3): type
/// kinds, enum members in order, constructors, properties, methods with parameter names and defaults, events, nullability.
/// </summary>
public sealed class ApiShapeTests
{
    private static readonly string FixturePath =
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "api", "debug-session.api.txt");

    [Fact]
    public void ApiShape_MatchesInterfaceDocument()
    {
        var expected = File.ReadAllLines(FixturePath)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .ToList();
        var actual = Listing(Assembly.Load("FreexComp.Debugger"));

        var missing = expected.Except(actual, StringComparer.Ordinal).ToList();
        var extra = actual.Except(expected, StringComparer.Ordinal).ToList();

        Assert.True(missing.Count == 0 && extra.Count == 0,
            "Public API differs from the interface document.\nMissing (in document, not in assembly):\n  "
            + string.Join("\n  ", missing) + "\nExtra (in assembly, not in document):\n  " + string.Join("\n  ", extra));
        Assert.Equal(expected.Count, expected.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ApiDefaults_MatchInterfaceDocument()
    {
        var options = new DebugSessionOptions { AdapterPath = @"C:\x\adapter.exe" };
        Assert.Empty(options.AdapterArguments);
        Assert.Empty(options.UserSourceRoots);
        Assert.Equal(TimeSpan.FromSeconds(10), options.RequestTimeout);
        Assert.Equal(TimeSpan.FromSeconds(30), options.LaunchTimeout);
        Assert.Equal(new ShutdownWaits(), options.ShutdownWaits);
        Assert.Equal(TimeSpan.FromSeconds(3), options.ShutdownWaits.Disconnect);
        Assert.Equal(TimeSpan.FromSeconds(2), options.ShutdownWaits.AdapterExit);
        Assert.Equal(TimeSpan.FromSeconds(2), options.ShutdownWaits.StderrEnd);
        Assert.Null(options.Trace);
        Assert.Equal(new ValueFormatOptions(), options.Format);

        var format = new ValueFormatOptions();
        Assert.False(format.HexIntegers);
        Assert.Equal(1024, format.MaxStringChars);
        Assert.Equal(8, format.MaxSummaryElements);
        Assert.Equal(100, format.ChildPageSize);

        var launch = new LaunchRequest { Program = @"C:\x\p.exe" };
        Assert.Empty(launch.Arguments);
        Assert.Null(launch.WorkingDirectory);
        Assert.Null(launch.Environment);
        Assert.False(launch.StopOnEntry);
        Assert.False(launch.StopAtMainBody);
        Assert.Empty(launch.Breakpoints);
        Assert.Null(launch.TypeInfoPath);

        var exceptions = launch.Exceptions;
        Assert.True(exceptions.BreakOnLanguageExceptions);
        Assert.True(exceptions.BreakOnOsExceptions);
        Assert.Equal(["EAbort"], exceptions.IgnoredClasses);

        var spec = new BreakpointSpec { Id = 1, Line = 2 };
        Assert.True(spec.Enabled);
        Assert.Null(spec.Condition);
        Assert.Null(spec.HitCount);
        Assert.Null(spec.LogMessage);
    }

    [Fact]
    public void DebuggerException_CarriesCodeCommandAndInner()
    {
        var inner = new InvalidOperationException("x");
        var exception = new DebuggerException(DebugErrorCode.RequestFailed, "boom", "launch", inner);

        Assert.Equal(DebugErrorCode.RequestFailed, exception.Code);
        Assert.Equal("boom", exception.Message);
        Assert.Equal("launch", exception.DapCommand);
        Assert.Same(inner, exception.InnerException);
        Assert.Null(new DebuggerException(DebugErrorCode.InvalidState, "m").DapCommand);
    }

    // ---- listing ----

    private const BindingFlags Declared =
        BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    private static readonly string[] RecordMethods =
        ["Equals", "GetHashCode", "ToString", "Deconstruct", "<Clone>$", "PrintMembers", "op_Equality", "op_Inequality"];

    private static List<string> Listing(Assembly assembly)
    {
        var nullability = new NullabilityInfoContext();
        var lines = new List<string>();
        foreach (var type in assembly.GetExportedTypes())
        {
            Assert.Equal("FreexComp.Debugger", type.Namespace);
            Assert.False(type.IsNested, type.FullName);
            if (type.IsEnum)
            {
                lines.Add($"type {type.Name} : enum = {string.Join(", ", Enum.GetNames(type))}");
                continue;
            }

            var isRecord = IsRecord(type);
            lines.Add($"type {type.Name} : {Kind(type, isRecord)}");
            var positional = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ctor in type.GetConstructors(Declared))
            {
                var parameters = ctor.GetParameters();
                if (isRecord)
                {
                    foreach (var parameter in parameters)
                        positional.Add(parameter.Name!);
                }

                lines.Add($"{type.Name}..ctor({Parameters(parameters, nullability)})");
            }

            foreach (var property in type.GetProperties(Declared))
            {
                if (isRecord && (property.Name == "EqualityContract" || positional.Contains(property.Name)))
                    continue;
                var required = property.IsDefined(typeof(RequiredMemberAttribute)) ? "required " : "";
                var accessors = property.SetMethod is not { IsPublic: true } set
                    ? "{ get; }"
                    : set.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)) ? "{ get; init; }" : "{ get; set; }";
                lines.Add($"{type.Name}.{property.Name} : {required}{Render(property.PropertyType, nullability.Create(property))} {accessors}");
            }

            foreach (var method in type.GetMethods(Declared))
            {
                if (method.IsSpecialName && !method.Name.StartsWith("op_", StringComparison.Ordinal))
                    continue;
                if (isRecord && RecordMethods.Contains(method.Name))
                    continue;
                lines.Add($"{type.Name}.{method.Name}({Parameters(method.GetParameters(), nullability)}) : "
                    + Render(method.ReturnType, nullability.Create(method.ReturnParameter)));
            }

            foreach (var @event in type.GetEvents(Declared))
                lines.Add($"event {type.Name}.{@event.Name} : {Render(@event.EventHandlerType!, nullability.Create(@event))}");
            Assert.Empty(type.GetFields(Declared));
        }

        return lines;
    }

    private static bool IsRecord(Type type) =>
        type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance) is not null
        || (type.IsValueType && type.GetMethod("PrintMembers", BindingFlags.NonPublic | BindingFlags.Instance) is not null);

    private static string Kind(Type type, bool isRecord)
    {
        string kind;
        if (type.IsInterface)
            kind = "interface";
        else if (type.IsValueType)
            kind = (type.IsDefined(typeof(IsReadOnlyAttribute)) ? "readonly " : "") + (isRecord ? "record struct" : "struct");
        else
            kind = (type.IsSealed ? "sealed " : type.IsAbstract ? "abstract " : "") + (isRecord ? "record" : "class");

        var bases = new List<string>();
        if (type.BaseType is { } baseType && baseType != typeof(object) && baseType != typeof(ValueType))
            bases.Add(baseType.Name);
        bases.AddRange(type.GetInterfaces()
            .Where(i => !(i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEquatable<>)))
            .Where(i => type.BaseType is null || !type.BaseType.GetInterfaces().Contains(i))
            .Select(i => i.Name));
        return bases.Count == 0 ? kind : kind + ", " + string.Join(", ", bases);
    }

    private static string Parameters(ParameterInfo[] parameters, NullabilityInfoContext nullability) =>
        string.Join(", ", parameters.Select(p =>
        {
            var text = Render(p.ParameterType, nullability.Create(p)) + " " + p.Name;
            if (!p.HasDefaultValue)
                return text;
            return text + " = " + (p.DefaultValue is null
                ? (p.ParameterType.IsValueType && Nullable.GetUnderlyingType(p.ParameterType) is null ? "default" : "null")
                : Convert.ToString(p.DefaultValue, System.Globalization.CultureInfo.InvariantCulture));
        }));

    private static string Render(Type type, NullabilityInfo? info)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
            return Render(underlying, null) + "?";
        string text;
        if (type.IsArray)
        {
            text = Render(type.GetElementType()!, info?.ElementType) + "[]";
        }
        else if (type.IsGenericType)
        {
            var arguments = type.GetGenericArguments();
            var rendered = arguments.Select((argument, i) =>
                Render(argument, info is not null && i < info.GenericTypeArguments.Length ? info.GenericTypeArguments[i] : null));
            text = type.Name[..type.Name.IndexOf('`')] + "<" + string.Join(", ", rendered) + ">";
        }
        else
        {
            text = type == typeof(int) ? "int" : type == typeof(string) ? "string" : type == typeof(bool) ? "bool"
                : type == typeof(ulong) ? "ulong" : type == typeof(uint) ? "uint" : type == typeof(byte) ? "byte"
                : type == typeof(void) ? "void" : type == typeof(object) ? "object" : type.Name;
        }

        return !type.IsValueType && info?.ReadState == NullabilityState.Nullable ? text + "?" : text;
    }
}
