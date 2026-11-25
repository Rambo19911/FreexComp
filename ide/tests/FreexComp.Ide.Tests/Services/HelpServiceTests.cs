using FreexComp.Ide.Core.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Services;

/// <summary>I-257: the user help pages of the repository's <c>help</c> folder are shipped next to the IDE.</summary>
[Trait("Phase", "8")]
public sealed class HelpServiceTests
{
    /// <summary>
    /// Building the IDE copies every <c>help/**/*.md</c> to <c>help</c> below its output directory, and to the output of
    /// a project that references it (this one: the directory the IDE of the UI tests runs from), byte for byte.
    /// </summary>
    [Fact(Timeout = 30000)]
    [Trait("Req", "UI-MENU-031")]
    public void HelpPagesAreDeployed()
    {
        var root = SpecTables.RepoRootPath();
        var source = Path.Combine(root, "help");
        var pages = Directory.EnumerateFiles(source, "*.md", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(source, f)).ToList();
        Assert.True(pages.Count >= 3, "the repository has help pages");

        // <this project>/bin/<configuration>/<framework>/ -> the same place of the IDE project.
        var own = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var ideOutput = Path.Combine(root, "ide", "src", "FreexComp.Ide", "bin",
            Path.GetFileName(Path.GetDirectoryName(own)!), Path.GetFileName(own));
        Assert.True(File.Exists(Path.Combine(ideOutput, "FreexComp.Ide.exe")), ideOutput + ": not the output directory of the IDE");

        foreach (var output in (string[])[ideOutput, own])
        {
            foreach (var page in pages)
            {
                var deployed = Path.Combine(output, "help", page);
                Assert.True(File.Exists(deployed), deployed + " is missing");
                Assert.Equal(File.ReadAllBytes(Path.Combine(source, page)), File.ReadAllBytes(deployed));
            }
        }
    }
}
