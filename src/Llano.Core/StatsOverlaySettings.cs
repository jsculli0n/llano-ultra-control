namespace Llano.Core;

[Flags]
public enum StatsMetrics
{
    None = 0,
    CoolerRpm = 1,
    CpuTemperature = 2,
    GpuTemperature = 4,
    CpuUsage = 8,
    GpuUsage = 16,
    CpuClock = 32,
    GpuClock = 64,
    MemoryUsage = 128,
    Fps = 256,
    All = 511
}

public enum StatsPosition { TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight }
public enum StatsSize { Compact, Standard, Large }

public sealed record StatsOverlaySettings
{
    public bool Enabled { get; init; }
    public StatsMetrics Metrics { get; init; } = StatsMetrics.CoolerRpm | StatsMetrics.CpuTemperature | StatsMetrics.GpuTemperature;
    public StatsPosition Position { get; init; } = StatsPosition.TopRight;
    public StatsSize Size { get; init; } = StatsSize.Standard;
    public bool Vertical { get; init; }
    public bool FollowTheme { get; init; }
    public bool ShowBorder { get; init; } = true;
    public int BackgroundOpacity { get; init; } = 85;

    public bool IsValid() => (Metrics & ~StatsMetrics.All) == 0 && Enum.IsDefined(Position) && Enum.IsDefined(Size) &&
        BackgroundOpacity is >= 0 and <= 100;
}
