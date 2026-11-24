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
        // A folder node is shown the same way: its parent folder opens with the folder selected.
        return (File.Exists(filePath) || Directory.Exists(filePath)) && StartExplorer($"/select,\"{Path.GetFullPath(filePath)}\"");
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

    public bool OpenConsole(string directory, out string reason)
    {
        using var process = StartConsole(directory, [], hidden: false, out reason);
        return process is not null;
    }

    /// <summary>
    /// IDE-PM-141. The interpreter is started through the shell so that it gets a console window of its own (also when
    /// the IDE itself was started from a console) and is no child of any job of the IDE; it has no arguments and the
    /// IDE's environment. <paramref name="arguments"/> and <paramref name="hidden"/> are for the test only (an
    /// interpreter that exits at once, without a window). Null on failure, with the OS reason.
    /// </summary>
    internal static Process? StartConsole(string directory, IReadOnlyList<string> arguments, bool hidden, out string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        reason = "";
        if (!Directory.Exists(directory))
        {
            // The shell would start the interpreter in another directory instead of failing.
            reason = new Win32Exception(ErrorDirectory).Message;
            return null;
        }

        var interpreter = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(interpreter))
            interpreter = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot") ?? @"C:\Windows", "System32", "cmd.exe");
        try
        {
            var start = new ProcessStartInfo(interpreter)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetFullPath(directory),
                WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal,
            };
            foreach (var argument in arguments)
                start.ArgumentList.Add(argument);
            var process = Process.Start(start);
            if (process is null)
                reason = new Win32Exception(ErrorFileNotFound).Message;
            return process;
        }
        catch (Exception e) when (e is Win32Exception or IOException or InvalidOperationException)
        {
            reason = e.Message;
            return null;
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
    private const int ErrorDirectory = 267; // "The directory name is invalid."
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
