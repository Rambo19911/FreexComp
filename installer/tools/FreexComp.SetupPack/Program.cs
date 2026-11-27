using FreexComp.Setup;

// SetupPack: the packaging steps of scripts/build-installer.ps1, in the same code the setup program reads them with.
//   copy-list <list.txt> <base dir> <target dir>   copies the files a payload list selects (INS-002)
//   setup <program.exe> <payload dir> <out.exe>     writes payload.sha256 into the payload, then the setup exe (INS-003, INS-004)
try
{
    switch (args)
    {
        case ["copy-list", var list, var from, var to]:
            Console.WriteLine($"{PayloadList.Copy(list, from, to)} files from {list}");
            return 0;
        case ["setup", var program, var payload, var output]:
            PackageWriter.WriteManifest(payload);
            PackageWriter.WriteSetup(program, payload, output);
            Console.WriteLine($"{output}: {new FileInfo(output).Length} bytes, {PackageWriter.PayloadFiles(payload).Count} payload files");
            return 0;
        default:
            Console.Error.WriteLine("usage: SetupPack copy-list <list> <base> <target> | setup <program> <payload> <output>");
            return 2;
    }
}
catch (Exception e) when (e is IOException or InvalidDataException or InvalidOperationException or UnauthorizedAccessException)
{
    Console.Error.WriteLine("SetupPack: " + e.Message);
    return 1;
}
