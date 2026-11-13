using System.Reflection;

// opc command line (docs/interfaces/compiler-cli.md §1). Only --version is implemented so far (C-001).
if (args is ["--version"])
{
    var version = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
    Console.WriteLine($"opc {version}");
    return 0;
}

Console.WriteLine("usage: opc <main.pas> [options]");
Console.WriteLine("       opc --version | --help");
return 1;
