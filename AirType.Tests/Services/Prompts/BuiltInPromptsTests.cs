using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Linq;
using System.Reflection;
using AirType.Models.Transcription;
using AirType.Services.Database;
using AirType.Services.Prompts;
using Xunit;

namespace AirType.Tests.Services.Prompts;

public sealed class BuiltInPromptsTests
{
    [Fact]
    public void RequestDefaults_UseClassicBuiltInPrompt()
    {
        Assert.Equal(GeminiTranscriptionRequest.DefaultSystemInstruction, BuiltInPrompts.Classic.Content);
        Assert.Equal(OpenRouterTranscriptionRequest.DefaultSystemInstruction, BuiltInPrompts.Classic.Content);
    }

    [Fact]
    public void AllBuiltIns_AreModernizedAndDictionaryAware()
    {
        foreach (var profile in BuiltInPrompts.All)
        {
            Assert.True(profile.IsBuiltIn);
            Assert.DoesNotContain('\u001A', profile.Content);
            Assert.DoesNotContain("??", profile.Content, StringComparison.Ordinal);
            Assert.DoesNotContain("GROQ PROMPT", profile.Content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("dictionary", profile.Content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("raw", profile.Content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("cleanup", profile.Content, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("## END OF INSTRUCTIONS", profile.Content, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void DatabaseInitializer_SeedsBuiltInsFromSharedCatalog()
    {
        using var connection = new SQLiteConnection("Data Source=:memory:;Version=3;New=True;");
        connection.Open();
        using (var createCommand = new SQLiteCommand("""
            CREATE TABLE Prompts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL,
                Content TEXT NOT NULL,
                IsBuiltIn INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER DEFAULT 0
            );
            """, connection))
        {
            createCommand.ExecuteNonQuery();
        }

        InvokeEnsureBuiltInPrompts(connection);

        var rows = ReadPromptRows(connection);
        var expected = BuiltInPrompts.DatabaseSeedOrder.ToList();

        Assert.Equal(expected.Select(p => p.Name), rows.Select(r => r.Name));
        Assert.Equal(expected.Select(p => p.Content), rows.Select(r => r.Content));
        Assert.All(rows, row => Assert.Equal(1, row.IsBuiltIn));
        Assert.Equal(Enumerable.Range(1, expected.Count), rows.Select(r => r.SortOrder));
    }

    private static void InvokeEnsureBuiltInPrompts(SQLiteConnection connection)
    {
        var method = typeof(DatabaseInitializer).GetMethod(
            "EnsureBuiltInPrompts",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);
        method!.Invoke(null, new object[] { connection });
    }

    private static List<PromptRow> ReadPromptRows(SQLiteConnection connection)
    {
        var rows = new List<PromptRow>();
        using var command = new SQLiteCommand(
            "SELECT Name, Content, IsBuiltIn, SortOrder FROM Prompts ORDER BY SortOrder",
            connection);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            rows.Add(new PromptRow(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetInt32(3)));
        }

        return rows;
    }

    private sealed record PromptRow(string Name, string Content, int IsBuiltIn, int SortOrder);
}
