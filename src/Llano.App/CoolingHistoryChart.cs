using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Llano.Core;

namespace Llano.App;

public sealed class CoolingHistoryChart : FrameworkElement
{
    private static readonly Pen RpmLine = Line(Color.FromRgb(91, 222, 189));
    private static readonly Pen CpuLine = Line(Color.FromRgb(255, 176, 106));
    private static readonly Pen GpuLine = Line(Color.FromRgb(132, 169, 255));
    private static readonly TimeSpan Gap = TimeSpan.FromSeconds(24);
    private CoolingSample[] samples = [];
    private DateTimeOffset end = DateTimeOffset.UtcNow;
    private TimeSpan range = TimeSpan.FromMinutes(5);
    private Point? pointer;
    private readonly System.Windows.Controls.ToolTip readingTip = new();
    private DrawingGroup? chart;
    private Rect chartBounds;
    private Pen? chartGrid;

    private Rect Plot => new(43, 12, Math.Max(1, ActualWidth - 82), Math.Max(1, ActualHeight - 38));

    public CoolingHistoryChart() => ToolTip = readingTip;

    public void Show(CoolingSample[] points, DateTimeOffset now, TimeSpan window)
    {
        samples = points;
        end = now;
        range = window;
        chart = null;
        UpdateReadingTip();
        InvalidateVisual();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        pointer = e.GetPosition(this);
        UpdateReadingTip();
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        pointer = null;
        InvalidateVisual();
    }

    private void UpdateReadingTip()
    {
        if (pointer is not { } cursor || !Plot.Contains(cursor) || samples.Length == 0)
        {
            readingTip.Content = "Hover over the chart to inspect a reading.";
            return;
        }
        var area = Plot;
        var time = end - range + range * ((cursor.X - area.Left) / area.Width);
        var sample = samples.MinBy(point => Math.Abs((point.At - time).TotalSeconds));
        if ((sample.At - time).Duration() > Gap)
        {
            readingTip.Content = $"{time.LocalDateTime:g} · No data";
            return;
        }
        readingTip.Content = $"{sample.At.LocalDateTime:G}\nCooler  {Format(sample.Rpm, "RPM")}    CPU  {Format(sample.Cpu, "°C")}    GPU  {Format(sample.Gpu, "°C")}";
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        var area = Plot;
        drawing.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        var grid = TelemetryInk.Get<Pen>("ChartGridPen");
        if (chart is null || chartBounds != area || chartGrid != grid)
        {
            chart = new();
            chartBounds = area;
            chartGrid = grid;
            using (var layer = chart.Open()) DrawChart(layer, area, grid);
            chart.Freeze();
        }
        drawing.DrawDrawing(chart);
        if (pointer is { } cursor && area.Contains(cursor))
            drawing.DrawLine(grid, new(cursor.X, area.Top), new(cursor.X, area.Bottom));
    }

    private void DrawChart(DrawingContext drawing, Rect area, Pen grid)
    {
        for (int i = 0; i <= 4; i++)
        {
            double y = area.Bottom - area.Height * i / 4;
            drawing.DrawLine(grid, new(area.Left, y), new(area.Right, y));
            Label(drawing, (i * 700).ToString("N0"), new(1, y - 7), RpmLine.Brush);
            Label(drawing, $"{i * 30}°", new(area.Right + 7, y - 7), TelemetryInk.Label);
        }
        for (int i = 0; i <= 4; i++)
        {
            double x = area.Left + area.Width * i / 4;
            drawing.DrawLine(grid, new(x, area.Top), new(x, area.Bottom));
            var time = (end - range * (1 - i / 4.0)).LocalDateTime;
            string label = i == 4 ? "Now" : time.ToString(range <= TimeSpan.FromMinutes(5) ? "HH:mm:ss" : "HH:mm");
            Label(drawing, label, new(x - (i == 0 ? 0 : i == 4 ? 24 : 22), area.Bottom + 7), TelemetryInk.Label);
        }

        drawing.PushClip(new RectangleGeometry(area));
        DrawSeries(RpmLine, sample => sample.Rpm, V12UltraStatus.SpeedRange.Maximum);
        DrawSeries(CpuLine, sample => sample.Cpu, 120);
        DrawSeries(GpuLine, sample => sample.Gpu, 120);
        drawing.Pop();

        if (!samples.Any(sample => sample.Rpm.HasValue || sample.Cpu.HasValue || sample.Gpu.HasValue))
            Label(drawing, "Waiting for cooler or temperature readings", new(area.Left + 10, area.Top + 10), TelemetryInk.Label);

        void DrawSeries(Pen pen, Func<CoolingSample, float?> value, int maximum)
        {
            var path = new StreamGeometry();
            using (var segment = path.Open())
            {
                CoolingSample? previous = null;
                foreach (var sample in samples)
                {
                    if (value(sample) is not { } data) { previous = null; continue; }
                    double age = (end - sample.At).TotalSeconds;
                    var point = new Point(area.Right - area.Width * age / range.TotalSeconds,
                        area.Bottom - Math.Clamp(data / maximum, 0, 1) * area.Height);
                    if (previous is { } prior && sample.At - prior.At <= Gap)
                        segment.LineTo(point, true, false);
                    else
                    {
                        segment.BeginFigure(point, false, false);
                        drawing.DrawEllipse(pen.Brush, null, point, 1.6, 1.6);
                    }
                    previous = sample;
                }
            }
            path.Freeze();
            drawing.DrawGeometry(null, pen, path);
        }
    }

    private void Label(DrawingContext drawing, string text, Point at, Brush brush) =>
        drawing.DrawText(new FormattedText(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI"), 11, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip), at);

    private static string Format(float? number, string unit) => number is { } value ? $"{value:0.#} {unit}" : "—";

    private static Pen Line(Color color)
    {
        var pen = new Pen(new SolidColorBrush(color), 1.65);
        pen.Freeze();
        return pen;
    }
}
