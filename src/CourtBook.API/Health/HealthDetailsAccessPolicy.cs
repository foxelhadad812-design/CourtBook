using System.Net;

namespace CourtBook.API.Health;

/// <summary>
/// Access policy for detailed health diagnostics. Secrets are accepted only from
/// the X-Health-Key header, never from query strings.
/// </summary>
public static class HealthDetailsAccessPolicy
{
    public static bool IsAuthorized(
        string? requiredKey,
        string? providedHeaderKey,
        IPAddress? remoteAddress,
        IPAddress? localAddress)
    {
        var isKeyAuthorized = !string.IsNullOrWhiteSpace(requiredKey) &&
                              string.Equals(providedHeaderKey, requiredKey, StringComparison.Ordinal);

        var isLocal = remoteAddress != null &&
                      (IPAddress.IsLoopback(remoteAddress) ||
                       remoteAddress.Equals(localAddress));

        return isKeyAuthorized || isLocal;
    }
}
