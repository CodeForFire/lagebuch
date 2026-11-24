using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using LageBuch.App.Shared.Controls;

namespace LageBuch.Acceptance.Tests;

public class TacticalSignControlRenderTests
{
    private const int Left = 20;
    private const int Top = 20;

    // The sign's circle, as the source set draws it: radius 64 around (128, 128) of 256 units.
    private const double Radius = (TacticalSignControl.BodyHeight / 2) + 1;
    private const double Scale = Radius / 64;

    private static readonly Color Yellow = Color.FromRgb(0xFF, 0xFF, 0x00);

    [AvaloniaFact]
    public void A_Bereitstellungsraum_is_the_DV_102_sign_a_yellow_circle_holding_a_vehicle_outline()
    {
        var window = Show(new TacticalSignControl { IsBereitstellungsraum = true });

        using var frame = window.CaptureRenderedFrame()!;

        // Above the vehicle's outline, inside the circle.
        AssertNear(Yellow, PixelAt(frame, At(128, 83)));

        // The outline is not filled, and no text stands in it: its inside is the circle's yellow.
        for (var x = 100; x <= 156; x += 4)
        {
            for (var y = 124; y <= 144; y += 4)
            {
                AssertNear(Yellow, PixelAt(frame, At(x, y)));
            }
        }

        // The outline's bottom edge is a black line.
        var (bx, by) = At(128, 151);
        var darkest = Enumerable.Range(by - 2, 5).Select(y => Brightness(PixelAt(frame, (bx, y)))).Min();
        Assert.True(darkest < 110, $"No black line where the outline's bottom edge should be (darkest {darkest}).");
    }

    [AvaloniaFact]
    public void A_Bereitstellungsraum_shows_its_name_right_of_the_sign()
    {
        var window = Show(new TacticalSignControl { IsBereitstellungsraum = true, Title = "BR Nord" });

        using var frame = window.CaptureRenderedFrame()!;

        // The name stands right of the circle, dark on its white backing.
        var right = (int)Math.Ceiling(Left + TacticalSignControl.SymbolCenter.X + Radius) + 6;
        var ink = Enumerable.Range(right, 50)
            .SelectMany(x => Enumerable.Range(Top, (int)TacticalSignControl.BodyHeight).Select(y => (x, y)))
            .Min(p => Brightness(PixelAt(frame, p)));
        Assert.True(ink < 110, $"No name right of the sign (darkest {ink}).");
    }

    // A point of the source set's 256-unit drawing, in the frame's pixels.
    private static (int X, int Y) At(double x, double y) =>
        ((int)Math.Floor(Left + TacticalSignControl.SymbolCenter.X + ((x - 128) * Scale)),
         (int)Math.Floor(Top + TacticalSignControl.SymbolCenter.Y + ((y - 128) * Scale)));

    private static Window Show(TacticalSignControl sign)
    {
        Canvas.SetLeft(sign, Left);
        Canvas.SetTop(sign, Top);
        var window = new Window
        {
            Content = new Canvas { Background = Brushes.White, Children = { sign } },
            Width = 200,
            Height = 100,
        };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return window;
    }

    private static int Brightness(Color c) => (c.R + c.G + c.B) / 3;

    private static void AssertNear(Color expected, Color actual) =>
        Assert.True(
            Math.Abs(expected.R - actual.R) <= 24 && Math.Abs(expected.G - actual.G) <= 24 && Math.Abs(expected.B - actual.B) <= 24,
            $"Expected about {expected}, was {actual}.");

    private static Color PixelAt(WriteableBitmap frame, (int X, int Y) p)
    {
        using var buffer = frame.Lock();
        Assert.Equal(PixelFormat.Rgba8888, buffer.Format);
        var offset = (p.Y * buffer.RowBytes) + (p.X * 4);
        return Color.FromArgb(
            Marshal.ReadByte(buffer.Address, offset + 3),
            Marshal.ReadByte(buffer.Address, offset),
            Marshal.ReadByte(buffer.Address, offset + 1),
            Marshal.ReadByte(buffer.Address, offset + 2));
    }
}
