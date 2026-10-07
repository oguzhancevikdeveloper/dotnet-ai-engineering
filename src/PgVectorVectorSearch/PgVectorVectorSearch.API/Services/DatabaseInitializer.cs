using Dapper;
using Npgsql;

namespace PgVectorVectorSearch.API.Services;

public static class DatabaseInitializer
{
    public static async Task InitializeAsync(NpgsqlDataSource npgsqlDataSource)
    {
        try
        {
            await using var connection = await npgsqlDataSource.OpenConnectionAsync();

            await using var command = new NpgsqlCommand("CREATE EXTENSION IF NOT EXISTS vector", connection);
            await command.ExecuteNonQueryAsync();

            await connection.ReloadTypesAsync();

            await connection.ExecuteAsync(
                """
                CREATE TABLE IF NOT EXISTS articles (
                    id SERIAL PRIMARY KEY,
                    title TEXT NOT NULL,
                    url TEXT NOT NULL,
                    content TEXT NOT NULL,
                    embedding VECTOR(4096) NOT NULL
                )
                """);

            await connection.ExecuteAsync(
                """
                CREATE INDEX IF NOT EXISTS articles_embedding_idx
                ON articles USING hnsw (embedding vector_cosine_ops)
                """);

            await Console.Out.WriteLineAsync("Database initialized successfully.");
        }
        catch
        {
            await Console.Out.WriteLineAsync("An error occurred while initializing the database.");
        }
    }
}
