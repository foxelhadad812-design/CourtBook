using System.Net;
using CourtBook.API.Health;
using Xunit;

namespace CourtBook.Tests;

public class HealthDetailsAccessPolicyTests
{
    [Fact]
    public void IsAuthorized_ValidHeaderKey_AllowsAccess()
    {
        Assert.True(HealthDetailsAccessPolicy.IsAuthorized(
            "secret-key", "secret-key",
            IPAddress.Parse("203.0.113.10"), IPAddress.Parse("10.0.0.5")));
    }

    [Fact]
    public void IsAuthorized_WrongHeaderKey_RejectsRemoteAccess()
    {
        Assert.False(HealthDetailsAccessPolicy.IsAuthorized(
            "secret-key", "wrong-key",
            IPAddress.Parse("203.0.113.10"), IPAddress.Parse("10.0.0.5")));
    }

    [Fact]
    public void IsAuthorized_MissingHeaderKey_RejectsRemoteAccess()
    {
        Assert.False(HealthDetailsAccessPolicy.IsAuthorized(
            "secret-key", null,
            IPAddress.Parse("203.0.113.10"), IPAddress.Parse("10.0.0.5")));
    }

    [Fact]
    public void IsAuthorized_LoopbackCaller_AllowsAccessWithoutKey()
    {
        Assert.True(HealthDetailsAccessPolicy.IsAuthorized(
            "secret-key", null,
            IPAddress.Loopback, IPAddress.Parse("10.0.0.5")));
    }

    [Fact]
    public void IsAuthorized_MissingConfiguredKey_DoesNotAuthorizeRemoteCaller()
    {
        Assert.False(HealthDetailsAccessPolicy.IsAuthorized(
            null, "secret-key",
            IPAddress.Parse("203.0.113.10"), IPAddress.Parse("10.0.0.5")));
    }
}
