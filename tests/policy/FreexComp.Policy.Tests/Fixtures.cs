using System.Runtime.InteropServices;

namespace FreexComp.Policy.Tests.Fixtures;

/// <summary>
/// Scanner fixture (NET-011, NET-002): uses HTTP types in a field, in a nested type and in a lambda. Nothing here is
/// ever executed; the network policy tests read this assembly's metadata only.
/// </summary>
internal sealed class UsesHttp
{
    public System.Net.Http.HttpClient? Client = null;

    public static Func<object?> Factory() => () => System.Net.Http.HttpMethod.Get;

    internal sealed class Inner
    {
        public System.Net.Http.HttpRequestMessage? Request = null;
    }
}

/// <summary>Scanner fixture (NET-011, §3.1 kind <c>native</c>): a platform import from a network module. Never called.</summary>
internal static class UsesNativeSocket
{
    [DllImport("ws2_32.dll")]
    internal static extern int WSAGetLastError();
}
