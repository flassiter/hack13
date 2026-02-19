using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using MySqlConnector;
using Npgsql;

namespace Hack13.DatabaseReader;

internal static class DbConnectionFactory
{
    public static DbConnection Create(string provider, string connectionString) =>
        provider.ToLowerInvariant() switch
        {
            "sqlserver" or "mssql"       => new SqlConnection(connectionString),
            "postgresql" or "postgres"   => new NpgsqlConnection(connectionString),
            "mysql" or "mariadb"         => new MySqlConnection(connectionString),
            "sqlite"                     => new SqliteConnection(connectionString),
            _                            => throw new InvalidOperationException("UNSUPPORTED_PROVIDER")
        };
}
