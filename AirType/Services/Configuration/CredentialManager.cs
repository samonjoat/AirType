using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using AirType.Models.Configuration;
using AirType.Services.Database;
using AirType.Services.Prompts;

namespace AirType.Services.Configuration;

/// <summary>
/// Manages API credentials with hybrid approach supporting both user-provided
/// and developer fallback API keys. Uses Windows Data Protection API for secure storage.
/// </summary>
public class CredentialManager : ICredentialManager
{
    private readonly string _credentialFilePath;
    private readonly string _openRouterCredentialFilePath;
    private readonly string _groqCredentialFilePath;
    private readonly string _providerConfigFilePath;
    private readonly string _appFolder;
    private readonly ICredentialStore _credentialStore;
    private CleanupProvider _activeCleanupProvider = CleanupProvider.Gemini;
    private string _activeGeminiCleanupModel = ProviderModelCatalog.GetDefaultCleanupModelId(CleanupProvider.Gemini);
    private string _activeOpenRouterCleanupModel = ProviderModelCatalog.GetDefaultCleanupModelId(CleanupProvider.OpenRouter);
    private string _activeGroqCleanupModel = ProviderModelCatalog.GetDefaultCleanupModelId(CleanupProvider.Groq);
    private bool _isTranscriptCleanupEnabled = true;
    private CleanupContextMode _cleanupContextMode = CleanupContextMode.Auto;
    private CleanupIntensity _cleanupIntensity = CleanupIntensity.Standard;
    private CleanupStyleOverrideKind _cleanupStyleOverrideKind = CleanupStyleOverrideKind.None;
    private int? _cleanupStylePromptId;
    private int _cleanupPromptMigrationVersion;
    
    // Default to true: inject into active window when transcription completes
    private TextFormattingMode _textFormattingMode = TextFormattingMode.PlainText;

    /// <summary>
    /// Initializes a new instance of CredentialManager.
    /// Uses encrypted file storage in %LOCALAPPDATA%\AirType\
    /// </summary>
    public CredentialManager()
        : this(new ProtectedFileCredentialStore())
    {
    }

    public CredentialManager(ICredentialStore credentialStore)
        : this(credentialStore, AirTypeStoragePaths.CanonicalRoot)
    {
    }

    public CredentialManager(ICredentialStore credentialStore, string appFolder)
    {
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        if (string.IsNullOrWhiteSpace(appFolder))
            throw new ArgumentException("App folder cannot be empty.", nameof(appFolder));

        Directory.CreateDirectory(appFolder);
        _appFolder = appFolder;
        _credentialFilePath = Path.Combine(appFolder, "credentials.dat");
        _openRouterCredentialFilePath = Path.Combine(appFolder, "openrouter_credentials.dat");
        _groqCredentialFilePath = Path.Combine(appFolder, "groq_credentials.dat");
        _providerConfigFilePath = Path.Combine(appFolder, "provider_config.json");

        // Load saved provider and model configuration
        LoadProviderAndModelConfig();
    }

    private string GetCredentialPath(TranscriptionProvider provider) => provider switch
    {
        TranscriptionProvider.Gemini => _credentialFilePath,
        TranscriptionProvider.OpenRouter => _openRouterCredentialFilePath,
        TranscriptionProvider.Groq => _groqCredentialFilePath,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Unsupported credential provider.")
    };

    private void SaveUserCredential(
        TranscriptionProvider provider,
        string apiKey,
        string emptyMessage,
        string failureMessage)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new ArgumentException(emptyMessage, nameof(apiKey));

