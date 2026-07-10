using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using AirType.Models;
using AirType.Models.Transcription;
using AirType.Models.Configuration;
using AirType.Services.Configuration;
using AirType.Services.Database;
using Xunit;

namespace AirType.Tests.Services.Configuration;

public sealed class CredentialManagerTests
{
    [Fact]
    public void SetActiveModel_PersistsFixedCloudModelImmediately()
    {
        using var tempDirectory = new TempDirectory();
        var manager = new CredentialManager(new InMemoryCredentialStore(), tempDirectory.RootPath);

        manager.SaveActiveProvider(TranscriptionProvider.Groq);
        manager.SetActiveModel(TranscriptionProvider.Groq, "whisper-large-v3");

        var reloaded = new CredentialManager(new InMemoryCredentialStore(), tempDirectory.RootPath);

        Assert.Equal(TranscriptionProvider.Groq, reloaded.GetActiveProvider());
        Assert.Equal("whisper-large-v3", reloaded.GetActiveModelId());
    }

    [Fact]
    public void GetActiveProvider_WhenConfigIsMissing_DefaultsToLocal()
    {
        using var tempDirectory = new TempDirectory();
        var manager = new CredentialManager(new InMemoryCredentialStore(), tempDirectory.RootPath);

        Assert.Equal(TranscriptionProvider.Local, manager.GetActiveProvider());
        Assert.Equal(LocalAsrModelCatalog.FasterWhisperSmallEnInt8, manager.GetActiveModelId());
    }

    [Fact]
    public void LoadingConfig_WhenSavedModelIsInvalid_SanitizesToProviderDefault()
    {
        using var tempDirectory = new TempDirectory();
        Directory.CreateDirectory(tempDirectory.RootPath);
        File.WriteAllText(
            Path.Combine(tempDirectory.RootPath, "provider_config.json"),
            """
            {
              "ActiveProvider": "OpenRouter",
              "GeminiModel": "gemini-2.5-flash",
              "OpenRouterModel": "google/gemini-2.5-flash",
              "GroqModel": "not-a-groq-model",
              "TextFormattingMode": "Markdown"
            }
            """);

        var manager = new CredentialManager(new InMemoryCredentialStore(), tempDirectory.RootPath);

        Assert.Equal("gemini-3.1-flash-lite", manager.GetGeminiModelId());
        Assert.Equal("openai/gpt-4o-mini-transcribe", manager.GetOpenRouterModelId());
        Assert.Equal("whisper-large-v3", manager.GetGroqModelId());
        Assert.Equal(TranscriptionProvider.Groq, manager.GetActiveProvider());
        Assert.Equal("whisper-large-v3", manager.GetActiveModelId());
        Assert.Equal(TextFormattingMode.Markdown, manager.GetTextFormattingMode());
    }

    [Theory]
    [InlineData("Gemini")]
    [InlineData("OpenRouter")]
    public void GetActiveProvider_WhenLegacyCloudAsrProviderIsSaved_MigratesToGroqCloud(string legacyProvider)
    {
        using var tempDirectory = new TempDirectory();
        Directory.CreateDirectory(tempDirectory.RootPath);
        File.WriteAllText(
            Path.Combine(tempDirectory.RootPath, "provider_config.json"),
            $$"""
            {
              "ActiveProvider": "{{legacyProvider}}",
              "GroqModel": "whisper-large-v3"
            }
            """);

        var manager = new CredentialManager(new InMemoryCredentialStore(), tempDirectory.RootPath);

        Assert.Equal(TranscriptionProvider.Groq, manager.GetActiveProvider());
        Assert.Equal("whisper-large-v3", manager.GetActiveModelId());
    }

