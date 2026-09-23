using System.Runtime.InteropServices;
using Avalonia.Controls;

namespace LageBuch.App.Shared.Views;

/// <summary>
/// Darkens the native title bar on Windows 10. Avalonia sets <c>DWMWA_USE_IMMERSIVE_DARK_MODE</c>
/// from the theme variant itself, but only from Windows 11 (build 22000) on, so on Windows 10 the
/// dark app otherwise sits under a bright white caption. Windows 10 honours the same attribute —
/// undocumented, and under id 19 before 20H1 — so this sets it by hand. Everywhere else it is a
/// no-op.
/// </summary>
public static class WindowsTitleBar
{
    private const int FirstWindows11Build = 22000;

    /// <summary>
    /// The DWM attribute id that switches the caption to dark on <paramref name="build"/>, or
    /// <see langword="null"/> when that build has none (before 1809) or Avalonia already handles
    /// it (Windows 11).
    /// </summary>
    /// <param name="build">The Windows build number.</param>
    /// <returns>The attribute id to set, or <see langword="null"/> to leave the frame alone.</returns>
    public static int? AttributeFor(int build) => build switch
    {
        >= FirstWindows11Build => null,
        >= 18985 => 20, // DWMWA_USE_IMMERSIVE_DARK_MODE, 20H1 and later
        >= 17763 => 19, // same switch under its pre-20H1 id, 1809 to 1903
        _ => null,
    };

    /// <summary>
    /// Switches <paramref name="window"/>'s caption to dark on Windows 10. Call it before the
    /// window is first shown: Windows 10 does not repaint the frame when the attribute changes
    /// on a visible window.
    /// </summary>
    /// <param name="window">The window whose native frame to darken.</param>
    public static void ApplyDarkMode(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var attribute = AttributeFor(Environment.OSVersion.Version.Build);
        var hwnd = window.TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (attribute is null || hwnd == IntPtr.Zero)
        {
            return;
        }

        // The HRESULT is deliberately ignored: a build that rejects the attribute keeps the
        // light caption it would have had anyway.
        var enabled = 1;
        _ = DwmSetWindowAttribute(hwnd, attribute.Value, ref enabled, sizeof(int));
    }

    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
