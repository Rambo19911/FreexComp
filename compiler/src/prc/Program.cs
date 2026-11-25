using FreexComp.Compiler;
using FreexComp.Prc;

// prc command line (resource scripts): all logic lives in PrcCommand so tests run it in-process.
Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

return PrcCommand.Run(args, new PrcEnvironment(Environment.CurrentDirectory, Console.Out, Console.Error, PhysicalFileSystem.Instance), cancel.Token);
