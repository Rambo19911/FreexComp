using FreexComp.Compiler;
using FreexComp.Opc;

// opc command line (compiler-cli.md §1–2): all logic lives in OpcCommand so tests run it in-process.
Console.OutputEncoding = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

return OpcCommand.Run(args, new OpcEnvironment(Environment.CurrentDirectory, Console.Out, Console.Error, PhysicalFileSystem.Instance), cancel.Token);