    [Fact]
    public void SetActiveModel_WhenModelIsInvalid_ThrowsAndDoesNotPersist()
    {
        using var tempDirectory = new TempDirectory();
        var manager = new CredentialManager(new InMemoryCredentialStore(), tempDirectory.RootPath);

        var exception = Assert.Throws<ArgumentException>(
            () => manager.SetActiveModel(TranscriptionProvider.OpenRouter, "google/gemini-2.5-flash"));

        Assert.Equal("modelId", exception.ParamName);
        Assert.Equal("openai/gpt-4o-mini-transcribe", manager.GetOpenRouterModelId());
    }

    [Fact]
    public void CleanupProviderAndModel_PersistImmediately()
    {
        using var tempDirectory = new TempDirectory();
        var manager = new CredentialManager(new InMemoryCredentialStore(), tempDirectory.RootPath);

        manager.SaveActiveCleanupProvider(CleanupProvider.Groq);
        manager.SetActiveCleanupModel(CleanupProvider.Groq, "groq/compound-mini");

        var reloaded = new CredentialManager(new InMemoryCredentialStore(), tempDirectory.RootPath);

        Assert.Equal(CleanupProvider.Groq, reloaded.GetActiveCleanupProvider());
        Assert.Equal("groq/compound-mini", reloaded.GetActiveCleanupModelId());
    }

    [Fact]
    public void TranscriptCleanupEnabled_DefaultsToTrue()
    {
        using var tempDirectory = new TempDirectory();
        var manager = new CredentialManager(new InMemoryCredentialStore(), tempDirectory.RootPath);

        Assert.True(manager.IsTranscriptCleanupEnabled());
    }

    [Fact]
    public void SetTranscriptCleanupEnabled_PersistsImmediately()
    {
        using var tempDirectory = new TempDirectory();
        var manager = new CredentialManager(new InMemoryCredentialStore(), tempDirectory.RootPath);

        manager.SetTranscriptCleanupEnabled(false);

        var reloaded = new CredentialManager(new InMemoryCredentialStore(), tempDirectory.RootPath);

        Assert.False(reloaded.IsTranscriptCleanupEnabled());
    }

    [Fact]
    public void CleanupContextIntensityAndStyleOverride_PersistImmediately()
    {
        using var tempDirectory = new TempDirectory();
        var manager = new CredentialManager(new InMemoryCredentialStore(), tempDirectory.RootPath);

        manager.SetCleanupContextMode(CleanupContextMode.Terminal);
        manager.SetCleanupIntensity(CleanupIntensity.Light);
        manager.SetCleanupStyleOverride(CleanupStyleOverrideKind.CustomPrompt, 42);

        var reloaded = new CredentialManager(new InMemoryCredentialStore(), tempDirectory.RootPath);

        Assert.Equal(CleanupContextMode.Terminal, reloaded.GetCleanupContextMode());
        Assert.Equal(CleanupIntensity.Light, reloaded.GetCleanupIntensity());
        Assert.Equal(CleanupStyleOverrideKind.CustomPrompt, reloaded.GetCleanupStyleOverrideKind());
        Assert.Equal(42, reloaded.GetCleanupStylePromptId());
    }

    [Fact]
    public void LoadingConfig_WhenCleanupModelIsInvalid_SanitizesToCleanupProviderDefault()
    {
        using var tempDirectory = new TempDirectory();
        Directory.CreateDirectory(tempDirectory.RootPath);
        File.WriteAllText(
            Path.Combine(tempDirectory.RootPath, "provider_config.json"),
            """
            {
              "ActiveProvider": "Groq",
              "GroqModel": "whisper-large-v3",
              "CleanupProvider": "Groq",
              "GeminiCleanupModel": "gemini-2.5-flash",
              "OpenRouterCleanupModel": "openai/whisper-1",
              "GroqCleanupModel": "openai/gpt-5.4-mini"
            }
            """);

        var manager = new CredentialManager(new InMemoryCredentialStore(), tempDirectory.RootPath);

        Assert.Equal("gemini-3.1-flash-lite", manager.GetCleanupModelId(CleanupProvider.Gemini));
        Assert.Equal("openai/gpt-5.4-mini", manager.GetCleanupModelId(CleanupProvider.OpenRouter));
        Assert.Equal("llama-3.1-8b-instant", manager.GetCleanupModelId(CleanupProvider.Groq));
        Assert.Equal("llama-3.1-8b-instant", manager.GetActiveCleanupModelId());
    }

