using FreexComp.Designer.Catalog;
using FreexComp.Designer.Editing;
using FreexComp.Designer.Model;
using FreexComp.Designer.Rendering;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Surface;

/// <summary>
/// Designer verbs (design-surface.md §11.6, DSG-SUR-190 … 196; D-097): the verb component of the selection, its metadata
/// verbs, and the commands <c>designer.newChild</c>, <c>deletePage</c>, <c>nextPage</c>, <c>previousPage</c>,
/// <c>editCollection</c> and <c>editComponent</c> (the two editors are opened by the host).
/// </summary>
public sealed partial class DesignSurfaceController
{
    /// <summary>
    /// DSG-SUR-190: with exactly one component selected, that component when its class has verbs; else, when its class has
    /// <c>parentClasses</c>, the nearest ancestor with verbs (a tab sheet, tool button, menu item or action offers its
    /// owner's verbs; a sub-item of a menu item the menu's). Null otherwise and for a multi-selection.
    /// </summary>
    public FormObject? VerbComponent
    {
        get
        {
            if (Selection.Items is not [var selected])
            {
                return null;
            }

            if (HasVerbs(selected))
            {
                return selected;
            }

            for (var child = selected; child.Info is { ParentClasses.IsEmpty: false } && child.Parent is { } parent; child = parent)
            {
                if (HasVerbs(parent))
                {
                    return parent;
                }
            }

            return null;
        }
    }

    /// <summary>DSG-SUR-191: the verbs of <see cref="VerbComponent"/> in metadata order (verbs naming an unknown command are not shown).</summary>
    public IReadOnlyList<DesignerVerb> Verbs => VerbComponent is { } component ? KnownVerbs(component) : [];

    /// <summary>
    /// <see cref="CanExecute(string)"/> with the command's argument (the verb commands of DSG-SUR-192 … 196 take one).
    /// </summary>
    public bool CanExecute(string commandId, string? argument) =>
        Commands.Verbs.Contains(commandId) ? !IsReadOnly && CanExecuteVerb(commandId, argument) : CanExecute(commandId);

    private static IReadOnlyList<DesignerVerb> KnownVerbs(FormObject component) =>
        component.Info is { } info ? [.. info.DesignerVerbs.Where(v => Commands.Verbs.Contains(v.Command))] : [];

    private static bool HasVerbs(FormObject obj) => KnownVerbs(obj).Count > 0;

    /// <summary>DSG-SUR-191 … 196: the verb component exists and is not preserved, no drag or tab-order mode, and the command's own rule.</summary>
    private bool CanExecuteVerb(string commandId, string? argument)
    {
        if (VerbComponent is not { IsPreserved: false, Info: { } info } component || IsPointerActive || State == InteractionState.TabOrder)
        {
            return false;
        }

        return commandId switch
        {
            Commands.NewChild => ParseNewChild(component, argument) is not null,
            Commands.DeletePage => IsPageControl(info) && Pages(component).Count >= 1,
            Commands.NextPage or Commands.PreviousPage => IsPageControl(info) && Pages(component).Count >= 2,
            Commands.EditCollection => argument is not null && info.FindProperty(argument) is { Kind: PropertyKind.Collection },
            Commands.EditComponent => info.ComponentEditor is not null,
            _ => false,
        };
    }

    /// <summary>Runs a verb command on the verb component.</summary>
    private void ExecuteVerb(string commandId, string? argument)
    {
        var component = VerbComponent!;
        switch (commandId)
        {
            case Commands.NewChild:
                var (info, properties) = ParseNewChild(component, argument)!.Value;
                CreateChild(component, info, component.Children.Count, properties, select: true);
                break;
            case Commands.DeletePage:
                DeletePage(component);
                break;
            case Commands.NextPage or Commands.PreviousPage:
                StepPage(component, commandId == Commands.NextPage ? 1 : -1);
                break;
            case Commands.EditCollection:
                _host.EditCollection(component, component.Info!.FindProperty(argument!)!.Name);
                break;
            case Commands.EditComponent:
                _host.EditComponent(component);
                break;
        }
    }

    /// <summary>
    /// DSG-SUR-192 / DSG-SUR-197: creates a component of <paramref name="info"/>'s class as child <paramref name="index"/>
    /// of <paramref name="parent"/> — named per DSG-TP-050 (or <paramref name="name"/>), with the always-written geometry
    /// that is stored for its class, the metadata <c>initial</c> values, then <paramref name="properties"/>, then the
    /// TabOrder when focusable — with its field through code sync, as one undo unit "Add {ClassName}". A new tab sheet
    /// becomes the active page (<c>ActivePage</c> stored unless it is the default page, DSG-RND-017). With
    /// <paramref name="select"/> the new component becomes the only selection (else the selection is kept). Null when the
    /// form is read-only or code sync refused the field.
    /// </summary>
    public FormObject? CreateChild(FormObject parent, ComponentInfo info, int index, IReadOnlyList<(string Path, FormValue Value)> properties,
        bool select, string? name = null)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(properties);
        if (IsReadOnly)
        {
            return null;
        }

