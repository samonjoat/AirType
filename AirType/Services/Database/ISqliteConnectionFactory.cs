using System.Data.SQLite;

namespace AirType.Services.Database;

public interface ISqliteConnectionFactory
{
    SQLiteConnection CreateConnection();
}

