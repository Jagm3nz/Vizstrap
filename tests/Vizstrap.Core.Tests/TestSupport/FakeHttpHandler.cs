using System.Collections.Concurrent;
using System.Net;

namespace Vizstrap.Core.Tests.TestSupport;

/// <summary>Routes requests by exact URL; anything unknown gets a 404.</summary>
internal sealed class FakeHttpHandler : HttpMessageHandler
{
    private readonly ConcurrentDictionary<string, Func<HttpResponseMessage>> _routes = new();

    public ConcurrentQueue<string> Requests { get; } = new();

    public void Map(string url, Func<HttpResponseMessage> respond) => _routes[url] = respond;

    public void MapText(string url, string text) =>
        Map(url, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(text) });

    public void MapBytes(string url, byte[] bytes) =>
        Map(url, () => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });

    public void MapFailure(string url, HttpStatusCode status = HttpStatusCode.InternalServerError) =>
        Map(url, () => new HttpResponseMessage(status));

    public int CountRequests(string url) => Requests.Count(request => request == url);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string url = request.RequestUri!.ToString();
        Requests.Enqueue(url);

        var response = _routes.TryGetValue(url, out var respond)
            ? respond()
            : new HttpResponseMessage(HttpStatusCode.NotFound);

        return Task.FromResult(response);
    }
}
