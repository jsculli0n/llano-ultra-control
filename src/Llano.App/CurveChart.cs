using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Llano.Core;

namespace Llano.App;

public sealed class CurveChart : FrameworkElement
{
    private CurvePoint[] first = [];
    private CurvePoint[] second = [];
    private CurvePoint? preview;
    private bool gpuPreview;

    public void Plot(CurvePoint[] primary, CurvePoint[]? secondary)
    {
        first = primary;
        second = secondary ?? [];
        InvalidateVisual();
    }

    public void SetPreview(CurvePoint? point, bool isGpu)
    {
        if (preview == point && gpuPreview == isGpu) return;
        preview = point;
        gpuPreview = isGpu;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var gridInk = TelemetryInk.Get<Pen>("ChartGridPen");
        var labelInk = TelemetryInk.Label;
        var cpuInk = TelemetryInk.Get<Pen>("ChartLinePen");
        var gpuInk = TelemetryInk.Get<Pen>("ChartSecondaryPen");
        var chart = new Rect(48, 17, Math.Max(1, ActualWidth - 60), Math.Max(1, ActualHeight - 48));
        foreach (int rpm in new[] { 300, 800, 1300, 1800, 2300, 2800 })
        {
            double y = ToPoint(new(0, rpm), chart).Y;
            drawing.DrawLine(gridInk, new(chart.Left, y), new(chart.Right, y));
            Label(drawing, rpm.ToString(CultureInfo.InvariantCulture), new(2, y - 7), labelInk);
        }
        foreach (int c in new[] { 0, 30, 60, 90, 120 })
        {
            double x = ToPoint(new(c, 300), chart).X;
            drawing.DrawLine(gridInk, new(x, chart.Top), new(x, chart.Bottom));
            Label(drawing, $"{c}°", new(x - 5, chart.Bottom + 6), labelInk);
        }
        Line(drawing, first, chart, cpuInk, TelemetryInk.Get<Brush>("ChartArea"));
        Line(drawing, second, chart, gpuInk);
        if (preview is { } estimate)
        {
            var position = ToPoint(estimate, chart);
            var ink = gpuPreview ? gpuInk : cpuInk;
            drawing.DrawLine(gridInk, new(position.X, chart.Top), new(position.X, chart.Bottom));
            drawing.DrawEllipse(Brushes.White, ink, position, 4.5, 4.5);
        }
        if (second.Length != 0)
        {
            Label(drawing, "CPU", new(chart.Left + 7, 0), cpuInk.Brush);
            Label(drawing, "GPU", new(chart.Left + 47, 0), gpuInk.Brush);
        }
        Label(drawing, "RPM", new(2, 0), labelInk);
    }

    private void Label(DrawingContext drawing, string label, Point point, Brush ink) =>
        drawing.DrawText(new FormattedText(label, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, ink, VisualTreeHelper.GetDpi(this).PixelsPerDip), point);

    private static Point ToPoint(CurvePoint point, Rect chart) =>
        new(chart.Left + chart.Width * point.Celsius / 120, chart.Bottom - chart.Height * (point.Rpm - 300) / 2500);

    private static void Line(DrawingContext drawing, CurvePoint[] points, Rect chart, Pen ink, Brush? fill = null)
    {
        if (points.Length < 2) return;
        if (fill is not null)
        {
            var area = new StreamGeometry();
            using (var path = area.Open())
            {
                path.BeginFigure(new(chart.Left, chart.Bottom), true, true);
                path.LineTo(ToPoint(points[0] with { Celsius = 0 }, chart), false, false);
                foreach (var point in points) path.LineTo(ToPoint(point, chart), false, false);
                path.LineTo(ToPoint(points[^1] with { Celsius = 120 }, chart), false, false);
                path.LineTo(new(chart.Right, chart.Bottom), false, false);
            }
            drawing.DrawGeometry(fill, null, area);
        }
        Point previous = ToPoint(points[0] with { Celsius = 0 }, chart);
        foreach (var point in points)
        {
            Point current = ToPoint(point, chart);
            drawing.DrawLine(ink, previous, current);
            drawing.DrawEllipse(ink.Brush, null, current, 3, 3);
            previous = current;
        }
        drawing.DrawLine(ink, previous, ToPoint(points[^1] with { Celsius = 120 }, chart));
    }

}
