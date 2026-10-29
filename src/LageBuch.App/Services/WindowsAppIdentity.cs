using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LageBuch.App.Services;

/// <summary>
/// The AppUserModelID Windows groups Lagebuch's taskbar button, pins and Start entry by. The
/// installer's shortcuts carry the same ID (<c>packaging/windows/Lagebuch.wxs</c>), so a pin made
/// from the running window and the Start menu entry are one app, whichever exe path an update
/// leaves behind.
/// </summary>
internal static class WindowsAppIdentity
{
    /// <summary>Also written into every shortcut in <c>Lagebuch.wxs</c>; a test holds the two together.</summary>
    public const string UserModelId = "CodeForFire.Lagebuch";

    /// <summary>
    /// Claims <see cref="UserModelId"/> for this process. Must run before the first window opens.
    /// The HRESULT is ignored on purpose: without the ID, Windows falls back to grouping by exe
    /// path, which only costs the pin-to-Start-entry match, never a working app.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static void Apply() => _ = SetCurrentProcessExplicitAppUserModelID(UserModelId);

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);
}
