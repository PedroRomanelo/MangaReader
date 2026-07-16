using System.Data;
using Microsoft.Data.Sqlite;

namespace MangaReader.Api.Infra;

public sealed class SqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(DatabaseOptions options)
    {
        _connectionString = options.ConnectionString;
    }

    public IDbConnection Create()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using var pragmas = connection.CreateCommand();
        pragmas.CommandText = "PRAGMA foreign_keys = ON; PRAGMA synchronous = NORMAL;";
        pragmas.ExecuteNonQuery();

        return connection;
    }
}
