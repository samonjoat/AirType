using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using AirType.Models;
using AirType.Models.Configuration;
using AirType.Models.Transcription;
using AirType.Services;
using AirType.Services.Storage;
using Xunit;

namespace AirType.Tests.Services.Storage;

public class TranscriptionLoggerProviderTraceTests
{
    [Fact]
    public async Task SaveTranscriptionAsync_WhenProviderIsGroq_WritesProviderRequestAndResponseTrace()
    {
        var logger = new TranscriptionLogger();
        var sessionId = Guid.NewGuid();
        var session = new RecordingSession
        {
            Id = sessionId,
            StartTime = new DateTime(2026, 7, 9, 10, 0, 0, DateTimeKind.Local),
            EndTime = new DateTime(2026, 7, 9, 10, 0, 3, DateTimeKind.Local),
            FilePath = Path.Combine(AirTypeStoragePaths.CanonicalRoot, "Audio_files", $"{sessionId}.wav"),
            Mode = RecordingMode.Hotkey,
            Status = RecordingStatus.Completed
        };
        var request = new GroqTranscriptionRequest
        {
            ModelId = "whisper-large-v3",
            Prompt = "Prefer AirType vocabulary.",
            Temperature = 0,
            ResponseFormat = "verbose_json"
        };
        var response = new GroqTranscriptionResponse
        {
            Text = "hello world",
            ModelUsed = "whisper-large-v3",
            Language = "en",
            RawResponseJson = "{\"text\":\"hello world\"}"
        };

        string path = await logger.SaveTranscriptionAsync(
            session,
            TranscriptionProvider.Groq,
            "whisper-large-v3",
            "hello world",
            request,
            response);

        Assert.Equal(Path.Combine(logger.LogsDirectory, $"{sessionId}.json"), path);
        Assert.True(File.Exists(path));

        using var json = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        JsonElement root = json.RootElement;

        Assert.Equal("Groq", root.GetProperty("provider").GetString());
        Assert.Equal("whisper-large-v3", root.GetProperty("model").GetString());
        Assert.Equal("hello world", root.GetProperty("transcription").GetProperty("text").GetString());
        Assert.Equal(2, root.GetProperty("transcription").GetProperty("wordCount").GetInt32());
        Assert.Equal("whisper-large-v3", root.GetProperty("apiRequest").GetProperty("modelId").GetString());
        Assert.Equal("Prefer AirType vocabulary.", root.GetProperty("apiRequest").GetProperty("prompt").GetString());
        Assert.Equal("en", root.GetProperty("apiResponse").GetProperty("language").GetString());
        Assert.Equal("{\"text\":\"hello world\"}", root.GetProperty("apiResponse").GetProperty("rawResponseJson").GetString());
    }

    [Fact]
    public void WorkflowLogging_UsesProviderNeutralRawResult()
    {
        string source = File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory,
            "..",
            "..",
            "..",
            "..",
            "AirType",
            "Services",
            "Transcription",
            "TranscriptionWorkflowService.cs"));

        Assert.DoesNotContain("OpenRouter transcription logging not yet implemented", source);
        Assert.DoesNotContain("requestObject is GeminiTranscriptionRequest", source);
        Assert.Contains("rawResult.RawProvider", source);
        Assert.Contains("rawResult.RawModelVersion", source);
        Assert.Contains("rawResult.RequestObject", source);
        Assert.Contains("rawResult.ResponseObject", source);
    }
}
