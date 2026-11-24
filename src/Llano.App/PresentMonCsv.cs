using System.Globalization;

namespace Llano.App;

internal readonly record struct PresentMonCsv(int ColumnCount, int DisplayedTimeColumn)
{
    public static bool TryReadHeader(string line, out PresentMonCsv format)
    {
        format = default;
        var columns = line.Split(',');
        int time = Array.IndexOf(columns, "DisplayedTime");
        if (time < 3 || columns[0] != "Application" || columns[1] != "ProcessID" ||
            columns[2] != "SwapChainAddress") return false;
        format = new(columns.Length, time);
        return true;
    }

    public bool TryReadFrame(ReadOnlySpan<char> line, uint foreground, out ulong chain, out double interval)
    {
        chain = 0;
        interval = 0;
        if (ColumnCount == 0 || foreground == 0) return false;

        ReadOnlySpan<char> time = default, address = default;
        // PresentMon writes the executable name verbatim, so it may contain commas.
        for (int column = ColumnCount - 1; column >= 1; column--)
        {
            int comma = line.LastIndexOf(',');
            if (comma < 0) return false;
            var field = line[(comma + 1)..];
            if (column == DisplayedTimeColumn) time = field;
            if (column == 2) address = field;
            if (column == 1 && (!uint.TryParse(field, NumberStyles.None, CultureInfo.InvariantCulture, out uint process) ||
                process != foreground)) return false;
            line = line[..comma];
        }

        return address.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            ulong.TryParse(address[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out chain) &&
            double.TryParse(time, NumberStyles.Float, CultureInfo.InvariantCulture, out interval) &&
            double.IsFinite(interval) && interval is > 0 and <= 2500;
    }
}
