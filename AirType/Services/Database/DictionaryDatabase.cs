using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Threading.Tasks;
using AirType.Models.Dictionary;

namespace AirType.Services.Database
{
    public class DictionaryDatabase
    {
        private readonly ISqliteConnectionFactory _connectionFactory;

        public DictionaryDatabase()
            : this(new SqliteConnectionFactory())
        {
        }

        public DictionaryDatabase(ISqliteConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public async Task AddEntryAsync(DictionaryEntry entry)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                await connection.OpenAsync();
                string insertSql = @"
                    INSERT INTO UserDictionary 
                    (Id, EntryType, Word, OriginalText, CorrectedText, CreatedAt, ModifiedAt)
                    VALUES (@Id, @Type, @Word, @Original, @Corrected, @Created, @Modified);";

                using (var command = new SQLiteCommand(insertSql, connection))
                {
                    command.Parameters.AddWithValue("@Id", entry.Id);
                    command.Parameters.AddWithValue("@Type", (int)entry.EntryType);
                    command.Parameters.AddWithValue("@Word", entry.Word);
                    command.Parameters.AddWithValue("@Original", entry.OriginalText);
                    command.Parameters.AddWithValue("@Corrected", entry.CorrectedText);
                    command.Parameters.AddWithValue("@Created", entry.CreatedAt);
                    command.Parameters.AddWithValue("@Modified", entry.ModifiedAt);
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task<List<DictionaryEntry>> GetAllEntriesAsync()
        {
            var entries = new List<DictionaryEntry>();
            using (var connection = _connectionFactory.CreateConnection())
            {
                await connection.OpenAsync();
                string selectSql = "SELECT * FROM UserDictionary ORDER BY ModifiedAt DESC;";

                using (var command = new SQLiteCommand(selectSql, connection))
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var entry = new DictionaryEntry
                        {
                            Id = reader.GetGuid(reader.GetOrdinal("Id")),
                            EntryType = (DictionaryEntryType)reader.GetInt32(reader.GetOrdinal("EntryType")),
                            Word = reader["Word"]?.ToString(),
                            OriginalText = reader["OriginalText"]?.ToString(),
                            CorrectedText = reader["CorrectedText"]?.ToString(),
                            CreatedAt = reader.GetDateTime(reader.GetOrdinal("CreatedAt")),
                            ModifiedAt = reader.GetDateTime(reader.GetOrdinal("ModifiedAt"))
                        };
                        entries.Add(entry);
                    }
                }
            }
            return entries;
        }

        public async Task UpdateEntryAsync(DictionaryEntry entry)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                await connection.OpenAsync();
                string updateSql = @"
                    UPDATE UserDictionary 
                    SET Word = @Word, OriginalText = @Original, CorrectedText = @Corrected, ModifiedAt = @Modified
                    WHERE Id = @Id;";

                using (var command = new SQLiteCommand(updateSql, connection))
                {
                    command.Parameters.AddWithValue("@Id", entry.Id);
                    command.Parameters.AddWithValue("@Word", entry.Word);
                    command.Parameters.AddWithValue("@Original", entry.OriginalText);
                    command.Parameters.AddWithValue("@Corrected", entry.CorrectedText);
                    command.Parameters.AddWithValue("@Modified", DateTime.Now);
                    await command.ExecuteNonQueryAsync();
                }
            }
        }

        public async Task DeleteEntryAsync(Guid id)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                await connection.OpenAsync();
                string deleteSql = "DELETE FROM UserDictionary WHERE Id = @Id;";

                using (var command = new SQLiteCommand(deleteSql, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    await command.ExecuteNonQueryAsync();
                }
            }
        }
    }
}
