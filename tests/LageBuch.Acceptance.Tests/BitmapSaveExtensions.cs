using Avalonia.Media.Imaging;

namespace LageBuch.Acceptance.Tests;

internal static class BitmapSaveExtensions
{
    public static void SavePng(this Bitmap bitmap, string path) =>
        bitmap.Save(path, new PngBitmapEncoderOptions());
}
