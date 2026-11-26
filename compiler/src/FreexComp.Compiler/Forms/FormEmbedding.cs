using System.Collections.Immutable;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Forms;

/// <summary>One <c>{$FORM}</c> blob (ABI-150): the upper-cased root class name, the file's bytes verbatim and its path (a <c>.pcu</c> source, C-114).</summary>
internal sealed record EmbeddedForm(string Name, byte[] Data, string Path);

/// <summary>One scanned <c>{$FORM}</c> directive of a unit: its location, the file's path and bytes, the root object and the root
/// class (both null after E3100, F0001 or E3103), and the diagnostics found so far.</summary>
internal sealed record ScannedForm(Location At, string Path, byte[] Bytes, FormObject? Root, ClassTypeSymbol? Class, ImmutableArray<Diagnostic> Diagnostics);

/// <summary>
/// LANG-670…672 (C-114): the <c>{$FORM file}</c> directives of a bound unit (the file and its includes, source order). Each file is
/// read relative to the file holding the directive (missing or unreadable → E3100 at the directive), scanned with
/// <see cref="FormFileScanner"/> (its F0001 is an error) and checked against the unit's declarations: the root class must be
/// declared in this unit (E3103) and embedded once (E3104 at the later directive); every non-root object, frame instances
/// (<c>inline</c>) included, needs a published field of the root class or an ancestor (E3101), of the object's class or an ancestor
/// of it when that class is visible here (E3102), except the override blocks of an instance (LANG-952); every event handler, those
/// of instances and override blocks included (LANG-953), a published method of the root class (E3101), compatible with the event
/// property when the component class and its property are visible (E3102). E3101/E3102/E3103 point into the form file.
/// C-343: the <c>inherited</c> blocks of a descendant file are checked with their contents (LANG-956), and the root keyword must fit
/// the class — <c>inherited</c> needs an ancestor with a form (E3105), <c>object</c> forbids one (E3106; LANG-957, 958). Which
/// classes have a form (LANG-959) is known only after every unit's directives are read, so <see cref="Scan"/> runs for every unit
/// of the compilation before <see cref="Collect"/> runs for any.
/// </summary>
internal static class FormEmbedding
{
    /// <summary>The first half: reads and scans each directive's file and finds its root class (E3100, F0001, E3103).</summary>
    public static ImmutableArray<ScannedForm> Scan(BindingResult result, SyntaxTree tree, IFileSystem fs)
    {
        var scanned = ImmutableArray.CreateBuilder<ScannedForm>();
        foreach (var (file, directive) in tree.IncludedTrees.Prepend(tree).SelectMany(t => t.Directives.Select(d => (t.Text, d))).Where(p => p.d.Name == "FORM"))
        {
            var span = directive.Span;
            var at = new Location(file.Path, span, new LinePositionSpan(file.Lines.GetLinePosition(span.Start), file.Lines.GetLinePosition(span.End)));
            var name = DirectiveProcessor.Unquote(directive.Argument).Replace('/', '\\');
            var path = PathUtil.Normalize(Path.IsPathFullyQualified(name) ? name : Path.Combine(Path.GetDirectoryName(file.Path)!, name));
            var diagnostics = new List<Diagnostic>();
            var found = ScanOne(result, at, directive.Argument, name, path, fs, diagnostics);
            scanned.Add(new ScannedForm(at, path, found?.Bytes ?? [], found?.Root, found?.Class, [.. diagnostics]));
        }

        return scanned.ToImmutable();
    }

    /// <summary>LANG-959: the classes that get a form from <paramref name="scanned"/> (a scan that found its root class), each once, declared casing, directive order.</summary>
    public static ImmutableArray<string> FormClasses(ImmutableArray<ScannedForm> scanned) =>
        [.. scanned.Select(f => f.Class).OfType<ClassTypeSymbol>().Distinct().Select(c => c.Name)];

    /// <summary>
    /// The second half: checks each scanned form (LANG-671, 952…958) and returns the blobs to embed. <paramref name="hasForm"/>
    /// tells whether a class has a form (LANG-959: from the scans of the units of the compilation or from a <c>.pcu</c>).
    /// </summary>
    public static ImmutableArray<EmbeddedForm> Collect(BindingResult result, ImmutableArray<ScannedForm> scanned, Func<ClassTypeSymbol, bool> hasForm, List<Diagnostic> diagnostics)
    {
        var forms = ImmutableArray.CreateBuilder<EmbeddedForm>();
        var embedded = new HashSet<ClassTypeSymbol>();
        foreach (var form in scanned)
        {
            diagnostics.AddRange(form.Diagnostics);
            if (form is not { Root: { } root, Class: { } @class })
            {
                continue;
            }

            Check(result, form.Path, root, @class, hasForm, diagnostics);
            if (!embedded.Add(@class))
            {
                diagnostics.Add(Diagnostic.Create("E3104", form.At, @class.Name));
                continue;
            }

            forms.Add(new EmbeddedForm(UpperAscii(@class.Name), form.Bytes, form.Path));
        }

        return forms.ToImmutable();
    }

