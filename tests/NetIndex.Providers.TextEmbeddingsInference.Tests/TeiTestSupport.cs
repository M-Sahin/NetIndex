using System.Net;
using System.Text;
using NetIndex.Core.Abstractions;
using NetIndex.Providers.TextEmbeddingsInference.Options;

namespace NetIndex.Providers.TextEmbeddingsInference.Tests;

internal sealed class FakeTimeProvider : TimeProvider
{
    private DateTimeOffset _now = new(2026, 10, 10, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

    public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        => _respond = respond;

    public int Calls { get; private set; }

    public string? LastBody { get; private set; }

    public Uri? LastUri { get; private set; }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        LastUri = request.RequestUri;
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return await _respond(request, cancellationToken);
    }

    public static FakeHandler Json(string json, HttpStatusCode status = HttpStatusCode.OK)
        => new((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }));

    public static FakeHandler Status(HttpStatusCode status)
        => new((_, _) => Task.FromResult(new HttpResponseMessage(status)));

    public static FakeHandler Throw(Exception ex)
        => new((_, _) => throw ex);
}

internal static class TeiFactory
{
    public static TeiRerankerOptions Options(Action<TeiRerankerOptions>? configure = null)
    {
        var o = new TeiRerankerOptions { Endpoint = "http://reranker:80/" };
        configure?.Invoke(o);
        return o;
    }

    public static TeiDocumentReranker Create(
        FakeHandler handler, TimeProvider? clock = null, Action<TeiRerankerOptions>? configure = null)
        => new(new HttpClient(handler), Options(configure), clock ?? TimeProvider.System);

    public static SearchResult<RagChunk> Result(string id, string text, float score = 0.1f)
        => new(new RagChunk(id, text, null, "doc-" + id, null), score, "doc-" + id);

    public static List<SearchResult<RagChunk>> Results(int n)
        => Enumerable.Range(0, n).Select(i => Result($"c{i}", $"text {i}", 1f - i * 0.01f)).ToList();
}
