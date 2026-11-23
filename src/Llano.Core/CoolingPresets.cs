namespace Llano.Core;

public enum CoolingMode { Quiet, Balanced, Boost, Manual }

public static class CoolingPresets
{
    public const string CpuInput = "automatic:cpu";
    public const string GpuInput = "automatic:gpu";

    public static FanProfile Create(CoolingMode mode, int manualRpm) => mode switch
    {
        CoolingMode.Quiet => new()
        {
            Name = "Quiet", Mode = FanMode.Automatic, MinimumRpm = 300,
            CpuCurve = [new(52, 300), new(68, 600), new(79, 1000), new(88, 1650), new(96, 2800)],
            GpuCurve = [new(47, 300), new(61, 650), new(72, 1100), new(81, 1800), new(88, 2800)]
        },
        CoolingMode.Balanced => new()
        {
            Mode = FanMode.Automatic, MinimumRpm = 600,
            CpuCurve = [new(53, 600), new(67, 900), new(78, 1450), new(88, 2150), new(96, 2800)],
            GpuCurve = [new(48, 600), new(60, 900), new(71, 1500), new(81, 2200), new(88, 2800)]
        },
        CoolingMode.Boost => new()
        {
            Name = "Boost", Mode = FanMode.Automatic, MinimumRpm = 1000,
            CpuCurve = [new(53, 1000), new(64, 1400), new(75, 1950), new(85, 2500), new(93, 2800)],
            GpuCurve = [new(48, 1000), new(57, 1400), new(67, 1950), new(77, 2500), new(85, 2800)]
        },
        CoolingMode.Manual => new() { Name = "Manual", Mode = FanMode.Fixed, FixedRpm = manualRpm },
        _ => throw new ArgumentOutOfRangeException(nameof(mode))
    };
}
