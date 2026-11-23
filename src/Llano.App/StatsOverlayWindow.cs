using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Llano.Core;

namespace Llano.App;

internal sealed class StatsOverlayWindow : Window
{
    public static readonly (StatsMetrics Metric, string Group, string Unit, string Option)[] Fields =
    [
        (StatsMetrics.Fps, "FPS", "", "FPS"),
        (StatsMetrics.GpuUsage, "GPU", "%", "GPU usage"),
        (StatsMetrics.GpuTemperature, "GPU", "°C", "GPU temperature"),
        (StatsMetrics.GpuClock, "GPU", "GHz", "GPU clock"),
        (StatsMetrics.CpuUsage, "CPU", "%", "CPU usage"),
        (StatsMetrics.CpuTemperature, "CPU", "°C", "CPU temperature"),
        (StatsMetrics.CpuClock, "CPU", "GHz", "CPU clock"),
        (StatsMetrics.MemoryUsage, "Memory", "%", "Memory usage"),
        (StatsMetrics.CoolerRpm, "Cooler", "RPM", "Cooler speed")
    ];

    private static readonly Brush Frame = Brush(255, 54, 66, 82);
    private static readonly Brush LabelInk = Brush(255, 173, 188, 208);
    private static readonly Brush ValueInk = Brush(255, 236, 243, 250);
    private static readonly Brush AccentInk = Brush(255, 91, 222, 189);
    private static readonly Brush FpsInk = Brush(255, 255, 183, 75);
    private readonly Border frame;
    private readonly WrapPanel cells = new();
    private readonly Dictionary<StatsMetrics, (TextBlock Text, string Unit)> values = new();
    private StatsOverlaySettings? settings;
    private AppTheme theme;

    public StatsOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        WindowStartupLocation = WindowStartupLocation.Manual;
        ShowActivated = false;
        ShowInTaskbar = false;
        Topmost = true;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Focusable = false;
        Content = frame = new Border
        {
            CornerRadius = new CornerRadius(4), Padding = new Thickness(5, 1, 5, 1), Child = cells
        };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            const int extendedStyle = -20, transparent = 0x20, toolWindow = 0x80, noActivate = 0x08000000;
            SetWindowLong(hwnd, extendedStyle, GetWindowLong(hwnd, extendedStyle) | transparent | toolWindow | noActivate);
        };
        SizeChanged += (_, _) => Position();
    }

    public void Configure(StatsOverlaySettings next, AppTheme appTheme)
    {
        if (settings == next && theme == appTheme) return;
        settings = next;
        theme = appTheme;
        Color surface = next.FollowTheme && Application.Current.FindResource("Panel") is SolidColorBrush panel
            ? panel.Color : Color.FromRgb(17, 20, 25);
        frame.Background = Brush((byte)Math.Round(next.BackgroundOpacity * 2.55), surface.R, surface.G, surface.B);
        frame.BorderBrush = next.FollowTheme ? TelemetryInk.Get<Brush>("AccentBorder") : Frame;
        frame.BorderThickness = new Thickness(next.ShowBorder ? 1 : 0);
        Brush accent = next.FollowTheme ? TelemetryInk.Get<Brush>("Accent") : AccentInk;
        cells.Children.Clear();
        values.Clear();
        cells.Orientation = next.Vertical ? Orientation.Vertical : Orientation.Horizontal;
        double size = next.Size switch { StatsSize.Compact => 10, StatsSize.Large => 15, _ => 12 };
        foreach (var group in Fields.Where(field => next.Metrics.HasFlag(field.Metric)).GroupBy(field => field.Group))
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(6, 2, 6, 2) };
            row.Children.Add(new TextBlock { Text = group.Key, FontSize = size - 1, Foreground = LabelInk, Margin = new Thickness(0, 0, 5, 0),
                VerticalAlignment = VerticalAlignment.Center });
            bool first = true;
            foreach (var (metric, _, unit, _) in group)
            {
                if (!first) row.Children.Add(new TextBlock { Text = "/", FontSize = size, Foreground = LabelInk, Margin = new Thickness(3, 0, 3, 0) });
                first = false;
                var value = new TextBlock
                {
                    Text = "—", FontSize = size, FontWeight = FontWeights.SemiBold,
                    Foreground = metric switch { StatsMetrics.Fps => next.FollowTheme ? accent : FpsInk, StatsMetrics.CoolerRpm => accent, _ => ValueInk }
                };
                row.Children.Add(value);
                values.Add(metric, (value, unit));
            }
            cells.Children.Add(row);
        }
        Position();
    }

    public void SetReading(StatsMetrics metric, double? value, bool estimated = false)
    {
        if (!values.TryGetValue(metric, out var cell)) return;
        cell.Text.Text = value is not double number ? "—" : cell.Unit switch
        {
            "%" => $"{number:0}%",
            "°C" => $"{number:0}°C",
            "GHz" => $"{(estimated ? "≈" : "")}{number:0.00}GHz",
            "" => $"{number:0}",
            _ => $"{number:0} RPM"
        };
    }

    public void Position()
    {
        if (settings is not { } current) return;
        Rect area = SystemParameters.WorkArea;
        const int padding = 12;
        MaxWidth = Math.Max(1, area.Width - 2 * padding);
        Left = current.Position switch
        {
            StatsPosition.TopLeft or StatsPosition.BottomLeft => area.Left + padding,
            StatsPosition.TopCenter or StatsPosition.BottomCenter => area.Left + (area.Width - ActualWidth) / 2,
            _ => Math.Max(area.Left + padding, area.Right - ActualWidth - padding)
        };
        Top = current.Position is StatsPosition.BottomLeft or StatsPosition.BottomCenter or StatsPosition.BottomRight
            ? area.Bottom - ActualHeight - padding : area.Top + padding;
    }

    private static Brush Brush(byte alpha, byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, red, green, blue));
        brush.Freeze();
        return brush;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(IntPtr window, int index, int value);
}
