using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Llano.Core;

namespace Llano.App;

public sealed class FanSpeedGauge : FrameworkElement
{
    private int? rpm;

    public void ShowSpeed(int? speed)
    {
        if (rpm == speed) return;
        rpm = speed;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        double radius = Math.Min(ActualWidth, ActualHeight) / 2 - 10;
        if (radius <= 0) return;
        drawing.DrawGeometry(null, TelemetryInk.Get<Pen>("GaugeTrackPen"), Arc(center, radius, 270));
        if (rpm is { } value)
        {
            double fraction = Math.Clamp((double)value / V12UltraStatus.SpeedRange.Maximum, 0, 1);
            drawing.DrawGeometry(null, TelemetryInk.Get<Pen>("GaugeFillPen"), Arc(center, radius, fraction * 270));
        }
        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var label = new FormattedText("RPM", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 12, TelemetryInk.Label, dpi);
        drawing.DrawText(label, new(center.X - label.Width / 2, center.Y - label.Height / 2));
    }

    private static StreamGeometry Arc(Point center, double radius, double sweep)
    {
        Point At(double degrees)
        {
            double radians = degrees * Math.PI / 180;
            return new(center.X + radius * Math.Cos(radians), center.Y + radius * Math.Sin(radians));
        }

        var arc = new StreamGeometry();
        using (var context = arc.Open())
        {
            context.BeginFigure(At(135), false, false);
            context.ArcTo(At(135 + sweep), new(radius, radius), 0, sweep > 180, SweepDirection.Clockwise, true, false);
        }
        arc.Freeze();
        return arc;
    }
}

public sealed class FanSpeedHistory : FrameworkElement
{
    private const int Seconds = 60;
    private readonly Queue<(TimeSpan At, int? Rpm)> samples = new(Seconds + 1);
    private TimeSpan? lastSample;

    public void Record(TimeSpan now, int? rpm)
    {
        if (lastSample is { } last && now - last < TimeSpan.FromMilliseconds(900)) return;
        lastSample = now;
        samples.Enqueue((now, rpm));
        while (samples.TryPeek(out var oldest) && now - oldest.At > TimeSpan.FromSeconds(Seconds)) samples.Dequeue();
        if (IsVisible) InvalidateVisual();
    }

    public void Clear()
    {
        samples.Clear();
        lastSample = null;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var area = new Rect(48, 9, Math.Max(1, ActualWidth - 52), Math.Max(1, ActualHeight - 28));
        var speedRange = V12UltraStatus.SpeedRange;
        var grid = TelemetryInk.Get<Pen>("ChartGridPen");
        var speedLine = TelemetryInk.Get<Pen>("ChartLinePen");
        foreach (int rpm in new[] { speedRange.Maximum, 1500, speedRange.Minimum })
        {
            double y = MapRpm(rpm);
            drawing.DrawLine(grid, new(area.Left, y), new(area.Right, y));
            Label(drawing, rpm.ToString("N0", CultureInfo.CurrentCulture), new(1, y - 7));
        }
        Label(drawing, "60s ago", new(area.Left, area.Bottom + 4));
        Label(drawing, "Now", new(Math.Max(area.Left, area.Right - 24), area.Bottom + 4));
        if (lastSample is not { } now) return;
        Point? previous = null;
        TimeSpan previousTime = default;
        foreach (var sample in samples)
        {
            if (sample.Rpm is not { } rpm)
            {
                previous = null;
                continue;
            }
            double age = (now - sample.At).TotalSeconds;
            var point = new Point(area.Right - area.Width * age / Seconds, MapRpm(rpm));
            if (previous is { } prior && sample.At - previousTime < TimeSpan.FromSeconds(3))
                drawing.DrawLine(speedLine, prior, point);
            else drawing.DrawEllipse(speedLine.Brush, null, point, 2.5, 2.5);
            previous = point;
            previousTime = sample.At;
        }

        double MapRpm(int rpm) => area.Bottom -
            area.Height * Math.Clamp((double)(rpm - speedRange.Minimum) / (speedRange.Maximum - speedRange.Minimum), 0, 1);
    }

    private void Label(DrawingContext drawing, string text, Point location) =>
        drawing.DrawText(new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, TelemetryInk.Label, VisualTreeHelper.GetDpi(this).PixelsPerDip), location);
}

internal static class TelemetryInk
{
    public static Brush Label => Get<Brush>("Muted");
    public static T Get<T>(string resource) => (T)Application.Current.FindResource(resource);
}
