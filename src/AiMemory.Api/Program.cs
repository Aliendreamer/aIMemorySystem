using AiMemory.Ai;
using AiMemory.Api;
using AiMemory.Connectors;
using AiMemory.Core;
using AiMemory.Ingestion;
using AiMemory.Query;
using AiMemory.Storage;
using Qdrant.Client;

var builder = WebApplication.CreateBuilder(args);

var ollamaUrl = builder.Configuration["Ollama:Url"] ?? "http://localhost:11434/";
var chatModelName = builder.Configuration["Ollama:ChatModel"] ?? "qwen2.5";
var embedModelName = builder.Configuration["Ollama:EmbedModel"] ?? "bge-m3";
var qdrantHost = builder.Configuration["Qdrant:Host"] ?? "localhost";
var qdrantPort = int.TryParse(builder.Configuration["Qdrant:Port"], out var p) ? p : 6334;
var qdrantCollection = builder.Configuration["Qdrant:Collection"] ?? "aimemory";
var embedDimensions = int.TryParse(builder.Configuration["Qdrant:VectorSize"], out var d) ? d : 1024;

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

builder.Services.AddSingleton(_ => new QdrantClient(qdrantHost, qdrantPort));
builder.Services.AddSingleton<IVectorStore>(sp =>
    new QdrantVectorStore(sp.GetRequiredService<QdrantClient>(), qdrantCollection, embedDimensions));

var app = builder.Build();
app.MapAiMemory();
app.Run();
