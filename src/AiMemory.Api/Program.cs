using AiMemory.Ai;
using AiMemory.Api;
using AiMemory.Connectors;
using AiMemory.Core;
using AiMemory.Ingestion;
using AiMemory.Query;

var builder = WebApplication.CreateBuilder(args);

var ollamaUrl = builder.Configuration["Ollama:Url"] ?? "http://localhost:11434/";
var chatModelName = builder.Configuration["Ollama:ChatModel"] ?? "qwen2.5";
var embedModelName = builder.Configuration["Ollama:EmbedModel"] ?? "bge-m3";

builder.Services.AddHttpClient("ollama", client => client.BaseAddress = new Uri(ollamaUrl));

builder.Services.AddSingleton<IChunker>(new Chunker());
builder.Services.AddSingleton<RepoKnowledgeScanner>();
builder.Services.AddSingleton<IChatModel>(sp =>
    new OllamaChatModel(sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama"), chatModelName));
builder.Services.AddSingleton<IEmbedder>(sp =>
    new OllamaEmbedder(sp.GetRequiredService<IHttpClientFactory>().CreateClient("ollama"), embedModelName));
builder.Services.AddSingleton<IDecisionExtractor, DecisionExtractor>();
builder.Services.AddSingleton<IngestionOrchestrator>();
builder.Services.AddSingleton<QueryService>();
builder.Services.AddSingleton<IQueryService>(sp => sp.GetRequiredService<QueryService>());

// TODO (task 3.2): register IVectorStore with the QdrantVectorStore adapter.
// Until then, resolving IngestionOrchestrator / QueryService fails at request time.
// e.g. builder.Services.AddSingleton<IVectorStore>(sp => new QdrantVectorStore(...));

var app = builder.Build();
app.MapAiMemory();
app.Run();
