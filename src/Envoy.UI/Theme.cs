using System.Windows.Media;

namespace Envoy.UI;

// Cyberpunk palette shared across views. Each brush is Frozen so it's safe to
// reference from any thread (e.g. background workers updating status labels)
// without WPF cloning per-use. Use with `using static Envoy.UI.Theme;` from
// code-behind to refer to colors as `Cyan`, `Green`, etc.
internal static class Theme
{
    public static readonly SolidColorBrush Cyan = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0xF0, 0xFF)));
    public static readonly SolidColorBrush Magenta = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x00, 0xFF)));
    public static readonly SolidColorBrush Green = Freeze(new SolidColorBrush(Color.FromRgb(0x39, 0xFF, 0x14)));
    public static readonly SolidColorBrush Red = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0x07, 0x3A)));
    public static readonly SolidColorBrush Yellow = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xE6, 0x00)));
    public static readonly SolidColorBrush Gray = Freeze(new SolidColorBrush(Color.FromRgb(0x88, 0x92, 0xA4)));
    public static readonly SolidColorBrush Muted = Freeze(new SolidColorBrush(Color.FromRgb(0x88, 0x92, 0xA4)));
    public static readonly SolidColorBrush TextFg = Freeze(new SolidColorBrush(Color.FromRgb(0xE0, 0xE6, 0xF0)));
    public static readonly SolidColorBrush Surface = Freeze(new SolidColorBrush(Color.FromRgb(0x11, 0x18, 0x27)));
    public static readonly SolidColorBrush Background = Freeze(new SolidColorBrush(Color.FromRgb(0x0A, 0x0E, 0x17)));
    public static readonly SolidColorBrush BorderColor = Freeze(new SolidColorBrush(Color.FromRgb(0x1A, 0x3A, 0x4A)));
    public static readonly SolidColorBrush NavActiveBg = Freeze(new SolidColorBrush(Color.FromArgb(40, 0, 240, 255)));
    public static readonly SolidColorBrush CyanHover = Freeze(new SolidColorBrush(Color.FromArgb(100, 0, 240, 255)));
    public static readonly SolidColorBrush Transparent = Freeze(new SolidColorBrush(Colors.Transparent));

    private static SolidColorBrush Freeze(SolidColorBrush b)
    {
        b.Freeze();
        return b;
    }
}
