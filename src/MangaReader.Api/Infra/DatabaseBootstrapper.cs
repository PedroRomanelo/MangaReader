using Dapper;
using Microsoft.Data.Sqlite;

namespace MangaReader.Api.Infra;

public static class DatabaseBootstrapper
{
    public static void Initialize(IServiceProvider services)
    {
        var options = services.GetRequiredService<DatabaseOptions>();
        var logger = services.GetRequiredService<ILoggerFactory>()
            .CreateLogger(typeof(DatabaseBootstrapper).FullName!);

        var dataSource = new SqliteConnectionStringBuilder(options.ConnectionString).DataSource;
        var directory = Path.GetDirectoryName(dataSource);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using var connection = new SqliteConnection(options.ConnectionString);
        connection.Open();

        // WAL persiste no arquivo — aplicar uma vez basta.
        connection.Execute("PRAGMA journal_mode = WAL;");
        connection.Execute("PRAGMA foreign_keys = ON;");

        var schemaAlreadyApplied = connection.ExecuteScalar<long>(
            "SELECT count(*) FROM sqlite_master WHERE type='table' AND name='manga';") > 0;

        if (schemaAlreadyApplied)
        {
            logger.LogInformation("Schema já presente em {DataSource}", dataSource);
            return;
        }

        var schemaPath = Path.Combine(AppContext.BaseDirectory, "schema.sql");
        if (!File.Exists(schemaPath))
        {
            throw new FileNotFoundException(
                $"schema.sql não encontrado em {schemaPath}. Verifique se está linkado no .csproj com CopyToOutputDirectory.",
                schemaPath);
        }

        var sql = File.ReadAllText(schemaPath);
        connection.Execute(sql);
        logger.LogInformation("Schema aplicado em {DataSource}", dataSource);
    }
}
