namespace AirType.Services.Injection;

internal enum InjectionBackendPriority
{
    ScintillaDirect = 10,
    Win32EditDirect = 20,
    UIAutomationInsert = 25,
    ClipboardPaste = 30,
    NativeTyping = 35,
    ClipboardOnly = 40
}
