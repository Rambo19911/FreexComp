using System.Text;

namespace FreexComp.Policy.Tests;

/// <summary>
/// Programs built with the product belong to their author (LIC-050…066): the runtime source files carry their licence
/// line, the list <c>tests/policy/user-output-components.txt</c> names everything that is linked into, generated into
/// or deployed beside such a program, with a licence of the allow-list, and the help page tells the user so.
/// </summary>
[Trait("Phase", "8")]
public sealed class CommercialUseTests
{
    private static readonly string ListFile = RepoPaths.Combine("tests", "policy", CommercialUse.ListName);
    private static readonly string NoticesFile = RepoPaths.Combine("THIRD-PARTY-NOTICES.md");
    private static readonly string HelpPage = RepoPaths.Combine("help", "your-applications.md");

    private static string Problems(IEnumerable<string> problems) => string.Join("\n", problems);

    private static List<CommercialUse.Entry> ListEntries()
    {
        Assert.True(File.Exists(ListFile), "tests/policy/" + CommercialUse.ListName + " is missing");
        return CommercialUse.ReadList(ListFile, RepoPaths.Root).Entries;
    }

    /// <summary>A temporary home with the five runtime source roots (empty).</summary>
    private static DirectoryInfo TempHome()
    {
        var temp = Directory.CreateTempSubdirectory("policy-commercial-");
        foreach (var root in CommercialUse.SourceRoots)
        {
            temp.CreateSubdirectory(root);
        }

        return temp;
    }

    private static string CopyToTemp(DirectoryInfo temp, string relative)
    {
        var target = Path.Combine([temp.FullName, .. relative.Split('/')]);
        File.Copy(RepoPaths.Combine(relative.Split('/')), target);
        return target;
    }

    [Fact]
    [Trait("Req", "LIC-050")]
    [Trait("Req", "LIC-051")]
    [Trait("Req", "LIC-053")]
    public void RuntimeSourcesCarryTheLicenceLine()
    {
        var problems = CommercialUse.LicenceLineProblems(RepoPaths.Root);
        Assert.True(problems.Count == 0, Problems(problems));

        // the roots are not empty: the check looked at files of every kind
        foreach (var root in CommercialUse.SourceRoots)
        {
            Assert.NotEmpty(Directory.GetFiles(RepoPaths.Combine(root.Split('/')), root.EndsWith("/def", StringComparison.Ordinal) ? "*.def" : "*.pas"));
        }
    }

