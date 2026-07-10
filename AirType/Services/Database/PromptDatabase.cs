using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AirType.Models;

namespace AirType.Services.Database
{
    public class PromptDatabase
    {
        private readonly ISqliteConnectionFactory _connectionFactory;

        public PromptDatabase()
            : this(new SqliteConnectionFactory())
        {
        }

        public PromptDatabase(ISqliteConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public List<PromptProfile> GetAllPrompts()
        {
            var prompts = new List<PromptProfile>();
            using (var connection = _connectionFactory.CreateConnection())
            {
                connection.Open();
                string selectSql = "SELECT * FROM Prompts ORDER BY SortOrder ASC, Name ASC;";
                using (var command = new SQLiteCommand(selectSql, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        prompts.Add(new PromptProfile
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("Id")),
                            Name = reader.GetString(reader.GetOrdinal("Name")),
                            Content = reader.GetString(reader.GetOrdinal("Content")),
                            IsBuiltIn = reader.GetBoolean(reader.GetOrdinal("IsBuiltIn")),
                            SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder"))
                        });
                    }
                }
            }
            return prompts;
        }

        public PromptProfile? GetPromptById(int id)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                connection.Open();
                string selectSql = "SELECT * FROM Prompts WHERE Id = @Id LIMIT 1;";
                using (var command = new SQLiteCommand(selectSql, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    using (var reader = command.ExecuteReader())
                    {
                        return reader.Read() ? ReadPrompt(reader) : null;
                    }
                }
            }
        }

        public PromptProfile? FindCustomPromptByContent(string content)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                connection.Open();
                string selectSql = "SELECT * FROM Prompts WHERE IsBuiltIn = 0 AND Content = @Content ORDER BY SortOrder ASC, Name ASC LIMIT 1;";
                using (var command = new SQLiteCommand(selectSql, connection))
                {
                    command.Parameters.AddWithValue("@Content", content);
                    using (var reader = command.ExecuteReader())
                    {
                        return reader.Read() ? ReadPrompt(reader) : null;
                    }
                }
            }
        }

        public bool PromptNameExists(string name)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                connection.Open();
                using (var command = new SQLiteCommand("SELECT COUNT(*) FROM Prompts WHERE Name = @Name;", connection))
                {
                    command.Parameters.AddWithValue("@Name", name);
                    return Convert.ToInt32(command.ExecuteScalar()) > 0;
                }
            }
        }

        public void AddPrompt(PromptProfile prompt)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                connection.Open();

                // Automatically find the next progressive SortOrder
                int nextOrder = 1;
                using (var orderCmd = new SQLiteCommand("SELECT COALESCE(MAX(SortOrder), 0) + 1 FROM Prompts", connection))
                {
                    nextOrder = Convert.ToInt32(orderCmd.ExecuteScalar());
                }

                string insertSql = "INSERT INTO Prompts (Name, Content, IsBuiltIn, SortOrder) VALUES (@Name, @Content, @IsBuiltIn, @SortOrder); SELECT last_insert_rowid();";
                using (var command = new SQLiteCommand(insertSql, connection))
                {
                    command.Parameters.AddWithValue("@Name", prompt.Name);
                    command.Parameters.AddWithValue("@Content", prompt.Content);
                    command.Parameters.AddWithValue("@IsBuiltIn", prompt.IsBuiltIn ? 1 : 0);
                    command.Parameters.AddWithValue("@SortOrder", nextOrder);
                    prompt.Id = Convert.ToInt32(command.ExecuteScalar());
                    prompt.SortOrder = nextOrder;
                }
            }
        }

        public void UpdatePrompt(PromptProfile prompt)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                connection.Open();
                string updateSql = "UPDATE Prompts SET Content = @Content, Name = @Name WHERE Id = @Id;";
                using (var command = new SQLiteCommand(updateSql, connection))
                {
                    command.Parameters.AddWithValue("@Id", prompt.Id);
                    command.Parameters.AddWithValue("@Name", prompt.Name);
                    command.Parameters.AddWithValue("@Content", prompt.Content);
                    command.ExecuteNonQuery();
                }
            }
        }

        public void SavePrompt(PromptProfile prompt)
        {
            if (prompt.Id <= 0)
            {
                AddPrompt(prompt);
            }
            else
            {
                UpdatePrompt(prompt);
            }
        }

        public void DeletePrompt(int id)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                connection.Open();
                string deleteSql = "DELETE FROM Prompts WHERE Id = @Id;";
                using (var command = new SQLiteCommand(deleteSql, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    command.ExecuteNonQuery();
                }
            }
        }

        private static PromptProfile ReadPrompt(SQLiteDataReader reader) => new()
        {
            Id = reader.GetInt32(reader.GetOrdinal("Id")),
            Name = reader.GetString(reader.GetOrdinal("Name")),
            Content = reader.GetString(reader.GetOrdinal("Content")),
            IsBuiltIn = reader.GetBoolean(reader.GetOrdinal("IsBuiltIn")),
            SortOrder = reader.GetInt32(reader.GetOrdinal("SortOrder"))
        };
    }
}
