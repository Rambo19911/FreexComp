using System.Reflection;

// pbuild command line (docs/interfaces/compiler-cli.md §4). Only --version is implemented so far (C-001).
if (args is ["--version"])
{
    var version = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";
    Console.WriteLine($"pbuild {version}");
    return 0;
}

Console.WriteLine("usage: pbuild <project.pproj | group.pgroup> [--config Debug|Release] [--target build|rebuild|clean|run]");
Console.WriteLine("              [--diagnostics text|json] [--verbose]");
Console.WriteLine("       pbuild --version | --help");
return 1;
