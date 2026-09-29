using System.Drawing;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace YoyoClawCompanion.Services;

internal static class ProviderIconService
{
    public static ImageSource? Load(string provider, IslandSettings settings)
    {
        var executable = provider.ToLowerInvariant() switch
        {
            "yoyo" => ApplicationLocator.FindYoyoExecutable(settings.YoyoExecutablePath),
            "codex" => ApplicationLocator.FindCodexDesktopExecutable(settings.CodexExecutablePath, IsCodexProcess),
            "workbuddy" => ApplicationLocator.FindWorkBuddyExecutable(settings.WorkBuddyExecutablePath),
            _ => null
        };
        if (string.IsNullOrWhiteSpace(executable)) return null;

        try
        {
            using var icon = Icon.ExtractAssociatedIcon(executable);
            if (icon is null) return null;
            var source = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty,
                BitmapSizeOptions.FromWidthAndHeight(64, 64));
            source.Freeze();
            return source;
        }
        catch { return null; }
    }

    private static bool IsCodexProcess(System.Diagnostics.Process process)
    {
        try { return process.MainModule?.FileName?.Contains("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) == true; }
        catch { return process.MainWindowTitle.Contains("ChatGPT", StringComparison.OrdinalIgnoreCase); }
    }

    public static ImageSource FromGeometry(Geometry geometry)
    {
        var drawing = new GeometryDrawing(null, new System.Windows.Media.Pen(System.Windows.Media.Brushes.White, 2), geometry);
        var image = new DrawingImage(drawing);
        image.Freeze();
        return image;
    }
}
