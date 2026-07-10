using System;
using AirType.Models;

namespace AirType.Services;

/// <summary>
/// Provides policy checks that determine whether a recording should be rejected.
/// </summary>
public interface IRecordingValidityGuard
{
    /// <summary>
    /// Minimum length for hotkey-triggered recordings before they are considered valid.
    /// </summary>
    TimeSpan MinimumHotkeyDuration { get; }

    /// <summary>
    /// Determines whether the supplied recording session should be discarded.
    /// </summary>
    /// <param name="session">The recording session being evaluated.</param>
    /// <param name="actualDuration">The actual duration of the session.</param>
    /// <returns><c>true</c> if the session should be discarded; otherwise, <c>false</c>.</returns>
    bool ShouldDiscard(RecordingSession session, TimeSpan actualDuration);
}
