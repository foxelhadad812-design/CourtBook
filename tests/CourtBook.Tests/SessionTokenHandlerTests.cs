using System.Net;
using System.Net.Http.Headers;
using CourtBook.Web.Services;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CourtBook.Tests;

public class SessionTokenHandlerTests
{
    private class TestInnerHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private class TestSession : ISession
    {
        private readonly Dictionary<string, byte[]> _store = new();

        public bool IsAvailable => true;
        public string Id => "test-session";
        public IEnumerable<string> Keys => _store.Keys;

        public void Clear() => _store.Clear();
        public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task LoadAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Remove(string key) => _store.Remove(key);
        public void Set(string key, byte[] value) => _store[key] = value;
        public bool TryGetValue(string key, out byte[] value) => _store.TryGetValue(key, out value!);
    }

    [Fact]
    public async Task SendAsync_AttachesBearerToken_WhenSessionHasToken()
    {
        var httpContext = new DefaultHttpContext();
        var session = new TestSession();
        session.SetString("JwtToken", "test-valid-jwt-token");
        httpContext.Session = session;

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var innerHandler = new TestInnerHandler();
        var handler = new SessionTokenHandler(accessor)
        {
            InnerHandler = innerHandler
        };

        var invoker = new HttpMessageInvoker(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.test/venues");

        await invoker.SendAsync(request, CancellationToken.None);

        Assert.NotNull(innerHandler.LastRequest);
        Assert.NotNull(innerHandler.LastRequest.Headers.Authorization);
        Assert.Equal("Bearer", innerHandler.LastRequest.Headers.Authorization.Scheme);
        Assert.Equal("test-valid-jwt-token", innerHandler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task SendAsync_LeavesAuthorizationNull_WhenGuestSession()
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Session = new TestSession(); // No JwtToken set

        var accessor = new HttpContextAccessor { HttpContext = httpContext };
        var innerHandler = new TestInnerHandler();
        var handler = new SessionTokenHandler(accessor)
        {
            InnerHandler = innerHandler
        };

        var invoker = new HttpMessageInvoker(handler);
        var request = new HttpRequestMessage(HttpMethod.Get, "http://api.test/venues");

        await invoker.SendAsync(request, CancellationToken.None);

        Assert.NotNull(innerHandler.LastRequest);
        Assert.Null(innerHandler.LastRequest.Headers.Authorization);
    }

    [Fact]
    public async Task SendAsync_IsThreadSafe_UnderConcurrentRequests()
    {
        var tasks = Enumerable.Range(1, 20).Select(async i =>
        {
            var ctx = new DefaultHttpContext();
            var sess = new TestSession();
            var token = $"user-token-{i}";
            sess.SetString("JwtToken", token);
            ctx.Session = sess;

            var req = new HttpRequestMessage(HttpMethod.Get, $"http://api.test/venues/{i}");
            var localAccessor = new HttpContextAccessor { HttpContext = ctx };
            var localHandler = new SessionTokenHandler(localAccessor)
            {
                InnerHandler = new TestInnerHandler()
            };
            var localInvoker = new HttpMessageInvoker(localHandler);
            await localInvoker.SendAsync(req, CancellationToken.None);

            Assert.Equal("Bearer", req.Headers.Authorization?.Scheme);
            Assert.Equal(token, req.Headers.Authorization?.Parameter);
        });

        await Task.WhenAll(tasks);
    }
}
