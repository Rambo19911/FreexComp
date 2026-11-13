using FreexComp.Ide.Core.Logging;
using FreexComp.Ide.Core.Platform;
using FreexComp.Ide.Core.Services;
using FreexComp.Ide.Core.Settings;
using FreexComp.Ide.Core.Text;

namespace FreexComp.Ide.Core.Composition;

/// <summary>
/// The platform inputs of the service graph (IDE-ARCH-012): implemented over the OS and Avalonia in production
/// (<c>FreexComp.Ide/Platform</c>), by fakes in tests. User paths and the log are created before the graph
/// (IDE-ARCH-015 steps 2–3), so they are inputs too.
/// </summary>
public interface IPlatformServices
{
    IUserPaths UserPaths { get; }

    ILog Log { get; }

    IFileSystem FileSystem { get; }

    IClock Clock { get; }

    IUiDispatcher Dispatcher { get; }

    IClipboardService Clipboard { get; }

    IShellService Shell { get; }

    IFileWatcherFactory FileWatchers { get; }

    IDialogService Dialogs { get; }

    IThemeHost ThemeHost { get; }

    /// <summary>Creates document buffers (IDE-ARCH-031).</summary>
    ITextBufferFactory TextBuffers { get; }
}

/// <summary>Plain holder of the platform inputs.</summary>
public sealed record PlatformServices(
    IUserPaths UserPaths,
    ILog Log,
    IFileSystem FileSystem,
    IClock Clock,
    IUiDispatcher Dispatcher,
    IClipboardService Clipboard,
    IShellService Shell,
    IFileWatcherFactory FileWatchers,
    IDialogService Dialogs,
    IThemeHost ThemeHost,
    ITextBufferFactory TextBuffers) : IPlatformServices;
