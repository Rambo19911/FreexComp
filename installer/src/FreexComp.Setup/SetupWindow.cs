using System.Diagnostics;

namespace FreexComp.Setup;

/// <summary>
/// The setup window (INS-006): one page with the install folder, the scope, the shortcut and association choices, the
/// licence line (its link opens <c>H\licenses</c> after the install), Install / Cancel, a progress bar and the result line.
/// </summary>
internal sealed class SetupWindow : Form
{
    private readonly SetupOptions _options;
    private readonly SetupEnvironment _environment;
    private readonly Installer _installer;
    private readonly TextBox _target = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 3, 6, 3) };
    private readonly Button _browse = new() { Text = "Browse…", AutoSize = true };
    private readonly RadioButton _forMe = new() { Text = "Install for me", AutoSize = true, Checked = true };
    private readonly RadioButton _forAll = new() { Text = "Install for all users (needs administrator rights)", AutoSize = true };
    private readonly CheckBox _shortcut = new() { Text = "Start menu shortcut", AutoSize = true };
    private readonly CheckBox _associations = new() { Text = "File associations (.pproj, .pgroup, .pas, .form)", AutoSize = true };
    private readonly LinkLabel _licences = new() { AutoSize = true, MaximumSize = new Size(520, 0) };
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Maximum = 1000 };
    private readonly Label _result = new() { AutoSize = true, MaximumSize = new Size(520, 0), Text = "" };
    private readonly Button _install = new() { Text = "Install", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true };
    private CancellationTokenSource? _running;
    private string? _installedHome;

    private SetupWindow(SetupOptions options, SetupEnvironment environment)
    {
        _options = options;
        _environment = environment;
        _installer = new Installer(environment);
        ExitCode = ExitCode.Cancelled;

        Text = SetupProgram.Title;
        Font = SystemFonts.MessageBoxFont ?? Font;
        AutoScaleMode = AutoScaleMode.Font;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = true;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = System.Drawing.Icon.ExtractAssociatedIcon(environment.ProgramPath);

        _forAll.Checked = options.AllUsers;
        _forMe.Checked = !options.AllUsers;
        _shortcut.Checked = !options.NoShortcuts;
        _associations.Checked = !options.NoAssociations;
        _target.Text = options.TargetDirectory ?? _installer.DefaultTarget(options.AllUsers);
        _target.MinimumSize = new Size(380, 0);

        const string licenceText = "FreexComp is free software under the Apache License 2.0. The bundled tools keep their own licences; "
                                   + "their texts are in licenses\\ after the installation.";
        _licences.Text = licenceText;
        _licences.LinkArea = new LinkArea(licenceText.IndexOf("licenses\\", StringComparison.Ordinal), "licenses\\".Length);
        _licences.Links[0].Enabled = false;

        var header = new Label
        {
            Text = $"Install FreexComp {environment.Version}",
            AutoSize = true,
            Font = new Font(Font.FontFamily, Font.Size * 1.4f, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 10),
        };
        var folderRow = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        folderRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        folderRow.Controls.Add(_target, 0, 0);
        folderRow.Controls.Add(_browse, 1, 0);
        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 10, 0, 0) };
        buttons.Controls.Add(_cancel);
        buttons.Controls.Add(_install);

        var page = new TableLayoutPanel { ColumnCount = 1, AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(16) };
        page.Controls.Add(header);
        page.Controls.Add(new Label { Text = "Install folder:", AutoSize = true });
        page.Controls.Add(folderRow);
        page.Controls.Add(_forMe);
        page.Controls.Add(_forAll);
        page.Controls.Add(_shortcut);
        page.Controls.Add(_associations);
        page.Controls.Add(new Label { Height = 6, AutoSize = false });
        page.Controls.Add(_licences);
        page.Controls.Add(_progress);
        page.Controls.Add(_result);
        page.Controls.Add(buttons);
        Controls.Add(page);

        AcceptButton = _install;
        CancelButton = _cancel;
        _browse.Click += (_, _) => Browse();
        _forMe.CheckedChanged += (_, _) => ScopeChanged();
        _install.Click += async (_, _) => await InstallAsync();
        _cancel.Click += (_, _) => CancelOrClose();
        _licences.LinkClicked += (_, _) => OpenLicences();
        FormClosing += (_, e) =>
        {
            if (_running is null)
                return;
            _running.Cancel();
            e.Cancel = true;   // closes when the run has rolled back
        };
    }

    public ExitCode ExitCode { get; private set; }

    public static int RunWindow(SetupOptions options, SetupEnvironment environment)
    {
        try
        {
            using (SetupPackage.Open(environment.ProgramPath))
            {
                // the trailer is there; the full check runs when Install is clicked
            }
        }
        catch (SetupException e)
        {
            var damaged = new SetupResult(e.Code, e.Message);
            SetupProgram.Log(environment, damaged);
            Report(damaged);
            return (int)e.Code;
        }

        using var window = new SetupWindow(options, environment);
        Application.Run(window);
        return (int)window.ExitCode;
    }

    public static bool Confirm(string question) =>
        MessageBox.Show(question, SetupProgram.Title, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    public static void Report(SetupResult result) =>
        MessageBox.Show(result.Message, SetupProgram.Title, MessageBoxButtons.OK, result.Code == ExitCode.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Error);

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog { Description = "Install folder", UseDescriptionForTitle = true, ShowNewFolderButton = true };
        if (Directory.Exists(_target.Text))
            dialog.InitialDirectory = _target.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _target.Text = dialog.SelectedPath;
    }

    private void ScopeChanged()
    {
        var other = _installer.DefaultTarget(!_forAll.Checked);
        if (string.Equals(_target.Text, other, StringComparison.OrdinalIgnoreCase))
            _target.Text = _installer.DefaultTarget(_forAll.Checked);
    }

    private async Task InstallAsync()
    {
        var target = _target.Text.Trim();
        if (target.Length == 0 || !Path.IsPathFullyQualified(target))
        {
            _result.Text = "Enter an absolute folder path, for example " + _installer.DefaultTarget(false) + ".";
            return;
        }

        var options = _options with
        {
            TargetDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)),
            AllUsers = _forAll.Checked,
            NoShortcuts = !_shortcut.Checked,
            NoAssociations = !_associations.Checked,
        };
        SetInputsEnabled(false);
        _result.Text = "";
        _running = new CancellationTokenSource();
        var progress = new Progress<InstallProgress>(p =>
        {
            _progress.Value = (int)Math.Clamp(p.Fraction * 1000, 0, 1000);
            _result.Text = p.Step;
        });
        var token = _running.Token;
        var result = await Task.Run(() => SetupProgram.Execute(options, _environment, null, progress, token));
        _running.Dispose();
        _running = null;
        SetupProgram.Log(_environment, result);
        ExitCode = result.Code;
        _result.Text = result.Message;
        if (result.Code == ExitCode.Ok)
        {
            _installedHome = options.TargetDirectory;
            _progress.Value = _progress.Maximum;
            _licences.Links[0].Enabled = true;
            _install.Visible = false;
            _cancel.Text = "Close";
            _cancel.Enabled = true;
            AcceptButton = _cancel;
        }
        else
        {
            _progress.Value = 0;
            SetInputsEnabled(true);
        }
    }

    private void CancelOrClose()
    {
        if (_running is not null)
        {
            _running.Cancel();
            _cancel.Enabled = false;
            return;
        }

        Close();
    }

    private void OpenLicences()
    {
        if (_installedHome is null || !Directory.Exists(Path.Combine(_installedHome, "licenses")))
            return;
        using var _ = Process.Start(new ProcessStartInfo(Path.Combine(_installedHome, "licenses")) { UseShellExecute = true });
    }

    private void SetInputsEnabled(bool enabled)
    {
        foreach (var control in (Control[])[_target, _browse, _forMe, _forAll, _shortcut, _associations, _install])
            control.Enabled = enabled;
        _cancel.Enabled = true;
    }
}
