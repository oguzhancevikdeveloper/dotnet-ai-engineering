using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;
using Npgsql;
using Pgvector.Dapper;
using PgVectorVectorSearch.API.Services;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddHttpClient()
    .ConfigureHttpClientDefaults(builder =>
    {
        builder.ConfigureHttpClient(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(600);
        });
    });


builder.Services.AddScoped<IEmbeddingGenerator<string, Embedding<float>>, CustomOllamaEmbeddingGenerator>();

builder.AddNpgsqlDataSource("articles", configureDataSourceBuilder: builder => builder.UseVector());

SqlMapper.AddTypeHandler(new VectorTypeHandler());
builder.Services.AddScoped<BlogService>();
builder.Services.AddControllers();
builder.Services.AddOpenApi();


var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

using var scope = app.Services.CreateScope();
await DatabaseInitializer.InitializeAsync(scope.ServiceProvider.GetRequiredService<NpgsqlDataSource>());

app.MapGet("/embeddings/generate", async (
    [FromServices] IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    [FromServices] NpgsqlDataSource npgsqlDataSource,
    [FromServices] BlogService blogService,
    [FromServices] ILogger<Program> logger) =>
{
    await using var connection = await npgsqlDataSource.OpenConnectionAsync();
    await connection.ReloadTypesAsync();

    var urls = await blogService.GetSitemapAsync();
    int count = 0;

    foreach (string url in urls)
    {
        var (title, content) = await blogService.GetTitleAndContentAsync(url);
        var embedding = await embeddingGenerator.GenerateAsync(content);
        await connection.ExecuteAsync(
            "INSERT INTO articles(title, url, content, embedding) VALUES(@Title, @Url, @Content, @Embedding)",
            new { Title = title, Url = url, Content = content, Embedding = new Pgvector.Vector(embedding.Vector.ToArray()) });
        count++;
        logger.LogInformation("Processed {count}: {url}", count, url);
    }
});

app.MapGet("/search", async (
    [FromQuery] string query,
    [FromServices] IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator,
    [FromServices] NpgsqlDataSource npgsqlDataSource) =>
{
    var searchEmbedding = await embeddingGenerator.GenerateAsync(query);

    await using var connection = await npgsqlDataSource.OpenConnectionAsync();
    await connection.ReloadTypesAsync();

    var results = await connection.QueryAsync<dynamic>(
        """
        SELECT id, title, url, content, embedding <=> @Embedding as distance
        FROM articles
        ORDER BY embedding <=> @Embedding
        LIMIT 5
        """,
        new { Embedding = new Pgvector.Vector(searchEmbedding.Vector.ToArray()) });

    return TypedResults.Ok(new { query, results = results.Select(r => r.url) });
});

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.MapDefaultEndpoints();

app.Run();

// ✅ Tamdır! Custom Ollama Embedding Generator
public class CustomOllamaEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public CustomOllamaEmbeddingGenerator(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public void Dispose()
    {
        throw new NotImplementedException();
    }

    public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values,
        EmbeddingGenerationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var endpoint = _configuration["Aspire:OllamaSharp:Endpoint"] ?? "http://localhost:11434";
        var modelName = _configuration["Aspire:OllamaSharp:Model"] ?? "qwen3-embedding:latest";

        var embeddings = new List<Embedding<float>>();

        foreach (var value in values)
        {
            var requestBody = new
            {
                model = modelName,
                input = value
            };

            var jsonContent = new StringContent(
                JsonSerializer.Serialize(requestBody),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(
                $"{endpoint}/api/embed",
                jsonContent,
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var jsonResponse = await response.Content.ReadAsStringAsync(cancellationToken);
            using var jsonDoc = JsonDocument.Parse(jsonResponse);
            var embeddingsArray = jsonDoc.RootElement.GetProperty("embeddings")[0];

            var floatArray = new List<float>();

            foreach (var element in embeddingsArray.EnumerateArray())
                floatArray.Add(element.GetSingle());

            embeddings.Add(new Embedding<float>(floatArray.ToArray()));
        }

        return new GeneratedEmbeddings<Embedding<float>>(embeddings);
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return null;
    }
}