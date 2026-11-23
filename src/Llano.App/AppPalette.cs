using System.Windows;
using System.Windows.Media;
using Llano.Core;

namespace Llano.App;

internal static class AppPalette
{
    private static readonly string[] Keys =
    [
        "WindowFill", "Sidebar", "Panel", "Input", "Hover", "Selected", "Line", "Text", "Muted",
        "Accent", "OnAccent", "AccentBorder", "HeroStart", "HeroMiddle", "PlotGrid", "Secondary"
    ];

    public static void Apply(AppTheme theme)
    {
        string[] colors = theme switch
        {
            AppTheme.Violet => ["101115", "15161C", "1B1D25", "242733", "303444", "342A53", "3A3D4E",
                "F2F1F7", "B1B2C3", "C7B5FA", "211832", "554770", "353056", "221F35", "34374A", "9DDFC9"],
            AppTheme.IceBlue => ["0D141C", "121D27", "17232F", "1F2E3D", "293C4E", "203C55", "3D5366",
                "EFF7FF", "B0C4D8", "91D5FF", "102436", "406383", "234764", "192E42", "344B60", "F6BD91"],
            AppTheme.Graphite => ["101112", "171819", "202123", "282A2D", "35373A", "373B40", "4B4F55",
                "F5F5F5", "BFC2C8", "E1E5ED", "202226", "646A74", "383C45", "282B32", "41454D", "A3D9BA"],
            _ => ["131113", "1A1518", "221B20", "2D2228", "3B2C32", "4C272D", "4B3940",
                "FFF3F2", "C9B5BC", "FF8B91", "2F1016", "73424D", "4F242F", "341D25", "463239", "9DE1D0"]
        };
        var resources = Application.Current.Resources;
        for (int i = 0; i < Keys.Length; i++)
            resources[Keys[i]] = Brush(colors[i]);
        resources["HeroFill"] = Gradient(colors[12], colors[13], colors[2]);
        resources["LightStage"] = Gradient(colors[13], colors[1]);
        resources["GaugeTrackPen"] = Pen((Brush)resources["AccentBorder"], 8);
        resources["GaugeFillPen"] = Pen((Brush)resources["Accent"], 8);
        resources["ChartLinePen"] = Pen((Brush)resources["Accent"], 2.4);
        resources["ChartSecondaryPen"] = Pen((Brush)resources["Secondary"], 2.4);
        resources["ChartGridPen"] = Pen((Brush)resources["PlotGrid"], 1);
        var accent = ((SolidColorBrush)resources["Accent"]).Color;
        var area = new LinearGradientBrush(Color.FromArgb(70, accent.R, accent.G, accent.B),
            Color.FromArgb(0, accent.R, accent.G, accent.B), 90);
        area.Freeze();
        resources["ChartArea"] = area;
    }

    private static SolidColorBrush Brush(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#" + hex));
        brush.Freeze();
        return brush;
    }

    private static Brush Gradient(params string[] colors)
    {
        var brush = new LinearGradientBrush { StartPoint = new(1, 0), EndPoint = new(0.15, 1) };
        for (int i = 0; i < colors.Length; i++)
            brush.GradientStops.Add(new(Brush(colors[i]).Color, (double)i / (colors.Length - 1)));
        brush.Freeze();
        return brush;
    }

    private static Pen Pen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        pen.Freeze();
        return pen;
    }
}
