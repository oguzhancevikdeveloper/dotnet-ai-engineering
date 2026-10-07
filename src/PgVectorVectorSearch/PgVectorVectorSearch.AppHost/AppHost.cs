var builder = DistributedApplication.CreateBuilder(args);

var ollama = builder.AddOllama("ollama")
    .WithLifetime(ContainerLifetime.Persistent) // Uygulama dursa bile container ayakta kalır, her seferinde yeniden başlatılmaz
    .WithDataVolume() // Model verileri için kalıcı volume oluşturur, veriler container silinse bile korunur
    .WithGPUSupport();

var embeddingModel = ollama.AddModel("qwen3-embedding");

var postgreSQL = builder.AddPostgres("postgreSQL", port: 6432)
    .WithLifetime(ContainerLifetime.Persistent)
    .WithDataVolume()
    .WithImage("pgvector/pgvector", "pg17")
    .AddDatabase("articles"); // PostgreSQL içinde "articles" adında bir veritabanı oluşturur

builder.AddProject<Projects.PgVectorVectorSearch_API>("api")
    .WithReference(embeddingModel)
    .WithReference(postgreSQL)
    .WaitFor(embeddingModel) // API, embedding model hazır olana kadar başlamaz
    .WaitFor(postgreSQL); // API, PostgreSQL hazır olana kadar başlamaz

// Uygulamayı derler ve çalıştırır
builder.Build().Run();