    [Theory]
    [InlineData("General", CleanupContextMode.Generic, CleanupIntensity.Standard, CleanupStyleOverrideKind.None)]
    [InlineData("Code", CleanupContextMode.Editor, CleanupIntensity.Standard, CleanupStyleOverrideKind.None)]
    [InlineData("Email", CleanupContextMode.Email, CleanupIntensity.Standard, CleanupStyleOverrideKind.None)]
    [InlineData("Chat", CleanupContextMode.Chat, CleanupIntensity.Standard, CleanupStyleOverrideKind.None)]
    [InlineData("Short Cleanup", CleanupContextMode.Generic, CleanupIntensity.Light, CleanupStyleOverrideKind.None)]
    [InlineData("Groq (Optimized)", CleanupContextMode.Generic, CleanupIntensity.Light, CleanupStyleOverrideKind.None)]
    [InlineData("6. Groq (Optimized)", CleanupContextMode.Generic, CleanupIntensity.Light, CleanupStyleOverrideKind.None)]
    [InlineData("Classic", CleanupContextMode.Generic, CleanupIntensity.Standard, CleanupStyleOverrideKind.Classic)]
    public void MigrateLegacyCleanupPromptSettings_MapsBuiltInActiveProfiles(
        string activeProfile,
        CleanupContextMode expectedContext,
        CleanupIntensity expectedIntensity,
        CleanupStyleOverrideKind expectedStyle)
    {
        using var tempDirectory = new TempDirectory();
        WriteLegacyPromptConfig(tempDirectory.RootPath, activeProfile);
        using var promptDatabase = new TempPromptDatabase();
        var manager = new CredentialManager(new InMemoryCredentialStore(), tempDirectory.RootPath);

        manager.MigrateLegacyCleanupPromptSettings(promptDatabase.Database);

        Assert.Equal(expectedContext, manager.GetCleanupContextMode());
        Assert.Equal(expectedIntensity, manager.GetCleanupIntensity());
        Assert.Equal(expectedStyle, manager.GetCleanupStyleOverrideKind());
        Assert.Null(manager.GetCleanupStylePromptId());
        Assert.Equal(1, manager.GetCleanupPromptMigrationVersion());
    }

    [Fact]
    public void MigrateLegacyCleanupPromptSettings_WhenNoLegacyConfig_UsesNewDefaults()
    {
        using var tempDirectory = new TempDirectory();
        using var promptDatabase = new TempPromptDatabase();
        var manager = new CredentialManager(new InMemoryCredentialStore(), tempDirectory.RootPath);

        manager.MigrateLegacyCleanupPromptSettings(promptDatabase.Database);

        Assert.Equal(CleanupContextMode.Auto, manager.GetCleanupContextMode());
        Assert.Equal(CleanupIntensity.Standard, manager.GetCleanupIntensity());
        Assert.Equal(CleanupStyleOverrideKind.None, manager.GetCleanupStyleOverrideKind());
        Assert.Null(manager.GetCleanupStylePromptId());
    }

    [Fact]
    public void MigrateLegacyCleanupPromptSettings_PreservesExistingCustomPromptRows()
    {
        using var tempDirectory = new TempDirectory();
        WriteLegacyPromptConfig(tempDirectory.RootPath, "General");
        using var promptDatabase = new TempPromptDatabase();
        var existing = new PromptProfile
        {
            Name = "My Custom Style",
            Content = "Keep this exact guidance.",
            IsBuiltIn = false
        };
        promptDatabase.Database.AddPrompt(existing);
        var manager = new CredentialManager(new InMemoryCredentialStore(), tempDirectory.RootPath);

        manager.MigrateLegacyCleanupPromptSettings(promptDatabase.Database);

        var saved = promptDatabase.Database.GetPromptById(existing.Id);
        Assert.NotNull(saved);
        Assert.Equal(existing.Id, saved!.Id);
        Assert.Equal("My Custom Style", saved.Name);
        Assert.Equal("Keep this exact guidance.", saved.Content);
        Assert.False(saved.IsBuiltIn);
        Assert.Equal(existing.SortOrder, saved.SortOrder);
    }

