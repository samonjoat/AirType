using System;
using System.Collections.Generic;
using System.Data.SQLite;
using AirType.Models;

namespace AirType.Services.Database
{
    public class NotesDatabase
    {
        private readonly ISqliteConnectionFactory _connectionFactory;

        public NotesDatabase()
            : this(new SqliteConnectionFactory())
        {
        }

        public NotesDatabase(ISqliteConnectionFactory connectionFactory)
        {
            _connectionFactory = connectionFactory ?? throw new ArgumentNullException(nameof(connectionFactory));
        }

        public void AddNote(Note note)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                connection.Open();
                string insertSql = "INSERT INTO Notes (Date, Content) VALUES (@Date, @Content); SELECT last_insert_rowid();";
                using (var command = new SQLiteCommand(insertSql, connection))
                {
                    command.Parameters.AddWithValue("@Date", note.Date);
                    command.Parameters.AddWithValue("@Content", note.Content);
                    note.Id = Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }

        public List<Note> GetAllNotes()
        {
            var notes = new List<Note>();
            using (var connection = _connectionFactory.CreateConnection())
            {
                connection.Open();
                string selectSql = "SELECT Id, Date, Content, EditedAt FROM Notes ORDER BY Date DESC;";
                using (var command = new SQLiteCommand(selectSql, connection))
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var editedAtOrdinal = reader.GetOrdinal("EditedAt");
                        notes.Add(new Note
                        {
                            Id = reader.GetInt32(reader.GetOrdinal("Id")),
                            Date = reader.GetDateTime(reader.GetOrdinal("Date")),
                            Content = reader.GetString(reader.GetOrdinal("Content")),
                            EditedAt = reader.IsDBNull(editedAtOrdinal) ? null : reader.GetDateTime(editedAtOrdinal)
                        });
                    }
                }
            }
            return notes;
        }

        public List<Note> SearchNotes(string searchQuery)
        {
            var notes = new List<Note>();
            using (var connection = _connectionFactory.CreateConnection())
            {
                connection.Open();
                string searchSql = "SELECT Id, Date, Content, EditedAt FROM Notes WHERE Content LIKE @Query ORDER BY Date DESC;";
                using (var command = new SQLiteCommand(searchSql, connection))
                {
                    command.Parameters.AddWithValue("@Query", $"%{searchQuery}%");
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var editedAtOrdinal = reader.GetOrdinal("EditedAt");
                            notes.Add(new Note
                            {
                                Id = reader.GetInt32(reader.GetOrdinal("Id")),
                                Date = reader.GetDateTime(reader.GetOrdinal("Date")),
                                Content = reader.GetString(reader.GetOrdinal("Content")),
                                EditedAt = reader.IsDBNull(editedAtOrdinal) ? null : reader.GetDateTime(editedAtOrdinal)
                            });
                        }
                    }
                }
            }
            return notes;
        }

        public void UpdateNote(Note note)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                connection.Open();
                // Keep original Date, set EditedAt to mark as edited
                string updateSql = "UPDATE Notes SET Content = @Content, EditedAt = @EditedAt WHERE Id = @Id;";
                using (var command = new SQLiteCommand(updateSql, connection))
                {
                    command.Parameters.AddWithValue("@Id", note.Id);
                    command.Parameters.AddWithValue("@Content", note.Content);
                    command.Parameters.AddWithValue("@EditedAt", DateTime.Now);
                    command.ExecuteNonQuery();
                }
            }
        }

        public void DeleteNote(int id)
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                connection.Open();
                string deleteSql = "DELETE FROM Notes WHERE Id = @Id;";
                using (var command = new SQLiteCommand(deleteSql, connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    command.ExecuteNonQuery();
                }
            }
        }

        public bool IsEmpty()
        {
            using (var connection = _connectionFactory.CreateConnection())
            {
                connection.Open();
                using (var command = new SQLiteCommand("SELECT COUNT(*) FROM Notes", connection))
                {
                    return Convert.ToInt32(command.ExecuteScalar()) == 0;
                }
            }
        }
    }
}
