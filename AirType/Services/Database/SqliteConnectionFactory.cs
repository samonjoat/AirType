using System;
using System.Data.SQLite;

namespace AirType.Services.Database;

public sealed class SqliteConnectionFactory : ISqliteConnectionFactory
{
    private readonly string _connectionString;

    public SqliteConnectionFactory(string? connectionString = null)
    {
        _connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? DatabaseInitializer.ConnectionString
            : connectionString;
    }

    public SQLiteConnection CreateConnection()
    {
        if (string.IsNullOrWhiteSpace(_connectionString))
        {
            throw new InvalidOperationException("SQLite connection string is not configured.");
        }

        return new SQLiteConnection(_connectionString);
    }
}