        return CreateCore(info, parent, (0, 0), null, index, properties, select, name);
    }

    /// <summary>DSG-TP-050: the name a new component with <paramref name="prefix"/> gets (<c>MenuItem</c>, separators <c>N</c>, UIF-727).</summary>
    public string FreeComponentName(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);
        return NewComponentName(prefix);
    }

    /// <summary>
    /// <c>Class[;Prop=Value…]</c> (component-metadata.md §7): the class (known, not abstract, allowed under
    /// <paramref name="parent"/> by <c>childClasses</c>/<c>parentClasses</c>) and the property pairs, each value in
    /// <c>.form</c> value syntax; null when anything is wrong.
    /// </summary>
    private (ComponentInfo Info, IReadOnlyList<(string Path, FormValue Value)> Properties)? ParseNewChild(FormObject parent, string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument) || parent.Info is null)
        {
            return null;
        }

        var parts = argument.Split(';');
        if (Catalog.Find(parts[0].Trim()) is not { IsAbstract: false } info || !AllowsChild(parent, info))
        {
            return null;
        }

        var properties = new List<(string, FormValue)>();
        foreach (var pair in parts.Skip(1))
        {
            var eq = pair.IndexOf('=', StringComparison.Ordinal);
            if (eq <= 0)
            {
                return null;
            }

            var path = pair[..eq].Trim();
            var text = "object X: " + info.ClassName + "\n  " + path + " = " + pair[(eq + 1)..].Trim() + "\nend\n";
            if (FormParser.ParseFragment(text, "verb.form", Catalog) is not { HasSyntaxErrors: false, Objects: [var parsed] }
                || parsed.FindProperty(path) is not { } property)
            {
                return null;
            }

            properties.Add((property.Path, property.Value));
        }

        return (info, properties);
    }

    /// <summary>The parent/child rules of <c>childClasses</c> and <c>parentClasses</c> (component-metadata.md §2.1).</summary>
    public bool AllowsChild(FormObject parent, ComponentInfo child)
    {
        ArgumentNullException.ThrowIfNull(parent);
        ArgumentNullException.ThrowIfNull(child);
        return parent is { IsPreserved: false, Info: { } p }
            && (p.ChildClasses.IsEmpty || p.ChildClasses.Any(c => Catalog.IsAssignable(child.ClassName, c)))
            && (child.ParentClasses.IsEmpty || child.ParentClasses.Any(c => Catalog.IsAssignable(p.ClassName, c)));
    }

    /// <summary>DSG-SUR-193: a class whose <c>childClasses</c> holds a sheet class (the <c>pagecontrol</c> renderer).</summary>
    private static bool IsPageControl(ComponentInfo info) => info.RendererId == "pagecontrol" && !info.ChildClasses.IsEmpty;

    /// <summary>The pages of a page control in creation order.</summary>
    private static List<FormObject> Pages(FormObject pageControl) =>
        [.. pageControl.Children.Where(c => c.Info is { Kind: not ComponentKind.NonVisual })];

    /// <summary>The change that makes <paramref name="sheet"/> the design-active page: <c>ActivePage</c> stored, or removed for the default page.</summary>
    private static FormChange? ActivePageChange(FormObject pageControl, FormObject sheet)
    {
        var stored = pageControl.FindProperty("ActivePage") is not null;
        if (ReferenceEquals(sheet, PageControlRenderer.DefaultSheet(pageControl)))
        {
            return stored ? new RemoveProperty(pageControl.Name, "ActivePage") : null;
        }

        return new SetProperty(pageControl.Name, "ActivePage", new IdentValue(sheet.Name));
    }

    /// <summary>
    /// DSG-SUR-193: deletes the design-active page with its subtree as <c>edit.delete</c> does, then activates the following
    /// page (the preceding one when it was the last, UIF-777); the page control becomes the selection. One "Delete" unit.
    /// </summary>
    private void DeletePage(FormObject pageControl)
    {
        var pages = Pages(pageControl);
        var active = PageControlRenderer.ActiveSheet(pageControl) ?? pages[0];
        var index = pages.IndexOf(active);
        var next = index + 1 < pages.Count ? pages[index + 1] : index > 0 ? pages[index - 1] : null;
        DeleteObjects([active], pageControl, () =>
        {
            // After the removal (the reference to the deleted page is cleared): the neighbour becomes the active page.
            return next is not null && ActivePageChange(pageControl, next) is { } change ? [change] : [];
        });
    }

    /// <summary>DSG-SUR-194: the next / previous page in creation order (wrapping) becomes active, as a tab click does.</summary>
    private void StepPage(FormObject pageControl, int step)
    {
        var pages = Pages(pageControl);
        var active = PageControlRenderer.ActiveSheet(pageControl) ?? pages[0];
        var target = pages[(pages.IndexOf(active) + step + pages.Count) % pages.Count];
        if (ActivePageChange(pageControl, target) is { } change)
        {
            Commit(UndoTitles.Property, [change], select: target);
        }
        else
        {
            Selection.Select(target);
        }
    }
}
