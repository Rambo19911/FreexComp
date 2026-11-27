using System.Globalization;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using FreexComp.Ide.Core.Dialogs;
using FreexComp.Ide.Core.Menus;
using FreexComp.Ide.Core.Resources;
using FreexComp.Ide.Core.ViewModels;
using FreexComp.Ide.Tests.Infrastructure;

namespace FreexComp.Ide.Tests.Views;

/// <summary>
/// The Project Options pages <c>Resources</c> and <c>Version Info</c>, the field <c>Icon</c> of <c>Application</c> and
/// Project › Resources (resource-manager.md UI-DLG-330…335, task I-336).
/// </summary>
[Trait("Phase", "8")]
[Trait("Kind", "Ui")]
public sealed class ProjectOptionsResourcesTests
{
    private static async Task<HeadlessHarness> StartAsync(string projectExtra = "") =>
        await BuildScenario.StartAsync(projectExtra: projectExtra, implementedPhase: null);

    private static string ProjectDir(HeadlessHarness h) => Path.GetDirectoryName(BuildScenario.ProjectPath(h))!;

    private static string Write(HeadlessHarness h, string relative, byte[] data)
    {
        var path = Path.Combine(ProjectDir(h), relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return path;
    }

    private static async Task<(DialogDriver Dialog, Task Opening)> OpenAsync(HeadlessHarness h, string command = "project.options")
    {
        var opening = h.Services.Commands.ExecuteAsync(command);
        return (await h.WaitForDialogAsync("ProjectOptions"), opening);
    }

    private static void ShowPage(DialogDriver dlg, string pageId)
    {
        var pages = dlg.Find<ListBox>("Pages");
        pages.SelectedItem = pages.Items.Cast<ProjectOptionsPage>().Single(p => p.Id == pageId);
        Dispatcher.UIThread.RunJobs();
    }

    private static ListBox Grid(DialogDriver dlg) => dlg.Find<ListBox>("ResourceList");

    private static IReadOnlyList<ResourceRow> Rows(DialogDriver dlg) => [.. Grid(dlg).Items.Cast<ResourceRow>()];

    /// <summary>The cells of the grid as shown: name, type, file.</summary>
    private static List<string> Cells(DialogDriver dlg) =>
    [
        .. Rows(dlg).Select((_, i) => string.Join("|",
            dlg.Find<TextBox>($"ResourceList.Row.{i}.Name").Text,
            dlg.Find<ComboBox>($"ResourceList.Row.{i}.Type").SelectedItem,
            dlg.Find<TextBlock>($"ResourceList.Row.{i}.File").Text)),
    ];

    private static void Select(DialogDriver dlg, params int[] rows)
    {
        var grid = Grid(dlg);
        grid.SelectedItems!.Clear();
        foreach (var row in rows)
            grid.SelectedItems.Add(Rows(dlg)[row]);
        Dispatcher.UIThread.RunJobs();
    }

    private static void SetText(DialogDriver dlg, string field, string text)
    {
        dlg.Find<TextBox>(field).Text = text;
        Dispatcher.UIThread.RunJobs();
    }

    private static JsonElement ReadProject(HeadlessHarness h) => JsonDocument.Parse(File.ReadAllText(BuildScenario.ProjectPath(h))).RootElement;

    private static async Task CloseAsync(DialogDriver dlg, Task opening, string button = "Ok")
    {
        dlg.Click(button);
        await opening;
    }

    private static bool PageHasError(DialogDriver dlg, string page) => dlg.Find<Avalonia.Controls.Shapes.Ellipse>("Page." + page + ".Error").IsVisible;

    /// <summary>A bitmap file of <paramref name="width"/> × <paramref name="height"/>, 24 bits per pixel.</summary>
    internal static byte[] Bitmap(int width, int height)
    {
        var stride = ((width * 3) + 3) / 4 * 4;
        var data = new byte[14 + 40 + (stride * height)];
        data[0] = (byte)'B';
        data[1] = (byte)'M';
        BitConverter.TryWriteBytes(data.AsSpan(2), data.Length);
        BitConverter.TryWriteBytes(data.AsSpan(10), 54);
        BitConverter.TryWriteBytes(data.AsSpan(14), 40);
        BitConverter.TryWriteBytes(data.AsSpan(18), width);
        BitConverter.TryWriteBytes(data.AsSpan(22), height);
        BitConverter.TryWriteBytes(data.AsSpan(26), (short)1);
        BitConverter.TryWriteBytes(data.AsSpan(28), (short)24);
        return data;
    }

    /// <summary>An icon (<paramref name="kind"/> 1) or cursor (2) file with one 32-bit frame per size, in the given order.</summary>
    internal static byte[] Icon(int kind, params int[] sizes)
    {
        var frames = sizes.Select(s =>
        {
            var frame = new byte[40 + (s * s * 4) + (((s + 31) / 32 * 4) * s)];
            BitConverter.TryWriteBytes(frame.AsSpan(0), 40);
            BitConverter.TryWriteBytes(frame.AsSpan(4), s);
            BitConverter.TryWriteBytes(frame.AsSpan(8), s * 2);
            BitConverter.TryWriteBytes(frame.AsSpan(12), (short)1);
            BitConverter.TryWriteBytes(frame.AsSpan(14), (short)32);
            return frame;
        }).ToList();
        var data = new List<byte>(new byte[6 + (16 * sizes.Length)]);
        BitConverter.TryWriteBytes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(data)[2..], (short)kind);
        BitConverter.TryWriteBytes(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(data)[4..], (short)sizes.Length);
        for (var i = 0; i < sizes.Length; i++)
        {
            var entry = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(data).Slice(6 + (16 * i), 16);
            entry[0] = entry[1] = (byte)(sizes[i] % 256);
            BitConverter.TryWriteBytes(entry[4..], (short)1);
            BitConverter.TryWriteBytes(entry[6..], (short)32);
            BitConverter.TryWriteBytes(entry[8..], frames[i].Length);
            BitConverter.TryWriteBytes(entry[12..], data.Count);
            data.AddRange(frames[i]);
        }

        return [.. data];
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-332")]
    public async Task AddSetsTypeAndName()
    {
        await using var h = await StartAsync("""
            , "resources": [ { "path": "data/old.txt", "type": "RCDATA", "name": "MY_DATA" } ]
            """);
        string[] files =
        [
            Write(h, @"data\my data.txt", "x"u8.ToArray()),
            Write(h, @"art\logo.bmp", Bitmap(2, 2)),
            Write(h, @"art\app.ico", Icon(1, 16)),
            Write(h, @"art\second.ICO", Icon(1, 16)),
            Write(h, @"art\pointer.cur", Icon(2, 32)),
            Write(h, @"data\1st-part.bin", [1, 2]),
            Write(h, @"data\42.dat", [1]),
        ];
        var outside = Path.Combine(h.TempDir, "shared.txt");
        File.WriteAllText(outside, "s");
        var (dlg, opening) = await OpenAsync(h);
        ShowPage(dlg, "Resources");
        Assert.Equal(["MY_DATA|RCDATA|data/old.txt"], Cells(dlg));

        h.FileDialogs.NextOpen([.. files, outside]);
        dlg.Click("ResourceList.Add");
        await h.WaitForAsync(() => Rows(dlg).Count == 9, "files added");

        // Multi-select picker over all files, starting in the project folder.
        var request = h.FileDialogs.Requests[^1];
        Assert.True(request.AllowMultiple);
        Assert.Equal(ProjectDir(h), request.InitialFolder);
        Assert.Equal(
        [
            "MY_DATA|RCDATA|data/old.txt",
            "MY_DATA_2|RCDATA|data/my data.txt", // the name of the entry before gets _2
            "LOGO|BITMAP|art/logo.bmp",
            "2|ICON|art/app.ico",                // icons are numbered from 2 (1 is the application icon)
            "3|ICON|art/second.ICO",
            "POINTER|CURSOR|art/pointer.cur",
            "_1ST_PART|RCDATA|data/1st-part.bin", // no valid name as it is: a leading _
            "42|RCDATA|data/42.dat",              // a number 1…65535 is a name
            "SHARED|RCDATA|" + outside,            // outside the project folder: absolute
        ], Cells(dlg));
        Assert.Same(Rows(dlg)[^1], Grid(dlg).SelectedItem);
        Assert.Null(dlg.ErrorText("ResourceList"));
        Assert.True(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
        Assert.True(dlg.Find<Button>("Apply").IsEffectivelyEnabled);

        // The same files again: every name is taken now.
        h.FileDialogs.NextOpen(files[0], files[2]);
        dlg.Click("ResourceList.Add");
        await h.WaitForAsync(() => Rows(dlg).Count == 11, "files added again");
        Assert.Equal(["MY_DATA_3|RCDATA|data/my data.txt", "4|ICON|art/app.ico"], Cells(dlg)[9..]);

        // A canceled picker adds nothing.
        h.FileDialogs.NextCancel();
        dlg.Click("ResourceList.Add");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(11, Rows(dlg).Count);

        await CloseAsync(dlg, opening);
        var saved = ReadProject(h).GetProperty("resources");
        Assert.Equal(11, saved.GetArrayLength());
        Assert.Equal(("art/pointer.cur", "CURSOR", "POINTER"),
            (saved[5].GetProperty("path").GetString(), saved[5].GetProperty("type").GetString(), saved[5].GetProperty("name").GetString()));
        Assert.Equal(outside.Replace('\\', '/'), saved[8].GetProperty("path").GetString());
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-332")]
    public async Task EditValidatesNameAndDuplicate()
    {
        await using var h = await StartAsync("""
            , "resources": [ { "path": "a.txt", "type": "RCDATA", "name": "ALPHA" },
                             { "path": "b.txt", "type": "RCDATA", "name": "BETA" },
                             { "path": "c.ico", "type": "ICON", "name": "2" } ]
            """);
        var (dlg, opening) = await OpenAsync(h);
        ShowPage(dlg, "Resources");
        Assert.False(PageHasError(dlg, "Resources"));

        // Not an identifier and no number 1…65535.
        foreach (var bad in (string[])["9x", "", "0", "65536", "A-B", new string('N', 65)])
        {
            SetText(dlg, "ResourceList.Row.1.Name", bad);
            Assert.Equal(Strings.Msg_ProjectOptions_ResourceNameInvalid, dlg.ErrorText("ResourceList"));
            Assert.True(dlg.Find<TextBox>("ResourceList.Row.1.Name").Classes.Contains("invalid"), bad);
            Assert.Equal(Strings.Msg_ProjectOptions_ResourceNameInvalid, ToolTip.GetTip(dlg.Find<TextBox>("ResourceList.Row.1.Name")));
            Assert.True(PageHasError(dlg, "Resources"));
            Assert.False(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
        }

        // The same type and name as an earlier entry: identifiers ignore case. The later entry carries the error.
        SetText(dlg, "ResourceList.Row.1.Name", "alpha");
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_ProjectOptions_ResourceDuplicate, "alpha", "a.txt"), dlg.ErrorText("ResourceList"));
        Assert.DoesNotContain("invalid", dlg.Find<TextBox>("ResourceList.Row.0.Name").Classes);
        Assert.False(dlg.Find<Button>("Ok").IsEffectivelyEnabled);

        // Another type: no duplicate.
        dlg.Find<ComboBox>("ResourceList.Row.1.Type").SelectedItem = "BITMAP";
        Dispatcher.UIThread.RunJobs();
        Assert.Null(dlg.ErrorText("ResourceList"));
        Assert.False(PageHasError(dlg, "Resources"));
        Assert.Equal(["RCDATA", "ICON", "BITMAP", "CURSOR"], dlg.Find<ComboBox>("ResourceList.Row.1.Type").Items.Cast<string>());

        // Numbers compare by value; icon group 1 belongs to the application icon (CMP-1431).
        SetText(dlg, "ResourceList.Row.0.Name", "002");
        dlg.Find<ComboBox>("ResourceList.Row.0.Type").SelectedItem = "ICON";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_ProjectOptions_ResourceDuplicate, "2", "a.txt"), dlg.ErrorText("ResourceList"));
        Assert.Contains("invalid", dlg.Find<TextBox>("ResourceList.Row.2.Name").Classes);
        SetText(dlg, "ResourceList.Row.0.Name", "01");
        Assert.Null(dlg.ErrorText("ResourceList")); // no application icon: an ICON entry may take 1
        ShowPage(dlg, "Application");
        Write(h, "app.ico", Icon(1, 16));
        SetText(dlg, "Icon", "app.ico");
        ShowPage(dlg, "Resources");
        Assert.Equal(string.Format(CultureInfo.CurrentCulture, Strings.Msg_ProjectOptions_ResourceDuplicate, "01", "app.ico"), dlg.ErrorText("ResourceList"));
        Assert.False(PageHasError(dlg, "Application"));
        Assert.True(PageHasError(dlg, "Resources"));

        // Valid again: the edited cells are written as typed (trimmed).
        SetText(dlg, "ResourceList.Row.0.Name", " Gamma ");
        Assert.Null(dlg.ErrorText("ResourceList"));
        Assert.True(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
        await CloseAsync(dlg, opening);
        var saved = ReadProject(h).GetProperty("resources");
        Assert.Equal(["Gamma ICON a.txt", "alpha BITMAP b.txt", "2 ICON c.ico"],
            saved.EnumerateArray().Select(e => $"{e.GetProperty("name").GetString()} {e.GetProperty("type").GetString()} {e.GetProperty("path").GetString()}"));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-333")]
    public async Task PreviewTextHexAndImage()
    {
        await using var h = await StartAsync("""
            , "resources": [ { "path": "text.txt", "type": "RCDATA", "name": "T" },
                             { "path": "data.bin", "type": "RCDATA", "name": "B" },
                             { "path": "logo.bmp", "type": "BITMAP", "name": "L" },
                             { "path": "app.ico", "type": "ICON", "name": "2" },
                             { "path": "gone.txt", "type": "RCDATA", "name": "G" },
                             { "path": "long.txt", "type": "RCDATA", "name": "LONG" },
                             { "path": "tab.txt", "type": "RCDATA", "name": "TAB" } ]
            """);
        var text = "﻿Grüße\r\nline\ttwo\n";
        Write(h, "text.txt", Encoding.UTF8.GetBytes(text));
        Write(h, "data.bin", [.. Enumerable.Range(0, 20).Select(i => (byte)(i * 13))]);
        Write(h, "logo.bmp", Bitmap(30, 20));
        Write(h, "app.ico", Icon(1, 16, 48, 32));
        Write(h, "long.txt", Encoding.UTF8.GetBytes(new string('a', 4095) + "ä" + new string('b', 1000)));
        Write(h, "tab.txt", [(byte)'a', 0x07, (byte)'b']);
        var (dlg, opening) = await OpenAsync(h);
        ShowPage(dlg, "Resources");
        var preview = dlg.Find<Border>("ResourcePreview");
        Assert.Equal(160, preview.Height);
        var shownText = dlg.Find<SelectableTextBlock>("ResourcePreview.Text");
        var image = dlg.Find<Image>("ResourcePreview.Image");
        var info = dlg.Find<TextBlock>("ResourcePreview.Info");
        string Info(long size, string type) => string.Format(CultureInfo.CurrentCulture, Strings.ProjectOptions_ResourceInfo, size, type);

        // Nothing selected: an empty preview.
        Assert.False(shownText.IsEffectivelyVisible);
        Assert.False(image.IsEffectivelyVisible);
        Assert.False(info.IsEffectivelyVisible);

        // UTF-8 text with tab / CR / LF: as text, without the byte-order mark.
        Select(dlg, 0);
        Assert.True(shownText.IsEffectivelyVisible);
        Assert.Equal(text[1..], shownText.Text);
        Assert.Equal(Info(Encoding.UTF8.GetByteCount(text), "RCDATA"), info.Text);
        Assert.False(image.IsEffectivelyVisible);

        // Binary data: offset, 16 bytes, ASCII.
        Select(dlg, 1);
        Assert.Equal(
            "00000000  00 0D 1A 27 34 41 4E 5B 68 75 82 8F 9C A9 B6 C3  ...'4AN[hu......\n" +
            "00000010  D0 DD EA F7                                      ....",
            shownText.Text);
        Assert.Equal(Info(20, "RCDATA"), info.Text);

        // Images: the largest frame, fitted into the preview, never above four times its size.
        Select(dlg, 2);
        Assert.True(image.IsEffectivelyVisible);
        Assert.False(shownText.IsEffectivelyVisible);
        Assert.NotNull(image.Source);
        Assert.Equal((120.0, 80.0), (image.MaxWidth, image.MaxHeight));
        Assert.Equal(Stretch.Uniform, image.Stretch);
        Assert.Equal(Info(new FileInfo(Path.Combine(ProjectDir(h), "logo.bmp")).Length, "BITMAP"), info.Text);
        Select(dlg, 3);
        Assert.Equal((192.0, 192.0), (image.MaxWidth, image.MaxHeight)); // 48 × 48 of 16, 48 and 32
        Assert.Equal(Info(new FileInfo(Path.Combine(ProjectDir(h), "app.ico")).Length, "ICON"), info.Text);

        // A missing file.
        Select(dlg, 4);
        Assert.Equal(Strings.Msg_ProjectOptions_ResourceMissing, shownText.Text);
        Assert.False(info.IsEffectivelyVisible);
        Assert.False(image.IsEffectivelyVisible);

        // The first 4 KB only; a character cut at the end is left out.
        Select(dlg, 5);
        Assert.Equal(new string('a', 4095), shownText.Text);

        // A control character other than tab / CR / LF: hex.
        Select(dlg, 6);
        Assert.StartsWith("00000000  61 07 62 ", shownText.Text, StringComparison.Ordinal);

        // The type decides: the bitmap as RCDATA is shown in hex; the text as ICON is no image, so hex as well.
        dlg.Find<ComboBox>("ResourceList.Row.2.Type").SelectedItem = "RCDATA";
        Select(dlg, 2);
        Assert.StartsWith("00000000  42 4D ", shownText.Text, StringComparison.Ordinal);
        Assert.False(image.IsEffectivelyVisible);
        dlg.Find<ComboBox>("ResourceList.Row.6.Type").SelectedItem = "ICON";
        Select(dlg, 6);
        Assert.StartsWith("00000000  61 07 62 ", shownText.Text, StringComparison.Ordinal);
        Assert.Equal(Info(3, "ICON"), info.Text);

        await CloseAsync(dlg, opening, "Cancel");
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-332")]
    [Trait("Req", "UI-DLG-335")]
    public async Task RemoveAndReorderSaveToPproj()
    {
        // Without the keys, an untouched dialog writes none of them.
        await using (var plain = await StartAsync())
        {
            var (first, opened) = await OpenAsync(plain);
            ShowPage(first, "Application");
            SetText(first, "Name", "Hello2");
            await CloseAsync(first, opened);
            var file = ReadProject(plain);
            Assert.Equal("Hello2", file.GetProperty("name").GetString());
            Assert.False(file.TryGetProperty("resources", out _));
            Assert.False(file.TryGetProperty("icon", out _));
            Assert.False(file.TryGetProperty("versionInfo", out _));
        }

        await using var h = await StartAsync("""
            , "resources": [ { "path": "a.txt", "type": "RCDATA", "name": "A" },
                             { "path": "b.txt", "type": "RCDATA", "name": "B", "note": "kept" },
                             { "path": "c.txt", "type": "RCDATA", "name": "C" },
                             { "path": "d.txt", "type": "RCDATA", "name": "D" } ]
            """);
        var (dlg, opening) = await OpenAsync(h);
        ShowPage(dlg, "Resources");

        // Move Up / Move Down act on one selected row; the first row cannot go up, the last not down.
        Select(dlg, 0);
        Assert.False(dlg.Find<Button>("ResourceList.MoveUp").IsEnabled);
        Assert.True(dlg.Find<Button>("ResourceList.MoveDown").IsEnabled);
        Select(dlg, 1);
        dlg.Click("ResourceList.MoveDown");
        Assert.Equal(["A", "C", "B", "D"], Rows(dlg).Select(r => r.Name));
        Assert.Same(Rows(dlg)[2], Grid(dlg).SelectedItem);
        dlg.Click("ResourceList.MoveDown");
        Assert.Equal(["A", "C", "D", "B"], Rows(dlg).Select(r => r.Name));
        Assert.False(dlg.Find<Button>("ResourceList.MoveDown").IsEnabled);
        Select(dlg, 2);
        dlg.Click("ResourceList.MoveUp");
        Assert.Equal(["A", "D", "C", "B"], Rows(dlg).Select(r => r.Name));

        // Remove: the selected rows, no question.
        Select(dlg, 0, 2);
        Assert.False(dlg.Find<Button>("ResourceList.MoveUp").IsEnabled); // two rows selected
        dlg.Click("ResourceList.Remove");
        Assert.Equal(["D", "B"], Rows(dlg).Select(r => r.Name));
        Assert.Equal(["D|RCDATA|d.txt", "B|RCDATA|b.txt"], Cells(dlg));
        Assert.True(dlg.Find<Button>("Apply").IsEffectivelyEnabled);

        // Apply writes the order; the unknown key stays with its entry.
        dlg.Click("Apply");
        Dispatcher.UIThread.RunJobs();
        var json = ReadProject(h);
        var saved = json.GetProperty("resources");
        Assert.Equal(["d.txt", "b.txt"], saved.EnumerateArray().Select(e => e.GetProperty("path").GetString()));
        Assert.Equal("kept", saved[1].GetProperty("note").GetString());
        Assert.False(saved[0].TryGetProperty("note", out _));

        // UI-DLG-335: the entries are no project files; the Project Manager shows nothing new.
        Assert.Equal(2, json.GetProperty("files").GetArrayLength());
        var manager = (ProjectManagerViewModel)h.Services.ToolWindows.Get("ProjectManager")!;
        var names = manager.Roots.SelectMany(r => r.SelfAndDescendants()).Select(n => n.Name).ToList();
        Assert.DoesNotContain(names, n => n.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || n.Contains(".project.rc", StringComparison.OrdinalIgnoreCase));

        // Removing every entry keeps the key, empty (it was in the file).
        Select(dlg, 0, 1);
        dlg.Find<ListBoxItem>("ResourceList.Row.0").Focus();
        dlg.Press("Delete");
        Assert.Empty(Rows(dlg));
        Assert.False(dlg.Find<Button>("ResourceList.Remove").IsEnabled);
        await CloseAsync(dlg, opening);
        Assert.Equal(0, ReadProject(h).GetProperty("resources").GetArrayLength());
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-331")]
    public async Task IconFieldPreviewAndRelativePath()
    {
        await using var h = await StartAsync();
        var inside = Write(h, @"res\app.ico", Icon(1, 16, 32));
        var notIcon = Write(h, @"res\fake.ico", "not an icon"u8.ToArray());
        var outside = Path.Combine(h.TempDir, "other.ico");
        File.WriteAllBytes(outside, Icon(1, 48));
        var (dlg, opening) = await OpenAsync(h);
        ShowPage(dlg, "Application");
        var image = dlg.Find<Image>("Icon.Image");
        Assert.Equal("", dlg.Find<TextBox>("Icon").Text);
        Assert.Null(image.Source);
        Assert.Null(dlg.ErrorText("Icon"));
        Assert.Equal((34.0, 34.0), (dlg.Find<Border>("Icon.Preview").Width, dlg.Find<Border>("Icon.Preview").Height));
        Assert.Equal((32.0, 32.0, StretchDirection.DownOnly), (image.Width, image.Height, image.StretchDirection));

        // Browse: an .ico filter; a file inside the project folder is stored relative with '/'.
        h.FileDialogs.NextOpen(inside);
        dlg.Click("Icon.Browse");
        await h.WaitForAsync(() => dlg.Find<TextBox>("Icon").Text == "res/app.ico", "icon picked");
        Assert.Equal(["*.ico"], h.FileDialogs.Requests[^1].Filters.Single().Patterns);
        Assert.Equal(Strings.ProjectOptions_IconFilter, h.FileDialogs.Requests[^1].Filters.Single().Name);
        Assert.NotNull(image.Source);
        Assert.Null(dlg.ErrorText("Icon"));

        // Outside the project folder: absolute.
        h.FileDialogs.NextOpen(outside);
        dlg.Click("Icon.Browse");
        await h.WaitForAsync(() => dlg.Find<TextBox>("Icon").Text == outside, "outside icon picked");
        Assert.Equal(Path.GetDirectoryName(inside), h.FileDialogs.Requests[^1].InitialFolder); // starts where the icon was

        // A missing file and a file that is no icon: empty frame, the field's error, OK disabled.
        foreach (var bad in (string[])["res/missing.ico", "res/fake.ico"])
        {
            SetText(dlg, "Icon", bad);
            Assert.Equal(Strings.Msg_ProjectOptions_IconInvalid, dlg.ErrorText("Icon"));
            Assert.Null(image.Source);
            Assert.True(PageHasError(dlg, "Application"));
            Assert.False(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
        }

        Assert.True(File.Exists(notIcon));

        // Clear: no icon, no error.
        dlg.Click("Icon.Clear");
        Assert.Equal("", dlg.Find<TextBox>("Icon").Text);
        Assert.Null(dlg.ErrorText("Icon"));
        Assert.True(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
        Assert.False(ReadProject(h).TryGetProperty("icon", out _));

        h.FileDialogs.NextOpen(inside);
        dlg.Click("Icon.Browse");
        await h.WaitForAsync(() => dlg.Find<TextBox>("Icon").Text == "res/app.ico", "icon picked again");
        await CloseAsync(dlg, opening);
        Assert.Equal("res/app.ico", ReadProject(h).GetProperty("icon").GetString());

        // Reopened: the stored icon with its preview; Clear and OK remove the key.
        (dlg, opening) = await OpenAsync(h);
        ShowPage(dlg, "Application");
        Assert.Equal("res/app.ico", dlg.Find<TextBox>("Icon").Text);
        Assert.NotNull(dlg.Find<Image>("Icon.Image").Source);
        dlg.Click("Icon.Clear");
        await CloseAsync(dlg, opening);
        Assert.False(ReadProject(h).TryGetProperty("icon", out _));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-334")]
    public async Task VersionInfoToggleWritesAndRemovesKey()
    {
        await using var h = await StartAsync();
        var (dlg, opening) = await OpenAsync(h);
        ShowPage(dlg, "VersionInfo");
        string[] fields = ["FileVersion", "ProductVersion", "CompanyName", "FileDescription", "ProductName", "LegalCopyright", "Comments"];
        var include = dlg.Find<CheckBox>("IncludeVersionInfo");
        Assert.False(include.IsChecked);
        Assert.All(fields, f => Assert.False(dlg.Find<TextBox>(f).IsEffectivelyEnabled, f));
        Assert.Equal("Hello", dlg.Find<TextBlock>("InternalName").Text);
        Assert.Equal("Hello.exe", dlg.Find<TextBlock>("OriginalFileName").Text);

        // On: the fields are enabled, the file version starts at 1.0.0.0.
        include.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.All(fields, f => Assert.True(dlg.Find<TextBox>(f).IsEffectivelyEnabled, f));
        Assert.Equal("1.0.0.0", dlg.Find<TextBox>("FileVersion").Text);
        Assert.Equal("", dlg.Find<TextBox>("ProductVersion").Text);

        // Versions: 1–4 numbers 0…65535.
        foreach (var bad in (string[])["1.2.3.4.5", "1..2", "65536", "a.b", "1.", " "])
        {
            SetText(dlg, "ProductVersion", bad);
            Assert.Equal(bad.Trim().Length == 0 ? null : Strings.Msg_ProjectOptions_VersionInvalid, dlg.ErrorText("ProductVersion"));
        }

        SetText(dlg, "FileVersion", "1.2.3.4.5");
        Assert.Equal(Strings.Msg_ProjectOptions_VersionInvalid, dlg.ErrorText("FileVersion"));
        Assert.True(PageHasError(dlg, "VersionInfo"));
        Assert.False(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
        SetText(dlg, "FileVersion", "1.2");
        SetText(dlg, "ProductVersion", "");
        Assert.Null(dlg.ErrorText("FileVersion"));
        Assert.False(PageHasError(dlg, "VersionInfo"));
        SetText(dlg, "CompanyName", "Example Ltd");
        SetText(dlg, "LegalCopyright", "(c) 2026");

        // The read-only lines follow the name of the Application page.
        ShowPage(dlg, "Application");
        SetText(dlg, "Name", "Tool");
        ShowPage(dlg, "VersionInfo");
        Assert.Equal("Tool", dlg.Find<TextBlock>("InternalName").Text);
        Assert.Equal("Tool.exe", dlg.Find<TextBlock>("OriginalFileName").Text);
        await CloseAsync(dlg, opening);

        // Only the set keys; no productVersion for the empty field (the empty string is E3123).
        var info = ReadProject(h).GetProperty("versionInfo");
        Assert.Equal(["fileVersion", "companyName", "legalCopyright"], info.EnumerateObject().Select(p => p.Name));
        Assert.Equal(("1.2", "Example Ltd", "(c) 2026"),
            (info.GetProperty("fileVersion").GetString(), info.GetProperty("companyName").GetString(), info.GetProperty("legalCopyright").GetString()));

        // Reopened: on, with the stored values; an invalid field while off does not block OK.
        (dlg, opening) = await OpenAsync(h);
        ShowPage(dlg, "VersionInfo");
        Assert.True(dlg.Find<CheckBox>("IncludeVersionInfo").IsChecked);
        Assert.Equal("1.2", dlg.Find<TextBox>("FileVersion").Text);
        Assert.Equal("Example Ltd", dlg.Find<TextBox>("CompanyName").Text);
        SetText(dlg, "FileVersion", "x");
        dlg.Find<CheckBox>("IncludeVersionInfo").IsChecked = false;
        Dispatcher.UIThread.RunJobs();
        Assert.False(dlg.Find<TextBox>("FileVersion").IsEffectivelyEnabled);
        Assert.False(PageHasError(dlg, "VersionInfo"));
        Assert.True(dlg.Find<Button>("Ok").IsEffectivelyEnabled);
        await CloseAsync(dlg, opening);
        Assert.False(ReadProject(h).TryGetProperty("versionInfo", out _));
    }

    [AvaloniaFact(Timeout = 30000)]
    [Trait("Req", "UI-DLG-330")]
    public async Task ProjectResourcesCommandOpensPage()
    {
        await using var h = await StartAsync();

        // Project menu: the entry before Project Options.
        var project = (SubmenuNode)MainMenuDefinition.TopLevel.Single(n => n is SubmenuNode { Key: "Project" });
        var ids = project.Children.OfType<CommandMenuNode>().Select(c => c.CommandId).ToList();
        Assert.Equal(ids.IndexOf("project.options") - 1, ids.IndexOf("project.resources"));
        Assert.Equal(Strings.Menu_project_resources, MenuLabels.For(project.Children.OfType<CommandMenuNode>().Single(c => c.CommandId == "project.resources")));
        Assert.True(h.Services.Commands.CanExecute("project.resources"));

        // It opens Project Options on the page Resources; the page order is that of UI-DLG-330.
        var (dlg, opening) = await OpenAsync(h, "project.resources");
        var pages = dlg.Find<ListBox>("Pages");
        Assert.Equal("Resources", ((ProjectOptionsPage)pages.SelectedItem!).Id);
        Assert.NotNull(dlg.TryFind<ListBox>("ResourceList"));
        Assert.Equal(["Application", "Resources", "VersionInfo", "Compiler", "SearchPaths", "ConditionalDefines", "Output", "Run", "BuildEvents"],
            pages.Items.Cast<ProjectOptionsPage>().Select(p => p.Id));
        Assert.Equal([Strings.ProjectOptions_Page_Resources, Strings.ProjectOptions_Page_VersionInfo],
            pages.Items.Cast<ProjectOptionsPage>().Skip(1).Take(2).Select(p => p.Title));
        Assert.All(pages.Items.Cast<ProjectOptionsPage>().Take(3), p => Assert.False(p.PerConfiguration));
        Assert.False(dlg.Find<ComboBox>("Configuration").IsEnabled);
        await CloseAsync(dlg, opening, "Cancel");

        // Project Options itself still opens on the remembered page (none chosen here: the first).
        (dlg, opening) = await OpenAsync(h);
        Assert.Equal("Application", ((ProjectOptionsPage)dlg.Find<ListBox>("Pages").SelectedItem!).Id);
        ShowPage(dlg, "VersionInfo");
        await CloseAsync(dlg, opening, "Cancel");
        (dlg, opening) = await OpenAsync(h, "project.resources");
        Assert.Equal("Resources", ((ProjectOptionsPage)dlg.Find<ListBox>("Pages").SelectedItem!).Id);
        await CloseAsync(dlg, opening, "Cancel");

        // Without a project the command is unavailable.
        await h.Services.Commands.ExecuteAsync("file.closeProject");
        Dispatcher.UIThread.RunJobs();
        Assert.False(h.Services.Commands.CanExecute("project.resources"));
    }
}
