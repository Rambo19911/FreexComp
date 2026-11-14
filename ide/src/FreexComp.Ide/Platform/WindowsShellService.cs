using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using FreexComp.Ide.Core.Platform;

namespace FreexComp.Ide.Platform;

/// <summary>
/// <see cref="IShellService"/> over the Windows shell: the file manager is started as <c>explorer.exe</c>; the
/// Recycle Bin is reached through <c>SHFileOperationW</c> with undo enabled and no UI except the warning shown when
/// a file cannot be recycled and would be deleted permanently.
/// </summary>
public sealed class WindowsShellService : IShellService
{
    public bool OpenFolder(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        return Directory.Exists(directory) && StartExplorer($"\"{Path.GetFullPath(directory)}\"");
    }

    public bool RevealFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        return File.Exists(filePath) && StartExplorer($"/select,\"{Path.GetFullPath(filePath)}\"");
    }

    public bool MoveToRecycleBin(string path) => MoveToRecycleBin(path, out _);

    public bool MoveToRecycleBin(string path, out string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("The path must be absolute.", nameof(path));
        }

        reason = "";
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            reason = new Win32Exception(ErrorFileNotFound).Message;
            return false;
        }

        var from = Marshal.StringToHGlobalUni(path + "\0"); // double-NUL-terminated list of one path
        try
        {
            var operation = new ShFileOpStruct
            {
                Func = FoDelete,
                From = from,
                Flags = FofAllowUndo | FofNoConfirmation | FofNoErrorUi | FofSilent | FofWantNukeWarning,
            };
            var code = SHFileOperationW(ref operation);
            if (code == 0 && !operation.AnyOperationsAborted)
            {
                return true;
            }

            reason = new Win32Exception(code != 0 ? code : ErrorCancelled).Message;
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(from);
        }
    }

    private static bool StartExplorer(string arguments)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("explorer.exe", arguments) { UseShellExecute = false });
            return process is not null;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private const int ErrorFileNotFound = 2;
    private const int ErrorCancelled = 1223;
    private const uint FoDelete = 0x0003;
    private const ushort FofSilent = 0x0004;
    private const ushort FofNoConfirmation = 0x0010;
    private const ushort FofAllowUndo = 0x0040;
    private const ushort FofNoErrorUi = 0x0400;
    private const ushort FofWantNukeWarning = 0x4000;

    // SHFILEOPSTRUCTW, 64-bit layout (ADR 007: Windows x64 only).
    [StructLayout(LayoutKind.Sequential)]
    private struct ShFileOpStruct
    {
        public IntPtr Hwnd;
        public uint Func;
        public IntPtr From;
        public IntPtr To;
        public ushort Flags;
        private int _anyOperationsAborted;
        public IntPtr NameMappings;
        public IntPtr ProgressTitle;

        public readonly bool AnyOperationsAborted => _anyOperationsAborted != 0;
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    private static extern int SHFileOperationW(ref ShFileOpStruct operation);
}
