namespace FreexComp.Setup.Tests;

[Trait("Phase", "8")]
public sealed class CommandLineTests
{
    private static readonly Func<string, string?> NoVariables = _ => null;
    private static readonly Func<string, string?> TestVariableSet = name => name == CommandLine.TestVariable ? "1" : null;

    [Fact]
    [Trait("Req", "INS-005")]
    public void Parse()
    {
        Assert.Equal(new SetupOptions(), CommandLine.Parse([], NoVariables));
        Assert.Equal(new SetupOptions { Silent = true }, CommandLine.Parse(["/S"], NoVariables));
        Assert.Equal(new SetupOptions { Silent = true }, CommandLine.Parse(["/s"], NoVariables));
        Assert.Equal(new SetupOptions { TargetDirectory = @"C:\Tools\Free Comp" }, CommandLine.Parse([@"/D=C:\Tools\Free Comp\"], NoVariables));
        Assert.Equal(new SetupOptions { AllUsers = true }, CommandLine.Parse(["/allusers"], NoVariables));
        Assert.Equal(new SetupOptions { NoShortcuts = true }, CommandLine.Parse(["/noshortcuts"], NoVariables));
        Assert.Equal(new SetupOptions { NoAssociations = true }, CommandLine.Parse(["/NOASSOC"], NoVariables));
        Assert.Equal(new SetupOptions { Uninstall = true }, CommandLine.Parse(["/uninstall"], NoVariables));
        Assert.Equal(new SetupOptions { VerifyOnly = true }, CommandLine.Parse(["/verify"], NoVariables));
        Assert.Equal(
            new SetupOptions { Silent = true, TargetDirectory = @"D:\fx", AllUsers = true, NoShortcuts = true, NoAssociations = true },
            CommandLine.Parse(["/S", @"/D=D:\fx", "/allusers", "/noshortcuts", "/noassoc"], NoVariables));

        // the test switches need FREEXCOMP_SETUP_TEST=1
        Assert.Equal(
            new SetupOptions { RegistryRoot = @"Software\FreexComp-SetupTest\x", StartMenuDirectory = @"C:\scratch\menu" },
            CommandLine.Parse([@"/regroot=Software\FreexComp-SetupTest\x", @"/startmenu=C:\scratch\menu"], TestVariableSet));
        foreach (var testSwitch in (string[])[@"/regroot=Software\FreexComp-SetupTest\x", @"/startmenu=C:\scratch\menu"])
        {
            Assert.Equal(ExitCode.BadArguments, Assert.Throws<SetupException>(() => CommandLine.Parse([testSwitch], NoVariables)).Code);
            Assert.Contains(CommandLine.TestVariable, Assert.Throws<SetupException>(() => CommandLine.Parse([testSwitch], name => name == CommandLine.TestVariable ? "0" : null)).Message, StringComparison.Ordinal);
        }

        // bad arguments: unknown, repeated, a relative or empty folder, a bad registry path, a value on a flag, /uninstall with /verify
        foreach (var bad in (string[][])[["/X"], ["install"], ["/S", "/s"], ["/D=relative\\dir"], ["/D="], ["/D"], ["/S=1"], ["/regroot="],
                     [@"/regroot=\Software\X"], [@"/regroot=Software\..\X"], ["/startmenu=menu"], ["/uninstall", "/verify"]])
            Assert.Equal(ExitCode.BadArguments, Assert.Throws<SetupException>(() => CommandLine.Parse(bad, TestVariableSet)).Code);

        Assert.True(CommandLine.IsSilent(["/x", "/s"]));
        Assert.False(CommandLine.IsSilent(["/verify"]));

        // the arguments of an elevated rerun carry every choice, silent
        Assert.Equal(["/S", "/allusers", "/noshortcuts", "/noassoc", @"/D=C:\P\FreexComp"],
            SetupProgram.Arguments(new SetupOptions { AllUsers = true, NoShortcuts = true, NoAssociations = true }, @"C:\P\FreexComp"));
    }

    [Fact]
    [Trait("Req", "INS-005")]
    public void ExitCodes()
    {
        Assert.Equal([0, 1, 2, 3, 4, 5, 6], Enum.GetValues<ExitCode>().Select(c => (int)c));

        using var box = new SetupSandbox();
        var package = box.Package(box.Payload("good", ManifestTests.SmallPayload));
        var environment = box.EnvironmentFor(package);

        // 0 ok, 3 damaged
        Assert.Equal(0, SetupProgram.Run(["/S", "/verify"], environment));
        Assert.Equal(3, SetupProgram.Run(["/S", "/verify"], box.EnvironmentFor(box.FakeProgram())));

        // 2 bad arguments (a test switch without the variable, too)
        Assert.Equal(2, SetupProgram.Run(["/S", "/bogus"], environment));
        var noTestVariable = box.Variables();
        noTestVariable.Remove(CommandLine.TestVariable);
        Assert.Equal(2, SetupProgram.Run(box.Switches(), box.EnvironmentFor(package, variables: noTestVariable)));
        ManifestTests.AssertNothingWritten(box);

        // 1 cancelled: nothing stays
        Assert.Equal(ExitCode.Cancelled, box.RunCancelled(package, box.Switches()).Code);
        ManifestTests.AssertNothingWritten(box);

        // 5 elevation declined (the consent prompt is simulated; a test never elevates)
        var declining = new SetupEnvironment
        {
            ProgramPath = package,
            Variable = environment.Variable,
            Version = "1.0.0",
            IsElevated = false,
            RunElevated = _ => null,
            NotifyShell = false,
        };
        Assert.Equal(5, SetupProgram.Run(box.Switches("/allusers"), declining));
        ManifestTests.AssertNothingWritten(box);

        // 6 I/O error: the target is a file
        File.WriteAllText(box.Home, "a file, not a folder");
        var io = box.Run(package, box.Switches());
        Assert.Equal(ExitCode.IoError, io.Code);
        Assert.Contains(box.Home, io.Message, StringComparison.OrdinalIgnoreCase);
        File.Delete(box.Home);

        // 4 not empty and not a FreexComp install
        Directory.CreateDirectory(box.Home);
        File.WriteAllText(Path.Combine(box.Home, "notes.txt"), "mine");
        Assert.Equal(ExitCode.TargetNotEmpty, box.Run(package, box.Switches()).Code);

        // the real program returns them as its process exit code and logs the result in %TEMP%
        var program = SetupProgramBuild.ProgramPath;
        Assert.Equal(2, SetupProgramBuild.Run(program, ["/S", "/bogus"], box.Variables(), TimeSpan.FromSeconds(60)));
        Assert.Equal(3, SetupProgramBuild.Run(program, ["/S", "/verify"], box.Variables(), TimeSpan.FromSeconds(60)));
        var real = box.Package(box.Payload("real", ManifestTests.SmallPayload), program, "real-setup.exe");
        Assert.Equal(0, SetupProgramBuild.Run(real, ["/S", "/verify"], box.Variables(), TimeSpan.FromSeconds(60)));
        var log = File.ReadAllText(Path.Combine(box.Temp, "FreexComp-setup.log"));
        Assert.Contains("exit 2: Unknown switch: /bogus", log, StringComparison.Ordinal);
        Assert.Contains("exit 3: The installation package is damaged (FreexComp.Setup.exe).", log, StringComparison.Ordinal);
        Assert.Contains("exit 0: The installation package is intact.", log, StringComparison.Ordinal);
    }
}
