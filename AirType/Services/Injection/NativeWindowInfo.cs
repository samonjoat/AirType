namespace AirType.Services.Injection;

internal sealed record NativeWindowInfo(
    IntPtr Handle,
    string ClassName,
    bool IsVisible,
    bool IsEnabled);
