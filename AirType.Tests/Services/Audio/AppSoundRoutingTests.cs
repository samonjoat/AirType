using System;
using System.Reflection;
using AirType.Services;
using AirType.Services.Audio;
using AirType.Services.Transcription;
using Xunit;

namespace AirType.Tests.Services.Audio;

public sealed class AppSoundRoutingTests
{
    [Fact]
    public void CreateCapsuleStateSoundRequest_WhenRecordingStartsWithMuteEnabled_ReturnsRecordingStartedWithMuteCallback()
    {
        var method = GetAppMethod("CreateCapsuleStateSoundRequest");
        var muteCallback = new Action(() => { });

        var request = InvokeAppMethod<AppSoundRequest>(
            method,
            new WidgetStateChangedEventArgs(WidgetStateManager.WidgetState.IdleHover, WidgetStateManager.WidgetState.Recording),
            false,
            true,
            muteCallback);

        Assert.Equal(AppSoundState.RecordingStarted, request.State);
        Assert.Same(muteCallback, request.OnStartPlaybackCompleted);
    }

    [Fact]
    public void CreateWorkflowResultSoundRequest_WhenInjectionWasVerifiedWithoutClipboardFallback_ReturnsInjectionSucceeded()
    {
        var method = GetAppMethod("CreateWorkflowResultSoundRequest");

        var request = InvokeAppMethod<AppSoundRequest>(
            method,
            new TranscriptionWorkflowResult
            {
                Success = true,
                InjectionSucceeded = true,
                ClipboardFallbackUsed = false
            });

        Assert.Equal(AppSoundState.InjectionSucceeded, request.State);
        Assert.True(request.InjectionVerified);
        Assert.False(request.ClipboardFallbackUsed);
    }

    [Theory]
    [InlineData(true, false, false, AppSoundState.Silent)]
    [InlineData(true, true, true, AppSoundState.Silent)]
    [InlineData(false, false, false, AppSoundState.WorkflowFailed)]
    public void CreateWorkflowResultSoundRequest_WhenWorkflowDoesNotHaveVerifiedInjection_RoutesExpectedState(
        bool success,
        bool injectionSucceeded,
        bool clipboardFallbackUsed,
        AppSoundState expectedState)
    {
        var method = GetAppMethod("CreateWorkflowResultSoundRequest");

        var request = InvokeAppMethod<AppSoundRequest>(
            method,
            new TranscriptionWorkflowResult
            {
                Success = success,
                InjectionSucceeded = injectionSucceeded,
                ClipboardFallbackUsed = clipboardFallbackUsed
            });

        Assert.Equal(expectedState, request.State);
    }

    [Fact]
    public void CreateWorkflowResultSoundRequest_WhenWorkflowWasCancelled_ReturnsWorkflowCanceled()
    {
        var method = GetAppMethod("CreateWorkflowResultSoundRequest");

        var request = InvokeAppMethod<AppSoundRequest>(
            method,
            new TranscriptionWorkflowResult
            {
                WasCancelled = true
            });

        Assert.Equal(AppSoundState.WorkflowCanceled, request.State);
    }

    [Fact]
    public void ServiceContainer_ExposesAppSoundPolicyBuiltOnNotificationSoundService()
    {
        var property = typeof(ServiceContainer).GetProperty("AppSoundPolicy", BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(property);
        Assert.Equal(typeof(IAppSoundPolicy), property!.PropertyType);

        using var container = new ServiceContainer();
        var policy = Assert.IsAssignableFrom<IAppSoundPolicy>(property.GetValue(container));
        var transportField = policy.GetType().GetField("_notificationSoundService", BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(transportField);
        Assert.Same(container.NotificationSoundService, transportField!.GetValue(policy));
    }

    private static MethodInfo GetAppMethod(string methodName)
    {
        var method = typeof(global::AirType.App).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return method;
    }

    private static T InvokeAppMethod<T>(MethodInfo method, params object?[] arguments)
    {
        var result = method.Invoke(null, arguments);
        return Assert.IsType<T>(result);
    }
}