    private static (byte[] Bytes, FormObject Root, ClassTypeSymbol Class)? ScanOne(BindingResult result, Location at, string argument, string name, string path,
        IFileSystem fs, List<Diagnostic> diagnostics)
    {
        byte[] bytes;
        try
        {
            if (name.Length == 0 || !fs.FileExists(path))
            {
                diagnostics.Add(Diagnostic.Create("E3100", at, name.Length == 0 ? argument : path));
                return null;
            }

            bytes = fs.ReadAllBytes(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(Diagnostic.Create("E3100", at, path));
            return null;
        }

        if (FormFileScanner.Decode(bytes) is not { } text)
        {
            diagnostics.Add(FormFileScanner.SyntaxError(At(path, 1, 1), "invalid UTF-8"));
            return null;
        }

        var scan = FormFileScanner.Scan(path, text);
        diagnostics.AddRange(scan.Diagnostics);
        if (scan.Root is not { } root)
        {
            return null;
        }

        var @class = DeclarationBinder.ClassesOf(result.Unit).FirstOrDefault(c => IdentifierComparer.Instance.Equals(c.Name, root.ClassName));
        if (@class is null)
        {
            diagnostics.Add(Diagnostic.Create("E3103", At(path, root.Line, root.Column), root.Name, root.ClassName));
            return null;
        }

        return (bytes, root, @class);
    }

    /// <summary>ABI-150: ordinal upper-casing of <c>a..z</c> only.</summary>
    internal static string UpperAscii(string name) => string.Concat(name.Select(c => c is >= 'a' and <= 'z' ? (char)(c - 32) : c));

    private static Location At(string path, int line, int column)
    {
        var position = new LinePosition(line - 1, column - 1);
        return new Location(path, default, new LinePositionSpan(position, position));
    }

    /// <summary>LANG-671, LANG-952…958.</summary>
    private static void Check(BindingResult result, string path, FormObject root, ClassTypeSymbol @class, Func<ClassTypeSymbol, bool> hasForm, List<Diagnostic> diagnostics)
    {
        // LANG-957, 958: the nearest proper ancestor with a form decides the root keyword
        var ancestor = @class.SelfAndAncestors().Skip(1).FirstOrDefault(hasForm);
        if (root.Keyword == "inherited" && ancestor is null)
        {
            diagnostics.Add(Diagnostic.Create("E3105", At(path, root.Line, root.Column), root.Name, @class.Name));
        }
        else if (root.Keyword == "object" && ancestor is not null)
        {
            diagnostics.Add(Diagnostic.Create("E3106", At(path, root.Line, root.Column), root.Name, @class.Name, ancestor.Name));
        }

        CheckEvents(result, path, root, @class, root, diagnostics);
        foreach (var item in root.Descendants())
        {
            if (item.IsOverride)
            {
                CheckEvents(result, path, root, @class, item, diagnostics);  // a component of the frame: no field of the root (LANG-952)
                continue;
            }

            var field = @class.SelfAndAncestors().SelectMany(c => c.Fields)
                .FirstOrDefault(f => f.Visibility == Visibility.Published && IdentifierComparer.Instance.Equals(f.Name, item.Name));
            if (field is null)
            {
                diagnostics.Add(Diagnostic.Create("E3101", At(path, item.Line, item.Column), root.Name, "field", item.Name + ": " + item.ClassName, @class.Name));
            }
            else if (result.Scope.Lookup(item.ClassName) is ClassTypeSymbol component
                     ? !(field.Type is ClassTypeSymbol declared && component.InheritsFrom(declared))
                     : field.Type is not ClassTypeSymbol)
            {
                diagnostics.Add(Diagnostic.Create("E3102", At(path, item.Line, item.Column), root.Name, field.Name, item.ClassName));
            }

            CheckEvents(result, path, root, @class, item, diagnostics);
        }
    }

    private static void CheckEvents(BindingResult result, string path, FormObject root, ClassTypeSymbol @class, FormObject item, List<Diagnostic> diagnostics)
    {
        foreach (var e in item.Events)
        {
            var handler = @class.SelfAndAncestors().SelectMany(c => c.Methods)
                .FirstOrDefault(m => m.Visibility == Visibility.Published && IdentifierComparer.Instance.Equals(m.Name, e.Handler));
            if (handler is null)
            {
                diagnostics.Add(Diagnostic.Create("E3101", At(path, e.Line, e.Column), root.Name, "method", e.Handler, @class.Name));
                continue;
            }

            // the event property's type, when the component class is visible here (the root's own class for the root's events)
            var component = ReferenceEquals(item, root) ? @class : result.Scope.Lookup(item.ClassName) as ClassTypeSymbol;
            var property = component?.SelfAndAncestors().SelectMany(c => c.Properties).FirstOrDefault(p => IdentifierComparer.Instance.Equals(p.Name, e.Property));
            if (property?.Type is MethodPointerTypeSymbol eventType && (handler.IsClassMethod || !Signatures.Compatible(handler, eventType.Signature)))
            {
                diagnostics.Add(Diagnostic.Create("E3102", At(path, e.Line, e.Column), root.Name, handler.Name, eventType.Name));
            }
        }
    }
}
