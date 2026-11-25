using System.Collections.Immutable;
using FreexComp.Compiler.Binding;
using FreexComp.Compiler.Symbols;
using FreexComp.Compiler.Syntax;
using FreexComp.Compiler.Text;
using FreexComp.Compiler.Types;

namespace FreexComp.Compiler.Forms;

/// <summary>One <c>{$FORM}</c> blob (ABI-150): the upper-cased root class name, the file's bytes verbatim and its path (a <c>.pcu</c> source, C-114).</summary>
internal sealed record EmbeddedForm(string Name, byte[] Data, string Path);

/// <summary>
/// LANG-670…672 (C-114): the <c>{$FORM file}</c> directives of a bound unit (the file and its includes, source order). Each file is
/// read relative to the file holding the directive (missing or unreadable → E3100 at the directive), scanned with
/// <see cref="FormFileScanner"/> (its F0001 is an error) and checked against the unit's declarations: the root class must be
/// declared in this unit (E3103) and embedded once (E3104 at the later directive); every non-root object, frame instances
/// (<c>inline</c>) included, needs a published field of the root class or an ancestor (E3101), of the object's class or an ancestor
/// of it when that class is visible here (E3102), except the override blocks of an instance (LANG-952); every event handler, those
/// of instances and override blocks included (LANG-953), a published method of the root class (E3101), compatible with the event
/// property when the component class and its property are visible (E3102). E3101/E3102/E3103 point into the form file.
/// </summary>
internal static class FormEmbedding
{
    public static ImmutableArray<EmbeddedForm> Collect(BindingResult result, SyntaxTree tree, IFileSystem fs, List<Diagnostic> diagnostics)
    {
        var forms = ImmutableArray.CreateBuilder<EmbeddedForm>();
        var embedded = new HashSet<ClassTypeSymbol>();
        foreach (var (file, directive) in tree.IncludedTrees.Prepend(tree).SelectMany(t => t.Directives.Select(d => (t.Text, d))).Where(p => p.d.Name == "FORM"))
        {
            var span = directive.Span;
            var at = new Location(file.Path, span, new LinePositionSpan(file.Lines.GetLinePosition(span.Start), file.Lines.GetLinePosition(span.End)));
            var name = DirectiveProcessor.Unquote(directive.Argument).Replace('/', '\\');
            var path = PathUtil.Normalize(Path.IsPathFullyQualified(name) ? name : Path.Combine(Path.GetDirectoryName(file.Path)!, name));
            byte[] bytes;
            try
            {
                if (name.Length == 0 || !fs.FileExists(path))
                {
                    diagnostics.Add(Diagnostic.Create("E3100", at, name.Length == 0 ? directive.Argument : path));
                    continue;
                }

                bytes = fs.ReadAllBytes(path);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                diagnostics.Add(Diagnostic.Create("E3100", at, path));
                continue;
            }

            if (FormFileScanner.Decode(bytes) is not { } text)
            {
                diagnostics.Add(FormFileScanner.SyntaxError(At(path, 1, 1), "invalid UTF-8"));
                continue;
            }

            var scan = FormFileScanner.Scan(path, text);
            diagnostics.AddRange(scan.Diagnostics);
            if (scan.Root is not { } root)
            {
                continue;
            }

            if (Check(result, path, root, diagnostics) is not { } @class)
            {
                continue;
            }

            if (!embedded.Add(@class))
            {
                diagnostics.Add(Diagnostic.Create("E3104", at, @class.Name));
                continue;
            }

            forms.Add(new EmbeddedForm(UpperAscii(@class.Name), bytes, path));
        }

        return forms.ToImmutable();
    }

    /// <summary>ABI-150: ordinal upper-casing of <c>a..z</c> only.</summary>
    internal static string UpperAscii(string name) => string.Concat(name.Select(c => c is >= 'a' and <= 'z' ? (char)(c - 32) : c));

    private static Location At(string path, int line, int column)
    {
        var position = new LinePosition(line - 1, column - 1);
        return new Location(path, default, new LinePositionSpan(position, position));
    }

    /// <summary>LANG-671, LANG-952…954; the root class, or null after E3103.</summary>
    private static ClassTypeSymbol? Check(BindingResult result, string path, FormObject root, List<Diagnostic> diagnostics)
    {
        var @class = DeclarationBinder.ClassesOf(result.Unit).FirstOrDefault(c => IdentifierComparer.Instance.Equals(c.Name, root.ClassName));
        if (@class is null)
        {
            diagnostics.Add(Diagnostic.Create("E3103", At(path, root.Line, root.Column), root.Name, root.ClassName));
            return null;
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

        return @class;
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
