namespace Llano.Core;

public sealed record V12UltraStatus(int SettingRpm, int LightingEffectIndex, int LightingColorIndex,
    bool SoftwareControl = false, bool Powered = true, bool LightingEnabled = true, int LightingSpeed = 2, int Brightness = 20)
{
    public static SpeedLimits SpeedRange { get; } = new(300, 2800, 100);
    public static IReadOnlyList<string> Palette { get; } = Array.AsReadOnly(new[] { "#EF2616", "#4080FF", "#36E34E", "#A454E6", "#FFA72F" });
    private static readonly int[] LegacyPalette = [0xFF3030, 0x4080FF, 0x36E34E, 0xA454E6, 0xFFD83D];
    public int BrightnessPercent => (Brightness * 100 + 127) / 255;

    public static V12UltraStatus? Decode(ReadOnlySpan<byte> report)
    {
        if (report.Length != 9 || report[0] != 0 || report[1] is not (0x80 or 0x88) ||
            report[2] > 100 || report[3] > 1 || (report[4] & 0x7F) > 3 || report[5] > 4 || report[6] > 3) return null;

        if (Checksum(report[..^1]) != report[^1]) return null;

        return new(300 + 25 * report[2], report[4] & 0x7F, report[5], report[1] == 0x80,
            report[3] == 0, (report[4] & 0x80) == 0, report[6], report[7]);
    }

    public byte[] EncodeCommand(int? rpm, Lighting? lighting)
    {
        int target = rpm ?? SettingRpm;
        if (target is < 300 or > 2800 || (target - 300) % 25 != 0) throw new ArgumentOutOfRangeException(nameof(rpm));
        if (lighting is not null && !lighting.IsValid())
            throw new ArgumentOutOfRangeException(nameof(lighting));
        int mode = lighting is null ? LightingEffectIndex : (int)lighting.Effect;
        int onOff = (lighting?.Enabled ?? LightingEnabled) ? 0 : 0x80;
        int color = lighting is null ? LightingColorIndex : PaletteIndex(lighting.Color);
        int brightness = lighting is null ? Brightness : (lighting.Brightness * 255 + 50) / 100;
        byte[] report = [0, rpm.HasValue && !SoftwareControl ? (byte)1 : (byte)0, (byte)((target - 300) / 25),
            Powered ? (byte)0 : (byte)1, (byte)(mode | onOff), (byte)color,
            (byte)(lighting?.Speed ?? LightingSpeed), (byte)brightness, 0];
        report[^1] = Checksum(report.AsSpan(0, report.Length - 1));
        return report;
    }

    public byte[] EncodePowerCommand(bool powered) => (this with { Powered = powered }).EncodeCommand(null, null);

    public static int PaletteIndex(string color)
    {
        int rgb = Convert.ToInt32(color[1..], 16);
        int index = Enumerable.Range(0, Palette.Count).FirstOrDefault(i => Palette[i].Equals(color, StringComparison.OrdinalIgnoreCase), -1);
        return index >= 0 ? index : Enumerable.Range(0, LegacyPalette.Length).MinBy(i => Distance(rgb, LegacyPalette[i]));
    }

    private static int Distance(int a, int b)
    {
        int r = (a >> 16) - (b >> 16);
        int g = ((a >> 8) & 255) - ((b >> 8) & 255);
        int blue = (a & 255) - (b & 255);
        return r * r + g * g + blue * blue;
    }

    private static byte Checksum(ReadOnlySpan<byte> bytes)
    {
        int sum = 0;
        foreach (byte value in bytes) sum += value;
        return (byte)(255 - (sum & 255));
    }
}
