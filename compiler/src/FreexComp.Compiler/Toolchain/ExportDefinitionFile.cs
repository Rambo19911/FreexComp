using System.Collections.Immutable;
using System.Globalization;
using System.Text;

namespace FreexComp.Compiler.Toolchain;

/// <summary>CMP-1061: the module-definition file handed to the linker for a library (<c>&lt;objdir&gt;\&lt;base&gt;.exports.def</c>).</summary>
internal static class ExportDefinitionFile
{
    /// <summary>
    /// <c>EXPORTS</c>, then one line per export in ordinal order: four spaces, export name, one space, <c>@</c>, ordinal. LF line
    /// ends, final newline. No other statement: the DLL name comes from <c>/out</c> and the thunk carries the export name.
    /// </summary>
    public static string Render(ImmutableArray<ExportInfo> exports)
    {
        var text = new StringBuilder("EXPORTS\n");
        foreach (var export in exports.OrderBy(e => e.Ordinal))
        {
            text.Append("    ").Append(export.Name).Append(" @").Append(export.Ordinal.ToString(CultureInfo.InvariantCulture)).Append('\n');
        }

        return text.ToString();
    }
}
