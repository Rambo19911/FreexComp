using System.Text.Json;
using System.Text.RegularExpressions;
using FreexComp.Compiler.Types;

namespace FreexComp.MetadataTesting;

/// <summary>
/// <c>MetadataSignaturesMatchSource</c> and <c>MetadataSharedDeclsAgree</c> (metadata-authoring.md §4, UIF-957, UIF-960): the
/// declarations under <c>types</c> and <c>eventSignatures</c> against the Pascal source and against each other.
/// </summary>
public static partial class MetadataDecls
{
    /// <summary>
    /// Every <c>eventSignatures</c> text equals the normalised source declaration; every enum/set equals the source enum values /
    /// element type; every object type's (flattened) properties equal the published properties of that class.
    /// </summary>
    public static List<string> SignaturesMatchSource(MetadataRoot root, SourceComponents source)
    {
        var problems = new List<string>();
        foreach (var file in root.Files)
        {
            foreach (var s in file.EventSignatures)
            {
                var at = $"{file.Name}: eventSignatures.{s.Name}";
                var decl = source.FindTypes(s.Name).OfType<MethodPointerTypeSymbol>().ToList();
                if (decl.Count == 0)
                {
                    problems.Add($"{at}: no method-pointer type {s.Name} in the source");
                    continue;
                }

                var texts = decl.Select(SourceSignature).Distinct(StringComparer.Ordinal).ToList();
                var meta = s.Value.ValueKind == JsonValueKind.String ? s.Value.GetString() : s.Value.GetRawText();
                if (!texts.Contains(meta!, StringComparer.Ordinal))
                {
                    problems.Add($"{at}: metadata \"{meta}\", source \"{string.Join("\" | \"", texts)}\"");
                }
            }