    [Fact]
    public void MigrateLegacyCleanupPromptSettings_ImportsLegacyCustomPromptOnceAndSelectsIt()
    {
        using var tempDirectory = new TempDirectory();
        WriteLegacyPromptConfig(tempDirectory.RootPath, "Custom");
        File.WriteAllText(Path.Combine(tempDirectory.RootPath, "custom_prompt.txt"), "Custom style guidance");
        using var promptDatabase = new TempPromptDatabase();
        var manager = new CredentialManager(new InMemoryCredentialStore(), tempDirectory.RootPath);

        manager.MigrateLegacyCleanupPromptSettings(promptDatabase.Database);
        manager.MigrateLegacyCleanupPromptSettings(promptDatabase.Database);

        var customPrompts = promptDatabase.Database.GetAllPrompts().Where(prompt => !prompt.IsBuiltIn).ToList();
        Assert.Single(customPrompts);
        Assert.Equal("Custom style guidance", customPrompts[0].Content);
        Assert.Equal(CleanupContextMode.Auto, manager.GetCleanupContextMode());
        Assert.Equal(CleanupIntensity.Standard, manager.GetCleanupIntensity());
        Assert.Equal(CleanupStyleOverrideKind.CustomPrompt, manager.GetCleanupStyleOverrideKind());
        Assert.Equal(customPrompts[0].Id, manager.GetCleanupStylePromptId());
    }

    private static void WriteLegacyPromptConfig(string rootPath, string activeProfile)
    {
        File.WriteAllText(
            Path.Combine(rootPath, "prompt_config.json"),
            $$"""
            {
              "ActiveProfile": "{{activeProfile}}"
            }
            """);
    }

    private sealed class InMemoryCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);

        public void Save(string filePath, string secret) => _secrets[filePath] = secret;

        public string? Read(string filePath) =>
            _secrets.TryGetValue(filePath, out string? secret) ? secret : null;

        public void Delete(string filePath) => _secrets.Remove(filePath);

        public bool Exists(string filePath) => _secrets.ContainsKey(filePath);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            RootPath = Path.Combine(Path.GetTempPath(), $"AirTypeCredentialManagerTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(RootPath);
        }

        public string RootPath { get; }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, recursive: true);
                }
            }
            catch
            {
            }
        }
    }

    private sealed class TempPromptDatabase : IDisposable
    {
        private readonly string _dbPath;

        public TempPromptDatabase()
        {
            _dbPath = Path.Combine(Path.GetTempPath(), $"AirTypePromptTests_{Guid.NewGuid():N}.db");
            using var connection = new SQLiteConnection($"Data Source={_dbPath};Version=3;");
            connection.Open();
            using var command = new SQLiteCommand("""
                CREATE TABLE Prompts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL UNIQUE,
                    Content TEXT NOT NULL,
                    IsBuiltIn INTEGER NOT NULL DEFAULT 0,
                    SortOrder INTEGER DEFAULT 0
                );
                """, connection);
            command.ExecuteNonQuery();

            Database = new PromptDatabase(new SqliteConnectionFactory($"Data Source={_dbPath};Version=3;"));
        }

        public PromptDatabase Database { get; }

        public void Dispose()
        {
            SQLiteConnection.ClearAllPools();
            try
            {
                if (File.Exists(_dbPath))
                {
                    File.Delete(_dbPath);
                }
            }
            catch
            {
            }
        }
    }
}
