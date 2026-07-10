using System;
using AirType.Models;

namespace AirType.Services;

/// <summary>
/// Default implementation that enforces minimum durations for specific recording modes.
/// </summary>
public sealed class RecordingValidityGuard : IRecordingValidityGuard
{
    private readonly TimeSpan _minimumHotkeyDuration;

    public RecordingValidityGuard(TimeSpan? minimumHotkeyDuration = null)
    {
        _minimumHotkeyDuration = minimumHotkeyDuration ?? TimeSpan.FromMilliseconds(700);
    }

    /// <inheritdoc />
    public TimeSpan MinimumHotkeyDuration => _minimumHotkeyDuration;

    /// <inheritdoc />
    public bool ShouldDiscard(RecordingSession session, TimeSpan actualDuration)
    {
        if (session == null)
        {
            return false;
        }

        if (session.Mode == RecordingMode.Hotkey)
        {
            return actualDuration < _minimumHotkeyDuration;
        }

        return false;
    }
}