            foreach (var t in file.Types)
            {
                CompareType(root, file, t.Name, t.Value, source, problems);
            }
        }

        return problems;
    }

    /// <summary>A name declared under <c>types</c> or <c>eventSignatures</c> of several files has identical text in all of them.</summary>
    public static List<string> SharedDeclsAgree(MetadataRoot root)
    {
        var problems = new List<string>();
        foreach (var section in new[] { "types", "eventSignatures" })
        {
            var decls = root.Files
                .SelectMany(f => (section == "types" ? f.Types : f.EventSignatures).Select(p => (File: f.Name, p.Name, Text: p.Value.GetRawText())))
                .GroupBy(d => d.Name, StringComparer.Ordinal);
            foreach (var g in decls)
            {
                var variants = g.GroupBy(d => d.Text, StringComparer.Ordinal).ToList();
                if (variants.Count > 1)
                {
                    problems.Add($"{section}.{g.Key}: differs between files: "
                        + string.Join("; ", variants.Select(v => string.Join(", ", v.Select(d => d.File)) + " = " + Squeeze(v.Key))));
                }
            }
        }

        return problems;
    }

    private static void CompareType(MetadataRoot root, MetadataFile file, string name, JsonElement t, SourceComponents source, List<string> problems)
    {
        var at = $"{file.Name}: types.{name}";
        var kind = t.TryGetProperty("kind", out var k) ? k.GetString() : null;
        var found = source.FindTypes(name);
        switch (kind)
        {
            case "enum":
                if (found.OfType<EnumTypeSymbol>().FirstOrDefault() is not { } e)
                {
                    problems.Add($"{at}: no enumeration {name} in the source");
                    return;
                }

                var expected = e.EnumMembers.Where(m => m.Ordinal >= e.Low && m.Ordinal <= e.High).Select(m => m.Name).ToList();
                var actual = t.TryGetProperty("values", out var v) ? v.EnumerateArray().Select(x => x.GetString()).ToList() : [];
                if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
                {
                    problems.Add($"{at}: values [{string.Join(", ", actual)}], source [{string.Join(", ", expected)}]");
                }

                break;
            case "set":
                if (found.OfType<SetTypeSymbol>().FirstOrDefault() is not { } s)
                {
                    problems.Add($"{at}: no set type {name} in the source");
                    return;
                }

                var of = t.TryGetProperty("of", out var o) ? o.GetString() : null;
                if (of != s.ElementType?.Name)
                {
                    problems.Add($"{at}: of {of}, source {s.ElementType?.Name}");
                }

                break;
            case "object":
                if (found.OfType<ClassTypeSymbol>().FirstOrDefault() is not { } c)
                {
                    problems.Add($"{at}: no class {name} in the source");
                    return;
                }

                if (t.TryGetProperty("ancestor", out var anc) && !c.SelfAndAncestors().Skip(1).Any(a => a.Name == anc.GetString()))
                {
                    problems.Add($"{at}: ancestor {anc.GetString()} is not an ancestor of {name} in the source");
                }

                var src = SourceComponents.Effective(c).Where(p => !p.IsEvent).ToList();
                var meta = FlattenObject(root, file, t);
                for (var i = 0; i < Math.Max(src.Count, meta.Count); i++)
                {
                    var sp = i < src.Count ? src[i] : null;
                    var mp = i < meta.Count ? meta[i] : (JsonElement?)null;
                    var mName = mp is { } m0 && m0.TryGetProperty("name", out var n) ? n.GetString() : null;
                    if (sp is null)
                    {
                        problems.Add($"{at}.{mName}: extra (not a published property of {name})");
                    }
                    else if (mp is not { } m)
                    {
                        problems.Add($"{at}.{sp.Name}: missing (published {sp.Type} in source)");
                    }
                    else if (mName != sp.Name)
                    {
                        problems.Add($"{at}.{sp.Name}: missing or out of order (metadata position {i + 1} is {mName})");
                    }
                    else
                    {
                        var mType = m.TryGetProperty("type", out var mt) ? mt.GetString() : null;
                        var mKind = m.TryGetProperty("kind", out var mk) ? mk.GetString() : null;
                        if (mType != sp.Type || mKind != sp.Kind)
                        {
                            problems.Add($"{at}.{sp.Name}: type mismatch: metadata {mType}{(mKind is null ? "" : "/" + mKind)}, source {sp.Type}{(sp.Kind is null ? "" : "/" + sp.Kind)}");
                        }
                    }
                }

                break;
        }
    }

    /// <summary>The sub-properties of an object type with its metadata ancestor's first (a redeclaration keeps the ancestor position).</summary>
    private static List<JsonElement> FlattenObject(MetadataRoot root, MetadataFile file, JsonElement t)
    {
        var list = t.TryGetProperty("ancestor", out var a) && root.FindType(file, a.GetString()!) is { } parent
            ? FlattenObject(root, file, parent)
            : [];
        foreach (var p in MetadataComponent.Array(t, "properties"))
        {
            var name = p.TryGetProperty("name", out var n) ? n.GetString() : null;
            var at = list.FindIndex(x => x.TryGetProperty("name", out var m) && m.GetString() == name);
            if (at >= 0)
            {
                list[at] = p;
            }
            else
            {
                list.Add(p);
            }
        }

        return list;
    }

    /// <summary>
    /// UIF-960: the declaration text <c>procedure(…)</c> as written after the type's name, normalised: single spaces, <c>; </c>
    /// between parameters, no space inside the parentheses, no <c>of object</c>.
    /// </summary>
    public static string SourceSignature(MethodPointerTypeSymbol t)
    {
        var loc = t.Locations[0];
        var text = File.ReadAllText(loc.Path);
        var start = text.IndexOf('(', loc.Span.Start + loc.Span.Length);
        var depth = 0;
        var end = start;
        for (; end < text.Length; end++)
        {
            depth += text[end] == '(' ? 1 : text[end] == ')' ? -1 : 0;
            if (depth == 0)
            {
                break;
            }
        }

        var head = text[(loc.Span.Start + loc.Span.Length)..start];
        var keyword = Keyword().Match(head).Value.ToLowerInvariant();
        var inner = Spaces().Replace(text[(start + 1)..end], " ").Trim();
        inner = Semicolon().Replace(inner, "; ");
        inner = Colon().Replace(inner, ": ");
        inner = Comma().Replace(inner, ", ");
        return $"{keyword}({inner})";
    }

    private static string Squeeze(string text) => Spaces().Replace(text, " ");

    [GeneratedRegex("procedure|function", RegexOptions.IgnoreCase)]
    private static partial Regex Keyword();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\s*;\s*")]
    private static partial Regex Semicolon();

    [GeneratedRegex(@"\s*:\s*")]
    private static partial Regex Colon();

    [GeneratedRegex(@"\s*,\s*")]
    private static partial Regex Comma();
}
