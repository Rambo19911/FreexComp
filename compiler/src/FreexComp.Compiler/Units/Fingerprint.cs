using System.Security.Cryptography;
using System.Text;

namespace FreexComp.Compiler.Units;

/// <summary>
/// CMP-150 rule 3 (C-164 first slice; the <c>.pcu</c> tasks C-073… record and compare it): the fingerprint of the options that
/// affect code — sorted define set, <c>-O</c> level, <c>-g</c>, default directive states, target fingerprint (CMP-1005) — plus the content hash
/// of the unit alias table (LANG-533), as <c>sha256:&lt;hex&gt;</c>.
/// </summary>
internal static class Fingerprint
{
    public static string Options(CompilationOptions options, string aliasTableHash)
    {
        var text = new StringBuilder();
        // the define set the units are parsed with: a console application also defines CONSOLE (C-075)
        var defines = options.AppType == AppType.Console ? options.Defines.Add("CONSOLE") : options.Defines;
        text.Append("defines=").AppendJoin(',', defines.Select(d => d.ToUpperInvariant()).Distinct().Order(StringComparer.Ordinal)).Append('\n');
        text.Append("O=").Append(options.Optimization).Append('\n');
        text.Append("g=").Append(options.DebugInfo).Append('\n');
        text.Append("R=").Append(options.RangeChecks).Append(";Q=").Append(options.OverflowChecks).Append(";C=").Append(options.Assertions).Append('\n');
        text.Append("target=").Append(options.Target.Fingerprint).Append('\n');        // CMP-1005
        text.Append("aliases=").Append(aliasTableHash).Append('\n');
        text.Append("rtti=").Append(options.Rtti).Append('\n');                              // C-169: the default RTTI scope (CMP-150)
        return "sha256:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
