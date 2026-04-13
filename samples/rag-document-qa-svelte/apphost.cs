#:package Aspire.Hosting.Python@13.2.2
#:package Aspire.Hosting.JavaScript@13.2.2
#:package Aspire.Hosting.Qdrant@13.2.2
#:package Aspire.Hosting.OpenAI@13.2.2
#:sdk Aspire.AppHost.Sdk@13.2.2

var builder = DistributedApplication.CreateBuilder(args);

// Add Qdrant vector database
var qdrant = builder.AddQdrant("qdrant");

// OpenAI for embeddings and LLM
var openai = builder.AddOpenAI("openai");

// Python FastAPI for RAG backend
var api = builder.AddUvicornApp("api", "./api", "main:app")
                 .WithUv()
                 .WithHttpHealthCheck("/health")
                 .WaitFor(qdrant)
                 .WithReference(qdrant)
                 .WithEnvironment("OPENAI_APIKEY", openai.Resource.Key)
                 .WithExternalHttpEndpoints();

// Svelte frontend
var frontend = builder.AddViteApp("frontend", "./frontend")
                      .WithEndpoint("http", e => e.Port = 8092)
                      .WithReference(api)
                      .WithUrl("", "RAG UI");

api.PublishWithContainerFiles(frontend, "public");

builder.Build().Run();


