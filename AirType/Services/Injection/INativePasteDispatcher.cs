namespace AirType.Services.Injection;

internal interface INativePasteDispatcher
{
    Task DispatchPasteAsync(IntPtr targetHandle);
}