    [Fact]
    [Trait("Req", "LIC-050")]
    public void LicenceLineCheckReportsAMissingLine()
    {
        var temp = TempHome();
        try
        {
            var removed = CopyToTemp(temp, "rtl/src/SysUtils.pas");
            var blank = CopyToTemp(temp, "rtl/winapi/def/kernel32.def");
            CopyToTemp(temp, "ui-framework/src/Forms.pas");
            CopyToTemp(temp, "rtl/src/unit-aliases.json");
            Assert.Empty(CommercialUse.LicenceLineProblems(temp.FullName));

            var text = File.ReadAllText(removed);
            Assert.StartsWith(CommercialUse.SourceLine, text, StringComparison.Ordinal);
            File.WriteAllText(removed, text[(text.IndexOf('\n', StringComparison.Ordinal) + 1)..]);
            text = File.ReadAllText(blank);
            Assert.StartsWith(CommercialUse.DefLine, text, StringComparison.Ordinal);
            File.WriteAllText(blank, text.Insert(CommercialUse.DefLine.Length, " "));

            Assert.Equal(
                (string[])
                [
                    "rtl/src/SysUtils.pas: first line is not the licence line",
                    "rtl/winapi/def/kernel32.def: first line is not the licence line",
                ],
                CommercialUse.LicenceLineProblems(temp.FullName));

            // LIC-053: a file of another kind in a root is reported, the alias table is not
            File.WriteAllText(Path.Combine(temp.FullName, "data", "src", "Notes.txt"), "x\n");
            Assert.Contains("data/src/Notes.txt: file type is not allowed in a runtime source root", CommercialUse.LicenceLineProblems(temp.FullName));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "LIC-054")]
    public void RuntimeFoldersAreClassified()
    {
        var problems = CommercialUse.FolderProblems(RepoPaths.Root);
        Assert.True(problems.Count == 0, Problems(problems));

        // the names found are classified names, and every area has its runtime source
        foreach (var area in CommercialUse.Areas)
        {
            var found = CommercialUse.FoldersOf(RepoPaths.Combine(area));
            Assert.Contains("src", found);
            Assert.Empty(found.Except(CommercialUse.ClassifiedFolders(area)));
        }

        Assert.Equal((string[])["def", "src"], CommercialUse.FoldersOf(RepoPaths.Combine("rtl", "winapi")));

        var temp = TempHome();
        try
        {
            // build folders and directories without files do not count
            temp.CreateSubdirectory("rtl/tests").CreateSubdirectory("bin");
            File.WriteAllText(Path.Combine(temp.FullName, "rtl", "tests", "T.pas"), "");
            File.WriteAllText(Path.Combine(temp.FullName, "rtl", "src", "A.pas"), "");
            temp.CreateSubdirectory("rtl/obj");
            File.WriteAllText(Path.Combine(temp.FullName, "rtl", "obj", "x.txt"), "");
            temp.CreateSubdirectory("data/empty");
            Assert.Empty(CommercialUse.FolderProblems(temp.FullName));

            temp.CreateSubdirectory("rtl/extras");
            File.WriteAllText(Path.Combine(temp.FullName, "rtl", "extras", "X.pas"), "");
            temp.CreateSubdirectory("ui-framework/winapi/src");
            File.WriteAllText(Path.Combine(temp.FullName, "ui-framework", "winapi", "src", "Y.pas"), "");
            temp.CreateSubdirectory("rtl/winapi/gen");
            File.WriteAllText(Path.Combine(temp.FullName, "rtl", "winapi", "gen", "Z.pas"), "");
            Assert.Equal(
                (string[])
                [
                    "rtl/extras: directory is not classified (runtime source, or tests, samples, design, tools)",
                    "ui-framework/winapi: directory is not classified (runtime source, or tests, samples, design, tools)",
                    "rtl/winapi/gen: only the runtime source roots src and def are allowed here",
                ],
                CommercialUse.FolderProblems(temp.FullName));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Fact]
    [Trait("Req", "LIC-055")]
    public void ProjectTemplatesCarryNoLicenceLine()
    {
        var templates = RepoPaths.Combine("ide", "src", "FreexComp.Ide.Core", "Templates");
        var files = Directory.GetFiles(templates, "*", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        var marked = files.Where(f => File.ReadAllText(f).Contains("SPDX-License-Identifier", StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(RepoPaths.Root, f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
        Assert.True(marked.Count == 0, "a new project is the user's own work; licence line found in: " + string.Join(", ", marked));
    }

    [Fact]
    [Trait("Req", "LIC-056")]
    [Trait("Req", "LIC-057")]
    [Trait("Req", "LIC-061")]
    public void UserOutputListIsWellFormed()
    {
        Assert.True(File.Exists(ListFile), "tests/policy/" + CommercialUse.ListName + " is missing");
        var (entries, problems) = CommercialUse.ReadList(ListFile, RepoPaths.Root);
        Assert.True(problems.Count == 0, Problems(problems));
        Assert.Empty(CommercialUse.LicenceProblems(entries));
        Assert.EndsWith("\n", File.ReadAllText(ListFile), StringComparison.Ordinal);

        // LIC-061: the product's own parts first, in the stated order, then the two components of other origin
        Assert.Equal(
            (string[])[.. CommercialUse.SourceRoots, CommercialUse.CompilerOutput],
            entries.Where(e => e.Kind == "own").Select(e => e.Name));
        Assert.Equal(
            (string[])["LLVM|Apache-2.0 WITH LLVM-exception", "SQLite|Public domain"],
            entries.Where(e => e.Kind == "third-party").Select(e => e.Name + "|" + e.Licence));
        Assert.Contains(entries, e => e.Kind == "system");

        // the reader itself: each broken line is reported with its line number
        var temp = Directory.CreateTempSubdirectory("policy-commercial-");
        try
        {
            temp.CreateSubdirectory("rtl/src");
            var list = Path.Combine(temp.FullName, CommercialUse.ListName);
            File.WriteAllText(list, string.Join('\n',
                "# comment",
                "",
                "own | rtl/src | Apache-2.0 WITH LLVM-exception | compiled into the program",
                "own | compiler output | Apache-2.0 WITH LLVM-exception | generated",
                "own | rtl/gone | Apache-2.0 WITH LLVM-exception | compiled into the program",
                "own | rtl/src | MIT | compiled into the program",
                "system | Kernel32.dll | \u2014 | imported",
                "system | user32.dll | MIT | imported",
                "bundled | Thing | MIT | copied",
                "third-party | Thing | MIT",
                "third-party |Thing| MIT | copied",
                "third-party | Other | MIT | ",
                ""));
            var (read, broken) = CommercialUse.ReadList(list, temp.FullName);
            Assert.Equal(9, read.Count);
            Assert.Equal(
                (string[])
                [
                    "user-output-components.txt(5): 'rtl/gone' is neither a repository path nor 'compiler output'",
                    "user-output-components.txt(6): the licence of the own entry 'rtl/src' must be 'Apache-2.0 WITH LLVM-exception'",
                    "user-output-components.txt(7): 'Kernel32.dll' is not a library file name in lower case",
                    "user-output-components.txt(8): the licence cell of the system entry 'user32.dll' must be '\u2014'",
                    "user-output-components.txt(9): kind 'bundled' is not one of own, third-party, system",
                    "user-output-components.txt(10): expected '<kind> | <name> | <licence> | <how it reaches the program>'",
                    "user-output-components.txt(11): four cells, none empty, separated by ' | ' without other blanks around them",
                    "user-output-components.txt(12): four cells, none empty, separated by ' | ' without other blanks around them",
                    "user-output-components.txt(6): 'rtl/src' is listed twice",
                ],
                broken);

            File.WriteAllBytes(list, [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("# x\r\n")]);
            Assert.Equal(
                (string[])
                [
                    "user-output-components.txt: starts with a byte order mark",
                    "user-output-components.txt: contains a carriage return (LF line ends only)",
                ],
                CommercialUse.ReadList(list, temp.FullName).Problems);
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    [Theory]
    [Trait("Req", "LIC-058")]
    [InlineData("GPL-3.0-only", false)]
    [InlineData("LGPL-2.1-only", false)]
    [InlineData("Apache-2.0", false)]
    [InlineData("MPL-2.0", false)]
    [InlineData("", false)]
    [InlineData("see the supplier's page", false)]
    [InlineData("mit", false)]
    [InlineData("Apache-2.0 WITH LLVM-exception", true)]
    [InlineData("MIT", true)]
    [InlineData("BSD-2-Clause", true)]
    [InlineData("BSD-3-Clause", true)]
    [InlineData("Zlib", true)]
    [InlineData("ISC", true)]
    [InlineData("Public domain", true)]
    public void DisallowedLicenceIsReported(string licence, bool allowed)
    {
        var temp = Directory.CreateTempSubdirectory("policy-commercial-");
        try
        {
            var list = Path.Combine(temp.FullName, CommercialUse.ListName);
            File.WriteAllText(list, "# a list with one component of another origin\n"
                + "system | kernel32.dll | \u2014 | imported by every program\n"
                + $"third-party | Contoso Codec | {licence} | `codec.dll` is deployed beside the program\n");
            var problems = CommercialUse.LicenceProblems(CommercialUse.ReadList(list, temp.FullName).Entries);
            if (allowed)
            {
                Assert.Empty(problems);
            }
            else
            {
                Assert.Equal(
                    (string[])[$"user-output-components.txt(3): licence '{licence}' of 'Contoso Codec' is not allowed in user programs"],
                    problems);
            }
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    /// <summary>LIC-058: the allow-list is the decided one; a change here is an owner decision.</summary>
    [Fact]
    [Trait("Req", "LIC-058")]
    public void AllowListIsTheDecidedOne()
    {
        Assert.Equal(
            (string[])["Apache-2.0 WITH LLVM-exception", "MIT", "BSD-2-Clause", "BSD-3-Clause", "Zlib", "ISC", "Public domain"],
            CommercialUse.AllowedLicences);
    }

    [Fact]
    [Trait("Req", "LIC-059")]
    public void ThirdPartyEntriesMatchTheNoticesTable()
    {
        var entries = ListEntries();
        Assert.Contains(entries, e => e.Kind == "third-party");
        var problems = CommercialUse.NoticesProblems(entries, NoticesFile);
        Assert.True(problems.Count == 0, Problems(problems));

        // the check itself: an entry without a row and an entry whose licence differs from its row are reported
        CommercialUse.Entry[] changed =
        [
            new(1, "third-party", "LLVM", "MIT", "x"),
            new(2, "third-party", "Contoso Codec", "MIT", "x"),
            new(3, "third-party", "SQL", "Public domain", "x"),
            new(4, "third-party", "SQLite", "Public domain", "x"),
        ];
        Assert.Equal(
            (string[])
            [
                "user-output-components.txt(1): licence 'MIT' of 'LLVM' differs from 'Apache-2.0 WITH LLVM-exception' in THIRD-PARTY-NOTICES.md",
                "user-output-components.txt(2): 'Contoso Codec' has no row in THIRD-PARTY-NOTICES.md, section 'Shipped with the product'",
                "user-output-components.txt(3): 'SQL' has no row in THIRD-PARTY-NOTICES.md, section 'Shipped with the product'",
            ],
            CommercialUse.NoticesProblems(changed, NoticesFile));
    }

    [Fact]
    [Trait("Req", "LIC-060")]
    public void EveryLibraryNamedByTheRuntimeIsListed()
    {
        var entries = ListEntries();
        var problems = CommercialUse.CompletenessProblems(entries, RepoPaths.Root);
        Assert.True(problems.Count == 0, Problems(problems));

        var temp = TempHome();
        try
        {
            CopyToTemp(temp, "rtl/winapi/def/kernel32.def");
            File.WriteAllText(Path.Combine(temp.FullName, "rtl", "src", "Loader.pas"),
                "unit Loader;\ninterface\nconst\n  A = 'kernel32.dll';\n  B = 'C:\\Tools\\ZLIB1.dll';\n  C = 'sqlite3.dll';\n  D = 'not a library.dllx';\nimplementation\nend.\n");
            File.WriteAllText(Path.Combine(temp.FullName, "rtl", "winapi", "def", "winmm.def"), "; x\nLIBRARY winmm.dll\nEXPORTS\n  timeGetTime\n");
            CommercialUse.Entry[] list =
            [
                .. CommercialUse.SourceRoots.Where(r => r != "data/src").Select((r, i) => new CommercialUse.Entry(i + 1, "own", r, CommercialUse.RuntimeLicence, "x")),
                new(5, "own", "rtl/tools", CommercialUse.RuntimeLicence, "x"),
                new(6, "own", CommercialUse.CompilerOutput, CommercialUse.RuntimeLicence, "x"),
                new(7, "third-party", "SQLite", "Public domain", "`sqlite3.dll` is deployed beside the program"),
                new(8, "system", "kernel32.dll", CommercialUse.NoLicence, "x"),
                new(9, "system", "gdi32.dll", CommercialUse.NoLicence, "x"),
            ];
            Assert.Equal(
                (string[])
                [
                    "runtime source root data/src is not listed as an own entry",
                    "user-output-components.txt(5): own entry 'rtl/tools' is not a runtime source root",
                    "zlib1.dll is named by rtl/src/Loader.pas but not listed",
                    "winmm.dll is the LIBRARY of rtl/winapi/def/winmm.def but not listed as a system entry",
                    "user-output-components.txt(9): system entry 'gdi32.dll' is not named by any runtime source file",
                ],
                CommercialUse.CompletenessProblems(list, temp.FullName));
        }
        finally
        {
            temp.Delete(recursive: true);
        }
    }

    /// <summary>LIC-063: the heading or the start of the sentence that carries each of the seven statements of the page.</summary>
    private static readonly string[] StatementMarkers =
    [
        "\n## Your rights\n- You may sell your programs",
        "\n## Why this is so\n",
        "They are published under the Apache License 2.0 with the LLVM exception.",
        "\n## What you still have to do\n- If you pass on the **library source code itself**",
        "SQLite is in the public domain",
        "\n## Windows libraries\nYour program calls libraries that are part of Windows.",
        "\n## Where the licence texts are\nThe `licenses` folder of the installation",
        "Help \u203A Third-Party Licenses",
        "It is information, not legal advice.",
    ];

    [Fact]
    [Trait("Req", "LIC-062")]
    [Trait("Req", "LIC-063")]
    [Trait("Req", "LIC-064")]
    [Trait("Req", "LIC-066")]
    public void HelpPageStatesTheRights()
    {
        Assert.True(File.Exists(HelpPage), "help/your-applications.md is missing");
        var text = File.ReadAllText(HelpPage);
        Assert.StartsWith("# What you may do with your applications\n", text, StringComparison.Ordinal);
        foreach (var marker in StatementMarkers)
        {
            Assert.True(text.Contains(marker, StringComparison.Ordinal), "the page does not contain: " + marker.Trim().Replace("\n", " / "));
        }

        // LIC-064: one row per own and third-party entry, in file order, with the entry's licence
        var rows = CommercialUse.TableAfter(HelpPage, "## What becomes part of your program");
        Assert.NotEmpty(rows);
        Assert.Equal((string[])["Part", "Licence", "How it gets there"], rows[0].Select(c => c.Column));
        Assert.Equal(
            ListEntries().Where(e => e.Kind is "own" or "third-party").Select(e => e.Name + " | " + e.Licence),
            rows.Select(r => CommercialUse.Cell(r, "Part")!.Trim('`') + " | " + CommercialUse.Cell(r, "Licence")));
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(CommercialUse.Cell(r, "How it gets there")), "empty cell in the row of " + CommercialUse.Cell(r, "Part")));

        // LIC-066: the two library pages lead here
        foreach (var page in (string[])["libraries.md", "library-source.md"])
        {
            Assert.Contains("What you may do with programs you build: see *What you may do with your applications*.",
                File.ReadAllText(RepoPaths.Combine("help", page)), StringComparison.Ordinal);
        }
    }

    [Fact]
    [Trait("Req", "LIC-065")]
    public void HelpPageIsDeliveredWithTheIde()
    {
        Assert.True(File.Exists(HelpPage), "help/your-applications.md is missing");
        var output = ProductOutputs.OutputDirectory("FreexComp.Ide");
        Assert.True(File.Exists(Path.Combine(output, "FreexComp.Ide.dll")), "FreexComp.Ide is not built: build the solution first");
        var copy = Path.Combine(output, "help", "your-applications.md");
        Assert.True(File.Exists(copy), $"{copy} is missing");
        Assert.Equal(File.ReadAllBytes(HelpPage), File.ReadAllBytes(copy));
    }
}
