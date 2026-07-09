using System.Net;
using AiMemory.Ai;

namespace AiMemory.Tests;

public class OllamaClientsTests
{
    private sealed class StubHandler(string responseJson) : HttpMessageHandler
    {
        public string? LastBody { get; private set; }
        public Uri? LastUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastUri = request.RequestUri;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson),
            };
        }
    }

    private static HttpClient Client(StubHandler handler) =>
        new(handler) { BaseAddress = new Uri("http://localhost:11434/") };

    [Fact]
    public async Task Chat_Complete_ReturnsMessageContent()
    {
        var handler = new StubHandler("""{"message":{"role":"assistant","content":"hello there"}}""");
        var model = new OllamaChatModel(Client(handler), "test-model");

        var result = await model.CompleteAsync("sys", "user");

        Assert.Equal("hello there", result);
        Assert.EndsWith("api/chat", handler.LastUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chat_CompleteJson_SendsSchemaFormatAndDisablesThinking()
    {
        var handler = new StubHandler("""{"message":{"content":"{\"ok\":true}"}}""");
        var model = new OllamaChatModel(Client(handler), "test-model");

        var result = await model.CompleteJsonAsync("sys", "user", """{"type":"object"}""");

        Assert.Equal("{\"ok\":true}", result);
        Assert.Contains("\"think\":false", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"format\":{\"type\":\"object\"}", handler.LastBody, StringComparison.Ordinal);
        Assert.Contains("\"model\":\"test-model\"", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Chat_MalformedSchema_FallsBackToJsonFormat()
    {
        var handler = new StubHandler("""{"message":{"content":"{}"}}""");
        var model = new OllamaChatModel(Client(handler), "test-model");

        await model.CompleteJsonAsync("sys", "user", "not a schema");

        Assert.Contains("\"format\":\"json\"", handler.LastBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Embed_ReturnsDenseVector()
    {
        var handler = new StubHandler("""{"embeddings":[[0.1,0.2,0.3]]}""");
        var embedder = new OllamaEmbedder(Client(handler), "bge-m3");

        var vector = await embedder.EmbedAsync("some text");

        Assert.Equal([0.1f, 0.2f, 0.3f], vector.Dense);
        Assert.EndsWith("api/embed", handler.LastUri!.AbsolutePath, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"embeddings":[]}""")]      // no inner arrays
    [InlineData("""{"embeddings":[null]}""")]  // null inner array
    [InlineData("""{}""")]                       // missing key entirely
    public async Task Embed_MissingOrNullEmbeddings_ReturnsEmptyVectorNotNull(string responseJson)
    {
        var embedder = new OllamaEmbedder(Client(new StubHandler(responseJson)), "bge-m3");

        var vector = await embedder.EmbedAsync("some text");

        Assert.NotNull(vector.Dense);
        Assert.Empty(vector.Dense);
    }
}
