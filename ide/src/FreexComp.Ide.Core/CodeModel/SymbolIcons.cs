using FreexComp.LanguageService;

namespace FreexComp.Ide.Core.CodeModel;

/// <summary>
/// The icon name per symbol kind: completion rows (UI-ED-114) and Structure rows (UI-TW-203). <c>symbol-*</c> icons are the
/// symbol set (<c>Assets/SymbolIcons</c>); <c>folder</c> and <c>error</c> come from the toolbar set (toolbars.md §4).
/// </summary>
public static class SymbolIcons
{
    public static string For(CompletionItemKind kind) => kind switch
    {
        CompletionItemKind.Keyword or CompletionItemKind.Directive => "symbol-keyword",
        CompletionItemKind.Snippet => "symbol-snippet",
        CompletionItemKind.Unit => "symbol-unit",
        CompletionItemKind.Class => "symbol-class",
        CompletionItemKind.Interface => "symbol-interface",
        CompletionItemKind.Enum => "symbol-enum",
        CompletionItemKind.EnumMember or CompletionItemKind.Constant => "symbol-constant",
        CompletionItemKind.Type => "symbol-type",
        CompletionItemKind.Routine or CompletionItemKind.Method => "symbol-routine",
        CompletionItemKind.Property => "symbol-property",
        _ => "symbol-field", // field, variable, parameter, label
    };

    public static string For(StructureKind kind) => kind switch
    {
        StructureKind.Group => "folder",
        StructureKind.Error => "error",
        StructureKind.Unit => "symbol-unit",
        StructureKind.Class => "symbol-class",
        StructureKind.Record => "symbol-record",
        StructureKind.Interface => "symbol-interface",
        StructureKind.Enum => "symbol-enum",
        StructureKind.Type => "symbol-type",
        StructureKind.Constant => "symbol-constant",
        StructureKind.Routine => "symbol-routine",
        StructureKind.Property => "symbol-property",
        _ => "symbol-field", // fields, variables, design-view components (the form's fields)
    };

    /// <summary>UI-TW-201: none for public / published, a lock for private, a half lock for protected (<c>strict</c> too).</summary>
    public static string? VisibilityOverlay(string? visibility) => visibility switch
    {
        "private" or "strict private" => "lock",
        "protected" or "strict protected" => "symbol-protected",
        _ => null,
    };
}