        try
        {
            _credentialStore.Save(GetCredentialPath(provider), apiKey);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(failureMessage, ex);
        }
    }

    private string? GetUserCredential(TranscriptionProvider provider) =>
        _credentialStore.Read(GetCredentialPath(provider));

    private void DeleteUserCredential(TranscriptionProvider provider) =>
        _credentialStore.Delete(GetCredentialPath(provider));

    private bool HasUserCredential(TranscriptionProvider provider) =>
        _credentialStore.Exists(GetCredentialPath(provider));

    #region User API Key Management (Mandatory)

    /// <summary>
    /// Saves user's API key using Windows Data Protection API (DPAPI) encryption.
    /// </summary>
    public void SaveApiKey(string apiKey)
    {
        SaveUserCredential(
            TranscriptionProvider.Gemini,
            apiKey,
            "API key cannot be empty",
            "Failed to save API key.");
    }

    public string? GetUserApiKey() => GetUserCredential(TranscriptionProvider.Gemini);

    public void DeleteApiKey() => DeleteUserCredential(TranscriptionProvider.Gemini);
    public bool HasUserApiKey() => HasUserCredential(TranscriptionProvider.Gemini);

    #endregion

    #region Key Retrieval (Strictly User-Provided)

    /// <summary>
    /// Gets the active API key. Only returns user-provided keys.
    /// </summary>
    public string? GetActiveApiKey()
    {
        Logger.Debug("CredentialManager", "Checking for user-provided Gemini API key...");
        return GetUserApiKey();
    }

    public ApiKeySource GetApiKeySource() => HasUserApiKey() ? ApiKeySource.UserProvided : ApiKeySource.None;
    public bool IsUsingFallbackKey() => false;

    #endregion

    #region OpenRouter API Key Management (Task 2 - OpenRouter MVP)

    /// <summary>
    /// Saves user's OpenRouter API key using Windows Data Protection API (DPAPI) encryption.
    /// Encrypted data is stored in %LOCALAPPDATA%\AirType\openrouter_credentials.dat
    /// </summary>
    public void SaveOpenRouterApiKey(string apiKey)
    {
        SaveUserCredential(
            TranscriptionProvider.OpenRouter,
            apiKey,
            "OpenRouter API key cannot be empty",
            "Failed to save OpenRouter API key. Ensure Windows Data Protection is available.");
    }

    /// <summary>
    /// Retrieves user's OpenRouter API key by decrypting stored data using Windows DPAPI.
    /// </summary>
    public string? GetUserOpenRouterApiKey() => GetUserCredential(TranscriptionProvider.OpenRouter);

    /// <summary>
    /// Deletes user's OpenRouter API key by removing the encrypted credentials file.
    /// </summary>
    public void DeleteOpenRouterApiKey() => DeleteUserCredential(TranscriptionProvider.OpenRouter);

    /// <summary>
    /// Checks if user has configured their own OpenRouter API key.
    /// </summary>
    public bool HasUserOpenRouterApiKey() => HasUserCredential(TranscriptionProvider.OpenRouter);

    public string? GetActiveOpenRouterApiKey()
    {
        Logger.Debug("CredentialManager", "Getting user-provided OpenRouter API key...");
        return GetUserOpenRouterApiKey();
    }

    public bool IsUsingOpenRouterFallbackKey() => false;

    #endregion

    #region Groq API Key Management (Strictly User-Provided)
    
    public void SaveGroqApiKey(string apiKey)
    {
        SaveUserCredential(
            TranscriptionProvider.Groq,
            apiKey,
            "API key cannot be empty",
            "Failed to save Groq API key.");
    }

    public string? GetUserGroqApiKey() => GetUserCredential(TranscriptionProvider.Groq);

    public void DeleteGroqApiKey() => DeleteUserCredential(TranscriptionProvider.Groq);
    public bool HasUserGroqApiKey() => HasUserCredential(TranscriptionProvider.Groq);

    public string? GetActiveGroqApiKey() => GetUserGroqApiKey();
    public bool IsUsingGroqFallbackKey() => false;

    #endregion

    #region Provider Configuration (Task 3 - Provider Selection)

    /// <summary>
    /// Saves the active transcription provider to JSON configuration file.
    /// Preserves existing model selections for both providers.
    /// Stored in %LOCALAPPDATA%\AirType\provider_config.json
    /// </summary>
    public void SaveActiveProvider(TranscriptionProvider provider)
    {
        provider = ProviderModelCatalog.NormalizeAsrProvider(provider);
        // Use comprehensive save to preserve model selections
        SaveProviderAndModelConfig(provider);
        Logger.Debug("CredentialManager", $"Provider switched to: {provider}");
    }

    /// <summary>
    /// Gets the active transcription provider from JSON configuration file.
    /// Defaults to Local if not configured.
    /// </summary>
    public TranscriptionProvider GetActiveProvider()
    {
        try
        {
            if (!File.Exists(_providerConfigFilePath))
                return TranscriptionProvider.Local;

            string json = File.ReadAllText(_providerConfigFilePath);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ActiveProvider", out var providerElement))
            {
                string providerString = providerElement.GetString() ?? ProviderModelCatalog.GetDisplayName(TranscriptionProvider.Local);
                if (ProviderModelCatalog.TryParseProvider(providerString, out var provider))
                {
                    return ProviderModelCatalog.NormalizeAsrProvider(provider);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("CredentialManager", $"Failed to load provider config: {ex.Message}");
        }

        return TranscriptionProvider.Local;
    }

    #endregion

    #region Model Configuration (MVP - Provider & Model Selection UI)

    private ModelConfig _modelConfig = new ModelConfig();

    /// <summary>
    /// Gets the active model ID for the current provider.
    /// </summary>
    public string GetActiveModelId()
    {
        var provider = GetActiveProvider();
        return GetModelId(provider);
    }

    public string GetModelId(TranscriptionProvider provider) =>
        ProviderModelCatalog.GetActiveModelId(_modelConfig, provider);

    /// <summary>
    /// Sets the active model for a specific provider and persists to config.
    /// </summary>
    public void SetActiveModel(TranscriptionProvider provider, string modelId)
    {
        if (!ProviderModelCatalog.ContainsModel(provider, modelId))
        {
            throw new ArgumentException(
                $"'{modelId}' is not a supported {ProviderModelCatalog.GetDisplayName(provider)} model.",
                nameof(modelId));
        }

        ProviderModelCatalog.SetActiveModelId(_modelConfig, provider, modelId);
        SaveProviderAndModelConfig();
        Logger.Debug("CredentialManager", $"Active model set to: {modelId}");
    }

    /// <summary>
    /// Toggles between Gemini and OpenRouter providers.
    /// </summary>
    public TranscriptionProvider ToggleProvider()
    {
        var current = GetActiveProvider();
        var providerOrder = ProviderModelCatalog.ProviderOrder;

        int currentIndex = Array.IndexOf(providerOrder, current);
        int nextIndex = (currentIndex + 1) % providerOrder.Length;
        var newProvider = providerOrder[nextIndex];

        SaveActiveProvider(newProvider);
        Logger.Debug("CredentialManager", $"Provider toggled to: {newProvider}");
        return newProvider;
    }

    /// <summary>
    /// Cycles to the next available model for the current provider.
    /// </summary>
    public string CycleModel()
    {
        var provider = GetActiveProvider();

        var models = ProviderModelCatalog.GetModels(provider);
        if (models.Length == 0)
        {
            return "Unknown";
        }

        string activeModelId = ProviderModelCatalog.GetActiveModelId(_modelConfig, provider);
        int currentIndex = Array.FindIndex(models, m => m.Id == activeModelId);
        int nextIndex = (currentIndex + 1) % models.Length;
        ProviderModelCatalog.SetActiveModelId(_modelConfig, provider, models[nextIndex].Id);
        SaveProviderAndModelConfig();
        Logger.Debug("CredentialManager", $"Cycled to {ProviderModelCatalog.GetDisplayName(provider)} model: {models[nextIndex].DisplayName}");
        return models[nextIndex].DisplayName;
    }

    /// <summary>
    /// Gets the active model ID for local faster-whisper.
    /// </summary>
    public string GetLocalModelId() => GetModelId(TranscriptionProvider.Local);

    /// <summary>
    /// Gets the active model ID for Gemini Direct.
    /// </summary>
    public string GetGeminiModelId() => GetModelId(TranscriptionProvider.Gemini);

    /// <summary>
    /// Gets the active model ID for OpenRouter.
    /// </summary>
    public string GetOpenRouterModelId() => GetModelId(TranscriptionProvider.OpenRouter);

    /// <summary>
    /// Gets the active model ID for Groq.
    /// </summary>
    public string GetGroqModelId() => GetModelId(TranscriptionProvider.Groq);

    /// <summary>
    /// Saves provider and model configuration to JSON file with both provider and model info.
    /// </summary>
    /// <param name="provider">Optional provider to save. If null, reads from current config file.</param>
    private void SaveProviderAndModelConfig(TranscriptionProvider? provider = null)
    {
        try
        {
            var config = new
            {
                ActiveProvider = ProviderModelCatalog.NormalizeAsrProvider(provider ?? GetActiveProvider()).ToString(),
                LocalModel = _modelConfig.ActiveLocalModel,
                GeminiModel = _modelConfig.ActiveGeminiModel,
                OpenRouterModel = _modelConfig.ActiveOpenRouterModel,
                GroqModel = _modelConfig.ActiveGroqModel,
                CleanupProvider = _activeCleanupProvider.ToString(),
                GeminiCleanupModel = _activeGeminiCleanupModel,
                OpenRouterCleanupModel = _activeOpenRouterCleanupModel,
                GroqCleanupModel = _activeGroqCleanupModel,
                TranscriptCleanupEnabled = _isTranscriptCleanupEnabled,
                TextFormattingMode = _textFormattingMode.ToString(),
                CleanupContextMode = _cleanupContextMode.ToString(),
                CleanupIntensity = _cleanupIntensity.ToString(),
                CleanupStyleOverrideKind = _cleanupStyleOverrideKind.ToString(),
                CleanupStylePromptId = _cleanupStylePromptId,
                CleanupPromptMigrationVersion = _cleanupPromptMigrationVersion
            };

            string json = System.Text.Json.JsonSerializer.Serialize(config, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(_providerConfigFilePath, json);
            Logger.Debug("CredentialManager", $"Config saved: {config.ActiveProvider} - Local: {config.LocalModel}, Gemini: {config.GeminiModel}, OpenRouter: {config.OpenRouterModel}, Groq: {config.GroqModel}, Formatting: {_textFormattingMode}");
        }
        catch (Exception ex)
        {
            Logger.Debug("CredentialManager", $"Failed to save provider/model config: {ex.Message}");
        }
    }

    /// <summary>
    /// Loads provider and model configuration from JSON file.
    /// </summary>
    private void LoadProviderAndModelConfig()
    {
        try
        {
            if (File.Exists(_providerConfigFilePath))
            {
                string json = File.ReadAllText(_providerConfigFilePath);
                using var doc = System.Text.Json.JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("LocalModel", out var localModelElement))
                {
                    string? localModel = localModelElement.GetString();
                    if (!string.IsNullOrEmpty(localModel) &&
                        ProviderModelCatalog.ContainsModel(TranscriptionProvider.Local, localModel))
                    {
                        _modelConfig.ActiveLocalModel = localModel;
                    }
                }

                // Load Gemini model
                if (doc.RootElement.TryGetProperty("GeminiModel", out var geminiModelElement))
                {
                    string? geminiModel = geminiModelElement.GetString();
                    if (!string.IsNullOrEmpty(geminiModel) &&
                        ProviderModelCatalog.ContainsModel(TranscriptionProvider.Gemini, geminiModel))
                    {
                        _modelConfig.ActiveGeminiModel = geminiModel;
                    }
                }

                // Load OpenRouter model
                if (doc.RootElement.TryGetProperty("OpenRouterModel", out var orModelElement))
                {
                    string? orModel = orModelElement.GetString();
                    if (!string.IsNullOrEmpty(orModel) &&
                        ProviderModelCatalog.ContainsModel(TranscriptionProvider.OpenRouter, orModel))
                    {
                        _modelConfig.ActiveOpenRouterModel = orModel;
                    }
                }

                if (doc.RootElement.TryGetProperty("GroqModel", out var groqModelElement))
                {
                    string? groqModel = groqModelElement.GetString();
                    if (!string.IsNullOrEmpty(groqModel) &&
                        ProviderModelCatalog.ContainsModel(TranscriptionProvider.Groq, groqModel))
                    {
                        _modelConfig.ActiveGroqModel = groqModel;
                    }
                }

                if (doc.RootElement.TryGetProperty("TextFormattingMode", out var formattingElement))
                {
                    string? formattingValue = formattingElement.GetString();
                    if (!string.IsNullOrEmpty(formattingValue) &&
                        Enum.TryParse<TextFormattingMode>(formattingValue, out var mode))
                    {
                        _textFormattingMode = mode;
                    }
                }

                if (doc.RootElement.TryGetProperty("CleanupProvider", out var cleanupProviderElement))
                {
                    string? cleanupProviderValue = cleanupProviderElement.GetString();
                    if (!string.IsNullOrWhiteSpace(cleanupProviderValue) &&
                        ProviderModelCatalog.TryParseCleanupProvider(cleanupProviderValue, out var cleanupProvider))
                    {
                        _activeCleanupProvider = cleanupProvider;
                    }
                }

                if (doc.RootElement.TryGetProperty("GeminiCleanupModel", out var geminiCleanupModelElement))
                {
                    string? geminiCleanupModel = geminiCleanupModelElement.GetString();
                    if (!string.IsNullOrEmpty(geminiCleanupModel) &&
                        ProviderModelCatalog.ContainsCleanupModel(CleanupProvider.Gemini, geminiCleanupModel))
                    {
                        _activeGeminiCleanupModel = geminiCleanupModel;
                    }
                }

                if (doc.RootElement.TryGetProperty("OpenRouterCleanupModel", out var openRouterCleanupModelElement))
                {
                    string? openRouterCleanupModel = openRouterCleanupModelElement.GetString();
                    if (!string.IsNullOrEmpty(openRouterCleanupModel) &&
                        ProviderModelCatalog.ContainsCleanupModel(CleanupProvider.OpenRouter, openRouterCleanupModel))
                    {
                        _activeOpenRouterCleanupModel = openRouterCleanupModel;
                    }
                }

                if (doc.RootElement.TryGetProperty("GroqCleanupModel", out var groqCleanupModelElement))
                {
                    string? groqCleanupModel = groqCleanupModelElement.GetString();
                    if (!string.IsNullOrEmpty(groqCleanupModel) &&
                        ProviderModelCatalog.ContainsCleanupModel(CleanupProvider.Groq, groqCleanupModel))
                    {
                        _activeGroqCleanupModel = groqCleanupModel;
                    }
                }

                if (doc.RootElement.TryGetProperty("TranscriptCleanupEnabled", out var cleanupEnabledElement) &&
                    (cleanupEnabledElement.ValueKind == JsonValueKind.True || cleanupEnabledElement.ValueKind == JsonValueKind.False))
                {
                    _isTranscriptCleanupEnabled = cleanupEnabledElement.GetBoolean();
                }

                if (doc.RootElement.TryGetProperty("CleanupContextMode", out var contextModeElement))
                {
                    string? contextModeValue = contextModeElement.GetString();
                    if (!string.IsNullOrWhiteSpace(contextModeValue) &&
                        Enum.TryParse<CleanupContextMode>(contextModeValue, ignoreCase: true, out var contextMode))
                    {
                        _cleanupContextMode = contextMode;
                    }
                }

                if (doc.RootElement.TryGetProperty("CleanupIntensity", out var intensityElement))
                {
                    string? intensityValue = intensityElement.GetString();
                    if (!string.IsNullOrWhiteSpace(intensityValue) &&
                        Enum.TryParse<CleanupIntensity>(intensityValue, ignoreCase: true, out var intensity))
                    {
                        _cleanupIntensity = intensity;
                    }
                }

                if (doc.RootElement.TryGetProperty("CleanupStyleOverrideKind", out var styleKindElement))
                {
                    string? styleKindValue = styleKindElement.GetString();
                    if (!string.IsNullOrWhiteSpace(styleKindValue) &&
                        Enum.TryParse<CleanupStyleOverrideKind>(styleKindValue, ignoreCase: true, out var styleKind))
                    {
                        _cleanupStyleOverrideKind = styleKind;
                    }
                }

                if (doc.RootElement.TryGetProperty("CleanupStylePromptId", out var stylePromptIdElement))
                {
                    _cleanupStylePromptId = stylePromptIdElement.ValueKind == JsonValueKind.Number &&
                                            stylePromptIdElement.TryGetInt32(out int promptId) &&
                                            promptId > 0
                        ? promptId
                        : null;
                }

                if (doc.RootElement.TryGetProperty("CleanupPromptMigrationVersion", out var migrationVersionElement) &&
                    migrationVersionElement.ValueKind == JsonValueKind.Number &&
                    migrationVersionElement.TryGetInt32(out int migrationVersion))
                {
                    _cleanupPromptMigrationVersion = migrationVersion;
                }

                Logger.Debug("CredentialManager", $"Loaded config - Gemini: {_modelConfig.ActiveGeminiModel}, OpenRouter: {_modelConfig.ActiveOpenRouterModel}");
            }
        }
        catch (Exception ex)
        {
            Logger.Debug("CredentialManager", $"Failed to load model config: {ex.Message}");
        }
    }

    #endregion

    public TextFormattingMode GetTextFormattingMode() => _textFormattingMode;

    public void SetTextFormattingMode(TextFormattingMode mode)
    {
        if (_textFormattingMode == mode)
        {
            return;
        }

        _textFormattingMode = mode;
        SaveProviderAndModelConfig();
        Logger.Debug("CredentialManager", $"TextFormattingMode set to: {mode}");
    }

    public CleanupContextMode GetCleanupContextMode() => _cleanupContextMode;

    public void SetCleanupContextMode(CleanupContextMode mode)
    {
        if (_cleanupContextMode == mode)
        {
            return;
        }

        _cleanupContextMode = mode;
        SaveProviderAndModelConfig();
        Logger.Debug("CredentialManager", $"CleanupContextMode set to: {mode}");
    }

    public CleanupIntensity GetCleanupIntensity() => _cleanupIntensity;

    public void SetCleanupIntensity(CleanupIntensity intensity)
    {
        if (_cleanupIntensity == intensity)
        {
            return;
        }

        _cleanupIntensity = intensity;
        SaveProviderAndModelConfig();
        Logger.Debug("CredentialManager", $"CleanupIntensity set to: {intensity}");
    }

    public CleanupStyleOverrideKind GetCleanupStyleOverrideKind() => _cleanupStyleOverrideKind;

    public int? GetCleanupStylePromptId() => _cleanupStylePromptId;

    public void SetCleanupStyleOverride(CleanupStyleOverrideKind kind, int? promptId)
    {
        int? normalizedPromptId = kind == CleanupStyleOverrideKind.CustomPrompt && promptId.HasValue && promptId.Value > 0
            ? promptId.Value
            : null;

        if (_cleanupStyleOverrideKind == kind && _cleanupStylePromptId == normalizedPromptId)
        {
            return;
        }

        _cleanupStyleOverrideKind = kind;
        _cleanupStylePromptId = normalizedPromptId;
        SaveProviderAndModelConfig();
        Logger.Debug("CredentialManager", $"Cleanup style override set to: {kind} ({_cleanupStylePromptId?.ToString() ?? "none"})");
    }

    public int GetCleanupPromptMigrationVersion() => _cleanupPromptMigrationVersion;

    public void MigrateLegacyCleanupPromptSettings(PromptDatabase promptDatabase)
    {
        if (promptDatabase == null)
        {
            throw new ArgumentNullException(nameof(promptDatabase));
        }

        if (_cleanupPromptMigrationVersion >= 1)
        {
            return;
        }

        bool hasLegacyPromptConfig = File.Exists(GetLegacyPromptConfigPath());
        string? activeProfile = hasLegacyPromptConfig ? ReadLegacyActiveProfile() : null;
        int? importedCustomPromptId = ImportLegacyCustomPromptIfNeeded(promptDatabase);

        if (hasLegacyPromptConfig && !string.IsNullOrWhiteSpace(activeProfile))
        {
            ApplyLegacyActiveProfile(activeProfile, importedCustomPromptId);
        }
        else
        {
            _cleanupContextMode = CleanupContextMode.Auto;
            _cleanupIntensity = CleanupIntensity.Standard;
            _cleanupStyleOverrideKind = CleanupStyleOverrideKind.None;
            _cleanupStylePromptId = null;
        }

        _cleanupPromptMigrationVersion = 1;
        SaveProviderAndModelConfig();
        Logger.Info("CredentialManager", "Cleanup prompt settings migration completed.");
    }

    public void SaveActiveCleanupProvider(CleanupProvider provider)
    {
        _activeCleanupProvider = provider;
        SaveProviderAndModelConfig();
        Logger.Debug("CredentialManager", $"Cleanup provider switched to: {provider}");
    }

    public CleanupProvider GetActiveCleanupProvider() => _activeCleanupProvider;

    public string GetActiveCleanupModelId() => GetCleanupModelId(_activeCleanupProvider);

    public bool IsTranscriptCleanupEnabled() => _isTranscriptCleanupEnabled;

    public void SetTranscriptCleanupEnabled(bool enabled)
    {
        if (_isTranscriptCleanupEnabled == enabled)
        {
            return;
        }

        _isTranscriptCleanupEnabled = enabled;
        SaveProviderAndModelConfig();
        Logger.Debug("CredentialManager", $"Transcript cleanup enabled set to: {enabled}");
    }

    public string GetCleanupModelId(CleanupProvider provider) => provider switch
    {
        CleanupProvider.Gemini => _activeGeminiCleanupModel,
        CleanupProvider.OpenRouter => _activeOpenRouterCleanupModel,
        CleanupProvider.Groq => _activeGroqCleanupModel,
        _ => ProviderModelCatalog.GetDefaultCleanupModelId(provider)
    };

    public void SetActiveCleanupModel(CleanupProvider provider, string modelId)
    {
        if (!ProviderModelCatalog.ContainsCleanupModel(provider, modelId))
        {
            throw new ArgumentException(
                $"'{modelId}' is not a supported {provider} cleanup model.",
                nameof(modelId));
        }

        switch (provider)
        {
            case CleanupProvider.Gemini:
                _activeGeminiCleanupModel = modelId;
                break;
            case CleanupProvider.OpenRouter:
                _activeOpenRouterCleanupModel = modelId;
                break;
            case CleanupProvider.Groq:
                _activeGroqCleanupModel = modelId;
                break;
        }

        SaveProviderAndModelConfig();
        Logger.Debug("CredentialManager", $"Active cleanup model set to: {modelId}");
    }

    private string GetLegacyPromptConfigPath() => Path.Combine(_appFolder, "prompt_config.json");

    private string GetLegacyCustomPromptPath() => Path.Combine(_appFolder, "custom_prompt.txt");

    private string? ReadLegacyActiveProfile()
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(GetLegacyPromptConfigPath()));
            return doc.RootElement.TryGetProperty("ActiveProfile", out var activeProfileElement)
                ? activeProfileElement.GetString()
                : null;
        }
        catch (Exception ex)
        {
            Logger.Warn("CredentialManager", $"Failed to read legacy prompt config: {ex.Message}");
            return null;
        }
    }

    private int? ImportLegacyCustomPromptIfNeeded(PromptDatabase promptDatabase)
    {
        try
        {
            string customPromptPath = GetLegacyCustomPromptPath();
            if (!File.Exists(customPromptPath))
            {
                return null;
            }

            string content = File.ReadAllText(customPromptPath);
            if (string.IsNullOrWhiteSpace(content))
            {
                return null;
            }

            string normalizedContent = content.Trim();
            var existing = promptDatabase.FindCustomPromptByContent(normalizedContent);
            if (existing != null)
            {
                return existing.Id;
            }

            var imported = new AirType.Models.PromptProfile
            {
                Name = GetUniquePromptName(promptDatabase, PromptManager.CustomProfileName),
                Content = normalizedContent,
                IsBuiltIn = false
            };
            promptDatabase.AddPrompt(imported);
            return imported.Id;
        }
        catch (Exception ex)
        {
            Logger.Warn("CredentialManager", $"Failed to import legacy custom prompt: {ex.Message}");
            return null;
        }
    }

    private static string GetUniquePromptName(PromptDatabase promptDatabase, string preferredName)
    {
        if (!promptDatabase.PromptNameExists(preferredName))
        {
            return preferredName;
        }

        const string fallbackName = "Custom (legacy file)";
        if (!promptDatabase.PromptNameExists(fallbackName))
        {
            return fallbackName;
        }

        for (int i = 2; i < 1000; i++)
        {
            string candidate = $"{fallbackName} {i}";
            if (!promptDatabase.PromptNameExists(candidate))
            {
                return candidate;
            }
        }

        return $"{fallbackName} {Guid.NewGuid():N}";
    }

    private void ApplyLegacyActiveProfile(string activeProfile, int? importedCustomPromptId)
    {
        if (IsLegacyProfile(activeProfile, "General"))
        {
            SetMigratedCleanupSettings(CleanupContextMode.Generic, CleanupIntensity.Standard, CleanupStyleOverrideKind.None, null);
        }
        else if (IsLegacyProfile(activeProfile, "Code"))
        {
            SetMigratedCleanupSettings(CleanupContextMode.Editor, CleanupIntensity.Standard, CleanupStyleOverrideKind.None, null);
        }
        else if (IsLegacyProfile(activeProfile, "Email"))
        {
            SetMigratedCleanupSettings(CleanupContextMode.Email, CleanupIntensity.Standard, CleanupStyleOverrideKind.None, null);
        }
        else if (IsLegacyProfile(activeProfile, "Chat"))
        {
            SetMigratedCleanupSettings(CleanupContextMode.Chat, CleanupIntensity.Standard, CleanupStyleOverrideKind.None, null);
        }
        else if (IsLegacyShortCleanupProfile(activeProfile))
        {
            SetMigratedCleanupSettings(CleanupContextMode.Generic, CleanupIntensity.Light, CleanupStyleOverrideKind.None, null);
        }
        else if (IsLegacyProfile(activeProfile, "Classic"))
        {
            SetMigratedCleanupSettings(CleanupContextMode.Generic, CleanupIntensity.Standard, CleanupStyleOverrideKind.Classic, null);
        }
        else if (IsLegacyProfile(activeProfile, PromptManager.CustomProfileName) && importedCustomPromptId.HasValue)
        {
            SetMigratedCleanupSettings(CleanupContextMode.Auto, CleanupIntensity.Standard, CleanupStyleOverrideKind.CustomPrompt, importedCustomPromptId);
        }
        else
        {
            SetMigratedCleanupSettings(CleanupContextMode.Auto, CleanupIntensity.Standard, CleanupStyleOverrideKind.None, null);
        }
    }

    private void SetMigratedCleanupSettings(
        CleanupContextMode contextMode,
        CleanupIntensity intensity,
        CleanupStyleOverrideKind styleOverrideKind,
        int? stylePromptId)
    {
        _cleanupContextMode = contextMode;
        _cleanupIntensity = intensity;
        _cleanupStyleOverrideKind = styleOverrideKind;
        _cleanupStylePromptId = styleOverrideKind == CleanupStyleOverrideKind.CustomPrompt ? stylePromptId : null;
    }

    private static bool IsLegacyShortCleanupProfile(string activeProfile) =>
        IsLegacyProfile(activeProfile, "Short Cleanup") ||
        IsLegacyProfile(activeProfile, "Groq Prompt (Short)") ||
        IsLegacyProfile(activeProfile, "Groq (Optimized)") ||
        IsLegacyProfile(activeProfile, "6. Groq (Optimized)");

    private static bool IsLegacyProfile(string activeProfile, string expected) =>
        string.Equals(activeProfile.Trim(), expected, StringComparison.OrdinalIgnoreCase);
}
