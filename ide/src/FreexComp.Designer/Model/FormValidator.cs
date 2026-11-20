using System.Globalization;
using FreexComp.Compiler;
using FreexComp.Compiler.Text;
using FreexComp.Designer.Catalog;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Model;

/// <summary>
/// Semantic checks of a form model (form-document.md §5, DSG-FD-070): unresolved component references (<c>F0105</c>),
/// references to a component of the wrong class, children their parent does not accept, and <c>min</c>/<c>max</c>
/// violations (<c>F0103</c>). Values are never changed or preserved by validation. Runs after the parse and after every
/// applied change and replaces <see cref="FormDocument.Diagnostics"/>: the parser's diagnostics whose object (and
/// property line) still exist, plus the current validation results, in source order.
/// </summary>
public static class FormValidator
{
    public static void Validate(FormDocument document, ComponentCatalog catalog)
    {
        var results = new List<FormDiagnostic>();
        foreach (var (diagnostic, owner, property) in document.ParseDiagnostics)
        {
            if (IsAttached(document, owner) && (property is null || owner.Properties.Contains(property)))
            {
                results.Add(diagnostic with { ObjectName = owner.Name });
            }
        }

        var check = new Checker(document, catalog, results);
        foreach (var obj in document.AllObjects())
        {
            if (!obj.IsPreserved && obj.Info is not null)
            {
                check.Object(obj, obj.Info);
            }
        }

        document.SetDiagnostics(results.OrderBy(d => d.Line).ThenBy(d => d.Col));
    }

    private static bool IsAttached(FormDocument document, FormObject obj)
    {
        while (obj.Parent is { } parent)
        {
            obj = parent;
        }

        return ReferenceEquals(obj, document.Root);
    }

    private sealed class Checker(FormDocument document, ComponentCatalog catalog, List<FormDiagnostic> results)
    {
        public void Object(FormObject obj, ComponentInfo info)
        {
            if (obj.Parent is { Info: { } parentInfo } parent && ParentProblem(info, parentInfo))
            {
                Invalid(obj.HeaderSpan, obj, "Parent", $"{obj.TypeName} cannot be a child of {parent.TypeName}");
            }

            foreach (var property in obj.Properties)
            {
                if (!property.IsPreserved && property.Info is { } propertyInfo)
                {
                    Value(obj, property, property.Path, propertyInfo, property.Value);
                }
            }
        }

        /// <summary>component-metadata.md §2.1: <c>acceptsChildren</c> (controls only), <c>childClasses</c>, <c>parentClasses</c>.</summary>
        private bool ParentProblem(ComponentInfo child, ComponentInfo parent)
        {
            var isControl = child.Kind is not (ComponentKind.NonVisual or ComponentKind.Form);
            return (isControl && !parent.AcceptsChildren)
                || (!parent.ChildClasses.IsEmpty && !parent.ChildClasses.Any(c => catalog.IsAssignable(child.ClassName, c)))
                || (!child.ParentClasses.IsEmpty && !child.ParentClasses.Any(p => catalog.IsAssignable(parent.ClassName, p)));
        }

        private void Value(FormObject obj, FormProperty property, string path, PropertyInfo info, FormValue value)
        {
            switch (value)
            {
                case IdentValue { Name: var name } when info.Kind == PropertyKind.Component && !name.Equals("nil", StringComparison.OrdinalIgnoreCase):
                    Reference(obj, property, path, info, name);
                    break;
                case IntegerValue or FloatValue when info.Min is not null || info.Max is not null:
                    var number = value is IntegerValue i ? i.Value : ((FloatValue)value).Value;
                    if (number < (info.Min ?? double.MinValue) || number > (info.Max ?? double.MaxValue))
                    {
                        Invalid(property.ValueSpan, obj, path, $"value out of range {Bound(info, info.Min, low: true)}..{Bound(info, info.Max, low: false)}");
                    }

                    break;
                case CollectionValue collection when info.ItemClass is { } itemClass && catalog.FindType(itemClass) is { Kind: TypeKind.Object } itemType:
                    // Collection item properties against the item class (lead note D-009…D-016): references and limits.
                    foreach (var item in collection.Items)
                    {
                        foreach (var (itemPath, itemValue, _) in item.Properties)
                        {
                            if (itemType.Properties.FirstOrDefault(p => p.Name.Equals(itemPath, StringComparison.OrdinalIgnoreCase)) is { } itemInfo)
                            {
                                Value(obj, property, path + "." + itemPath, itemInfo, itemValue);
                            }
                        }
                    }

                    break;
            }
        }

        /// <summary>DSG-FD-028: a plain name must name a component of the form (the root included) of the property's class.</summary>
        private void Reference(FormObject obj, FormProperty property, string path, PropertyInfo info, string name)
        {
            if (document.FindByName(name) is not { } target)
            {
                Warn(property.ValueSpan, obj, path, "F0105", $"Component {name} not found");
            }
            else if (target.Info is { } targetInfo && catalog.Find(info.Type) is not null && !catalog.IsAssignable(targetInfo.ClassName, info.Type))
            {
                Invalid(property.ValueSpan, obj, path, $"'{name}' is not a {info.Type}");
            }
        }

        private static string Bound(PropertyInfo info, double? bound, bool low)
        {
            var value = bound ?? info.Type.ToLowerInvariant() switch
            {
                "integer" or "tcolor" or "tcursor" => low ? int.MinValue : int.MaxValue,
                "double" => low ? double.MinValue : double.MaxValue,
                _ => low ? long.MinValue : long.MaxValue,
            };
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private void Invalid(TextSpan span, FormObject obj, string path, string detail) =>
            Warn(span, obj, path, "F0103", $"Invalid value for property {path}: {detail}");

        private void Warn(TextSpan span, FormObject obj, string path, string code, string message)
        {
            var (line, col) = document.Lines.LineCol(span.Start);
            var (endLine, endCol) = document.Lines.LineCol(span.End);
            results.Add(new FormDiagnostic(code, DiagnosticSeverity.Warning, message, document.SourcePath, line, col, endLine, endCol, obj.Name, path));
        }
    }
}
