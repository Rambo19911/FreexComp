namespace FreexComp.Setup;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        return SetupProgram.Run(args, SetupEnvironment.Current());
    }
}
