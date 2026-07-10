using AirType.Services;
using Xunit;

namespace AirType.Tests.Services;

public sealed class WidgetStateManagerTests
{
    [Fact]
    public void InitialState_UsesIdleMinimalFootprint()
    {
        using var stateManager = new WidgetStateManager();

        Assert.Equal(WidgetStateManager.WidgetState.IdleMinimal, stateManager.CurrentState);
        Assert.Equal(40, stateManager.CurrentDimensions.Width);
        Assert.Equal(10, stateManager.CurrentDimensions.Height);
        Assert.Equal(0.6, stateManager.CurrentOpacity);
        Assert.False(stateManager.ShowControls);
    }

    [Fact]
    public void HoverState_UsesIdleHoverFootprintWithoutRecordingControls()
    {
        using var stateManager = new WidgetStateManager();

        stateManager.EnterHoverState();

        Assert.Equal(WidgetStateManager.WidgetState.IdleHover, stateManager.CurrentState);
        Assert.Equal(100, stateManager.CurrentDimensions.Width);
        Assert.Equal(30, stateManager.CurrentDimensions.Height);
        Assert.Equal(0.65, stateManager.CurrentOpacity);
        Assert.False(stateManager.ShowControls);
    }

    [Fact]
    public void RecordingState_KeepsExistingActiveFootprintAndUnattendedControls()
    {
        using var stateManager = new WidgetStateManager();

        stateManager.StartUnattendedRecording();

        Assert.Equal(WidgetStateManager.WidgetState.Recording, stateManager.CurrentState);
        Assert.Equal(100, stateManager.CurrentDimensions.Width);
        Assert.Equal(30, stateManager.CurrentDimensions.Height);
        Assert.Equal(1.0, stateManager.CurrentOpacity);
        Assert.True(stateManager.ShowControls);
    }

    [Theory]
    [InlineData(WidgetStateManager.WidgetState.Transcribing)]
    [InlineData(WidgetStateManager.WidgetState.Cleaning)]
    [InlineData(WidgetStateManager.WidgetState.FailureFlash)]
    public void ProcessingStates_UseActiveFootprintAndHideControls(WidgetStateManager.WidgetState state)
    {
        using var stateManager = new WidgetStateManager();

        stateManager.StartUnattendedRecording();
        stateManager.TransitionTo(state);

        Assert.Equal(state, stateManager.CurrentState);
        Assert.Equal(100, stateManager.CurrentDimensions.Width);
        Assert.Equal(30, stateManager.CurrentDimensions.Height);
        Assert.Equal(1.0, stateManager.CurrentOpacity);
        Assert.False(stateManager.ShowControls);
    }

    [Fact]
    public void TransitionFromWorkflow_ReturnsProcessingStatesToIdleMinimal()
    {
        using var stateManager = new WidgetStateManager();

        stateManager.TransitionToCleaning();
        stateManager.TransitionFromWorkflow();

        Assert.Equal(WidgetStateManager.WidgetState.IdleMinimal, stateManager.CurrentState);
    }
}
