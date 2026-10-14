using System.Net;
using System.Net.Sockets;
using System.Text;
using ContextBudget.Application;
using ContextBudget.Infrastructure;

namespace ContextBudget.Tests;

public sealed class ProviderTests
{
    [Fact]
    public async Task ParsesOpenAICompatibleResponseAndMapsAllRolesAndUsage()
    {
        const string payload = """{"model":"actual-model","choices":[{"message":{"content":"hello"},"finish_reason":"stop"}],"usage":{"prompt_tokens":11,"completion_tokens":4,"prompt_tokens_details":{"cached_tokens":3}}}""";
        var handler = new StubHandler((request, _) =>
        {
            Assert.Equal("Bearer secret-value", request.Headers.Authorization!.ToString());
            return Task.FromResult(Json(HttpStatusCode.OK, payload));
        });
        var provider = CreateProvider(handler);
        Assert.DoesNotContain("secret-value", System.Text.Json.JsonSerializer.Serialize(provider.GetConfiguration()));
        var result = await provider.CompleteAsync(new LLMRequest("test-model", [
            new("system", "rules"), new("developer", "format"), new("user", "question"),
            new("assistant", "thinking"), new("tool", "tool result", "call-7")
        ], 0.2, 64), CancellationToken.None);

        Assert.Equal("SUCCESS", result.Status);
        Assert.Equal("actual-model", result.Model);
        Assert.Equal("hello", result.Content);
        Assert.Equal(new ProviderUsage(11, 4, 3), result.Usage);
        Assert.Equal("stop", result.FinishReason);
        Assert.Contains("\"tool_call_id\":\"call-7\"", handler.LastBody);
        Assert.Contains("\"role\":\"developer\"", handler.LastBody);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "AuthenticationError")]
    [InlineData(HttpStatusCode.Forbidden, "Forbidden")]
    [InlineData(HttpStatusCode.TooManyRequests, "RateLimit")]
    [InlineData(HttpStatusCode.NotFound, "InvalidModel")]
    public async Task CategorizesProviderHttpErrorsWithoutEchoingBody(HttpStatusCode status, string category)
    {
        var provider = CreateProvider(new StubHandler((_, _) => Task.FromResult(Json(status, "upstream secret secret-value"))));
        var result = await provider.TestConnectionAsync(CancellationToken.None);
        Assert.Equal(category, result.Error!.Category);
        Assert.DoesNotContain("secret-value", result.Error.Message);
    }

    [Fact]
    public async Task MissingKeyDoesNotSendRequestOrRevealSecret()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("Should not send"));
        var provider = CreateProvider(handler, apiKey: " ");
        var result = await provider.TestConnectionAsync(CancellationToken.None);
        Assert.Equal("MissingApiKey", result.Error!.Category);
        Assert.False(provider.GetConfiguration().ApiKeyConfigured);
    }

    [Fact]
    public async Task MissingUsageIsUnknownAndMalformedOrInvalidResponsesAreCategorized()
    {
        var noUsage = CreateProvider(new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"ok"},"finish_reason":"stop"}]}"""))));
        Assert.Null((await noUsage.TestConnectionAsync(CancellationToken.None)).Usage);

        var malformed = CreateProvider(new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{"))));
        Assert.Equal("InvalidResponse", (await malformed.TestConnectionAsync(CancellationToken.None)).Error!.Category);
        var wrongShape = CreateProvider(new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{}"))));
        Assert.Equal("InvalidResponse", (await wrongShape.TestConnectionAsync(CancellationToken.None)).Error!.Category);
        var arrayShape = CreateProvider(new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "[]"))));
        Assert.Equal("InvalidResponse", (await arrayShape.TestConnectionAsync(CancellationToken.None)).Error!.Category);
    }

    [Fact]
    public async Task InvalidModelErrorAndReflectedSecretAreHandledSafely()
    {
        var invalidModel = CreateProvider(new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest,
            """{"error":{"code":"model_not_found","message":"secret-value"}}"""))));
        var result = await invalidModel.TestConnectionAsync(CancellationToken.None);
        Assert.Equal("InvalidModel", result.Error!.Category);
        Assert.DoesNotContain("secret-value", result.Error.Message);

        var reflected = CreateProvider(new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            """{"choices":[{"message":{"content":"echo secret-value"},"finish_reason":"stop"}]}"""))));
        var safe = await reflected.CompleteAsync(new("test-model", [new("user", "hello")]), CancellationToken.None);
        Assert.Equal("echo [REDACTED]", safe.Content);
    }

    [Fact]
    public async Task OversizedResponseIsRejected()
    {
        var provider = CreateProvider(new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK,
            "{" + new string(' ', 4096) + "}"))), maxResponseBytes: 128);
        Assert.Equal("InvalidResponse", (await provider.TestConnectionAsync(CancellationToken.None)).Error!.Category);
    }

    [Fact]
    public async Task TimeoutIsMappedAndCallerCancellationPropagates()
    {
        var handler = new StubHandler(async (_, ct) => { await Task.Delay(Timeout.InfiniteTimeSpan, ct); return Json(HttpStatusCode.OK, "{}"); });
        var timeoutProvider = CreateProvider(handler, timeout: TimeSpan.FromMilliseconds(100));
        Assert.Equal("Timeout", (await timeoutProvider.TestConnectionAsync(CancellationToken.None)).Error!.Category);

        var cancelProvider = CreateProvider(handler);
        using var cts = new CancellationTokenSource(30);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelProvider.TestConnectionAsync(cts.Token));
    }

    [Fact]
    public async Task UnsupportedRolesAndToolMessagesWithoutIdsAreRejected()
    {
        var provider = CreateProvider(new StubHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{}"))));
        await Assert.ThrowsAsync<ProviderRequestException>(() => provider.CompleteAsync(new("model", [new("system_prompt", "x")]), CancellationToken.None));
        await Assert.ThrowsAsync<ProviderRequestException>(() => provider.CompleteAsync(new("model", [new("tool", "x")]), CancellationToken.None));
        var roleError = await Assert.ThrowsAsync<ProviderRequestException>(() => provider.CompleteAsync(new("model", [new("secret-value", "x")]), CancellationToken.None));
        Assert.DoesNotContain("secret-value", roleError.Message);
    }

    [Fact]
    public void ProviderUrlRequiresExactHttpsAllowlistedOriginAndNoRedirectsOrProxy()
    {
        var options = Options();
        var policy = new ProviderConnectionPolicy(options, new FakeDns(IPAddress.Parse("8.8.8.8")));
        var handler = policy.CreateHttpHandler();
        Assert.False(handler.AllowAutoRedirect);
        Assert.False(handler.UseProxy);
        Assert.NotNull(handler.ConnectCallback);
        Assert.Null(handler.SslOptions.RemoteCertificateValidationCallback);
        Assert.True(ProviderConnectionPolicy.IsPublicAddress(IPAddress.Parse("2001:4860:4860::8888")));
        Assert.Throws<ProviderRequestException>(() => new ProviderConnectionPolicy(Options(baseUrl: "http://provider.example/v1"), new FakeDns(IPAddress.Parse("8.8.8.8"))).ValidateConfiguredBaseUrl());
        Assert.Throws<ProviderRequestException>(() => new ProviderConnectionPolicy(Options(baseUrl: "https://not-allowed.example/v1"), new FakeDns(IPAddress.Parse("8.8.8.8"))).ValidateConfiguredBaseUrl());
        Assert.Throws<ProviderRequestException>(() => new ProviderConnectionPolicy(Options(baseUrl: "https://provider.example/v10"), new FakeDns(IPAddress.Parse("8.8.8.8"))).ValidateConfiguredBaseUrl());
    }

    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.2.3.4")]
    [InlineData("169.254.10.20")]
    [InlineData("::1")]
    [InlineData("fc00::1")]
    [InlineData("fe80::1")]
    [InlineData("::ffff:127.0.0.1")]
    public async Task PrivateLoopbackAndIpv6AddressesAreBlocked(string address)
    {
        var ip = IPAddress.Parse(address);
        Assert.False(ProviderConnectionPolicy.IsPublicAddress(ip));
        var policy = new ProviderConnectionPolicy(Options(), new FakeDns(ip));
        await Assert.ThrowsAsync<ProviderRequestException>(() => policy.ValidateDestinationAsync(new Uri("https://provider.example/v1/chat/completions"), CancellationToken.None));
    }

    [Fact]
    public async Task RealSocketsHandlerRejectsDnsRebindingAtConnectTime()
    {
        var dns = new SequenceDns(IPAddress.Parse("8.8.8.8"), IPAddress.Loopback);
        var policy = new ProviderConnectionPolicy(Options(), dns);
        using var client = new HttpClient(policy.CreateHttpHandler()) { Timeout = TimeSpan.FromSeconds(2) };
        var provider = new OpenAICompatibleProvider(client, Options(), policy);
        var result = await provider.TestConnectionAsync(CancellationToken.None);
        Assert.NotEqual("SUCCESS", result.Status);
        Assert.Equal("ConnectionError", result.Error!.Category);
        Assert.True(dns.Calls >= 2);
    }

    private static OpenAICompatibleProvider CreateProvider(StubHandler handler, string apiKey = "secret-value", int maxResponseBytes = 4096, TimeSpan? timeout = null)
    {
        var options = Options(apiKey: apiKey, maxResponseBytes: maxResponseBytes);
        var policy = new ProviderConnectionPolicy(options, new FakeDns(IPAddress.Parse("8.8.8.8")));
        return new(new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(2) }, options, policy);
    }

    private static LLMProviderOptions Options(string baseUrl = "https://provider.example/v1", string apiKey = "secret-value", int maxResponseBytes = 4096) => new()
    {
        BaseUrl = baseUrl, Model = "test-model", ProviderName = "Test Provider", ApiKey = apiKey,
        AllowedHosts = ["provider.example"], MaxResponseBytes = maxResponseBytes
    };

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public string LastBody { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return await callback(request, cancellationToken);
        }
    }

    private sealed class FakeDns(IPAddress address) : IProviderDnsResolver
    {
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken) => Task.FromResult(new[] { address });
    }

    private sealed class SequenceDns(IPAddress first, IPAddress next) : IProviderDnsResolver
    {
        public int Calls { get; private set; }
        public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new[] { Calls == 1 ? first : next });
        }
    }
}
