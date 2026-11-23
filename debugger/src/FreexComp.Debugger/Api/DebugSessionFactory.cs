using FreexComp.Debugger.Session;

namespace FreexComp.Debugger;

/// <summary>The session factory of the library.</summary>
public sealed class DebugSessionFactory : IDebugSessionFactory
{
    public DebugSessionFactory()
    {
    }

    /// <inheritdoc />
    public IDebugSession Create(DebugSessionOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new DebugSession(options);
    }
}
