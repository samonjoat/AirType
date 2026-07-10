using System;
using System.Reflection;
using AirType.Models;
using AirType.Services.Storage;
using Xunit;

namespace AirType.Tests.Services.Storage;

public class TranscriptionHistoryManagerTests
{
    [Fact]
    public void ShouldReplaceExistingEntry_keeps_successful_entry_when_incoming_same_id_entry_failed()
    {
        var existing = new TranscriptionHistoryEntry
        {
            Id = Guid.NewGuid(),
            TranscribedText = "successful transcript",
            WordCount = 2,
            Timestamp = new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Local)
        };

        var incomingFailure = new TranscriptionHistoryEntry
        {
            Id = existing.Id,
            TranscribedText = string.Empty,
            WordCount = 0,
            Timestamp = existing.Timestamp
        };

        bool shouldReplace = InvokeShouldReplaceExistingEntry(existing, incomingFailure);

        Assert.False(shouldReplace);
    }

    [Fact]
    public void ShouldReplaceExistingEntry_allows_successful_rerun_to_replace_failed_entry()
    {
        var existingFailure = new TranscriptionHistoryEntry
        {
            Id = Guid.NewGuid(),
            TranscribedText = string.Empty,
            WordCount = 0,
            Timestamp = new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Local)
        };

        var incomingSuccess = new TranscriptionHistoryEntry
        {
            Id = existingFailure.Id,
            TranscribedText = "rerun succeeded",
            WordCount = 2,
            Timestamp = existingFailure.Timestamp
        };

        bool shouldReplace = InvokeShouldReplaceExistingEntry(existingFailure, incomingSuccess);

        Assert.True(shouldReplace);
    }

    [Fact]
    public void ShouldReplaceExistingEntry_allows_successful_entry_to_receive_injection_status_update()
    {
        var existing = new TranscriptionHistoryEntry
        {
            Id = Guid.NewGuid(),
            TranscribedText = "successful transcript",
            WordCount = 2,
            InjectionSucceeded = false,
            InjectionErrorMessage = "Injection failed - copied to clipboard",
            Timestamp = new DateTime(2026, 4, 18, 12, 0, 0, DateTimeKind.Local)
        };

        var incomingUpdate = new TranscriptionHistoryEntry
        {
            Id = existing.Id,
            TranscribedText = "successful transcript",
            WordCount = 2,
            InjectionSucceeded = true,
            InjectionErrorMessage = null,
            Timestamp = existing.Timestamp
        };

        bool shouldReplace = InvokeShouldReplaceExistingEntry(existing, incomingUpdate);

        Assert.True(shouldReplace);
    }

    private static bool InvokeShouldReplaceExistingEntry(TranscriptionHistoryEntry existing, TranscriptionHistoryEntry incoming)
    {
        MethodInfo? method = typeof(TranscriptionHistoryManager).GetMethod(
            "ShouldReplaceExistingEntry",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        object? result = method!.Invoke(null, new object[] { existing, incoming });
        return Assert.IsType<bool>(result);
    }
}
