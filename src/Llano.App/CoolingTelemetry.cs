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

internal static class TelemetryInk
{
    public static Brush Label => Get<Brush>("Muted");
    public static T Get<T>(string resource) => (T)Application.Current.FindResource(resource);
}
