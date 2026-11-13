using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Tests.Infrastructure;

namespace FreexComp.Ide.Core.Tests.Resources;

[Trait("Phase", "1")]
public sealed class MessageDefinitionsTests
{
    [Fact(Timeout = 30000)]
    [Trait("Category", "Spec")]
    [Trait("Req", "UI-DLG-020")]
    [Trait("Req", "UI-DLG-022")]
    [Trait("Req", "UI-DLG-024")]
    public void DialogRowsMatchSpec()
    {
        var problems = new List<string>();
        var specIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (file, heading) in MessageCatalogTests.MessageTables)
        {
            foreach (var row in SpecTables.Read(SpecTables.DocPath(file), heading))
            {
                var kind = row["Kind"];
                if (!kind.StartsWith("Dialog (", StringComparison.Ordinal))
                {
                    continue;
                }

                var id = row["Id"].Trim();
                specIds.Add(id);
                if (MessageDefinitions.TryGet(id) is not { } definition)
                {
                    problems.Add($"{id}: no definition");
                    continue;
                }

                var expectedKind = kind["Dialog (".Length..^1];
                if (!string.Equals(definition.Kind.ToString(), expectedKind, StringComparison.OrdinalIgnoreCase))
                {
                    problems.Add($"{id}: kind {definition.Kind}, spec {expectedKind}");
                }

                // "Break, Continue, check box "…"": only the buttons count.
                var buttons = row.TryGetValue("Buttons (default first)", out var first) ? first : row["Buttons"];
                var labels = buttons.Split(", ").TakeWhile(l => !l.StartsWith("check box", StringComparison.Ordinal)).ToList();
                var ids = labels.Select(MessageDefinitions.ButtonId).ToList();
                if (!ids.SequenceEqual(definition.Buttons))
                {
                    problems.Add($"{id}: buttons [{string.Join(", ", definition.Buttons)}], spec [{string.Join(", ", ids)}]");
                }

                foreach (var (label, buttonId) in labels.Zip(ids))
                {
                    if (MessageDefinitions.ButtonLabel(buttonId) != label)
                    {
                        problems.Add($"Button_{buttonId}: \"{MessageDefinitions.ButtonLabel(buttonId)}\", spec \"{label}\"");
                    }
                }
            }
        }

        problems.AddRange(MessageDefinitions.All.Select(d => d.Id).Where(id => !specIds.Contains(id)).Select(id => $"{id}: defined but not a Dialog row of the spec"));
        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems.Distinct()));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-024")]
    public void ButtonIdIsTheLabelInPascalCaseLetters()
    {
        Assert.Equal("DontSave", MessageDefinitions.ButtonId("Don't Save"));
        Assert.Equal("SaveAs", MessageDefinitions.ButtonId("Save As…"));
        Assert.Equal("RunLastBuild", MessageDefinitions.ButtonId("Run Last Build"));
        Assert.Equal("OK", MessageDefinitions.ButtonId("OK"));
        Assert.Equal("SaveAsUTF8", MessageDefinitions.ButtonId("Save as UTF-8"));
    }

    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-023")]
    public void TitleIsTheTitleResourceOrTheProductName()
    {
        Assert.Equal(Strings.Msg_File_SaveChangesOne_Title, MessageDefinitions.Get("Msg.File.SaveChangesOne").Title);
        Assert.Equal("Save Changes", Strings.Msg_File_SaveChangesOne_Title);
        Assert.Equal(Strings.App_ProductName, MessageDefinitions.Get("Msg.File.NotFound").Title);
    }
}
