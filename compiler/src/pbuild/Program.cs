using FreexComp.Build;
using FreexComp.Compiler;
using FreexComp.PBuild;

// pbuild command line (compiler-cli.md §4): all logic lives in PbuildCommand so tests run it in-process.
Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();                                                                    // CMP-803: the engine reports E9010 and returns
};

var environment = new PbuildEnvironment(Environment.CurrentDirectory, Console.Out, Console.Error, PhysicalFileSystem.Instance)
{
    TrustFilePath = ProjectTrust.DefaultStorePath(Environment.GetEnvironmentVariable),   // CMP-1247: the list the IDE writes
};
return PbuildCommand.Run(args, environment, cancel.Token);
