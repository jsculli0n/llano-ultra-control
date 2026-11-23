using System.Text.Json.Serialization;

namespace Llano.Core;

public enum FanMode { Fixed, SharedCurve, SeparateCurves, Automatic }
public enum LightingEffect { SolidColor, Breathing, ColorGradient, ColorChase }
public readonly record struct CurvePoint(double Celsius, int Rpm);
public sealed record Lighting(bool Enabled = true, LightingEffect Effect = LightingEffect.SolidColor,
    string Color = "#45D9AF", int Brightness = 70, int? Speed = null)
{
    public bool IsValid() => Enum.IsDefined(Effect) && Brightness is >= 0 and <= 100 &&
        Speed is null or (>= 0 and <= 3) && Color is { Length: 7 } &&
        Color[0] == '#' && Color[1..].All(Uri.IsHexDigit);
}

public sealed record SpeedLimits(int Minimum, int Maximum, int Step)
{
    public int Quantize(double value, int minimum, int maximum)
    {
        var first = Math.Max(0, (int)Math.Ceiling((minimum - Minimum) / (double)Step));
        var last = (Math.Min(Maximum, maximum) - Minimum) / Step;
        var nearest = (int)Math.Round((value - Minimum) / Step, MidpointRounding.AwayFromZero);
        return Minimum + Math.Clamp(nearest, first, last) * Step;
    }

    public bool ContainsStep(int minimum, int maximum) => minimum <= maximum && minimum >= Minimum &&
        maximum <= Maximum && (int)Math.Ceiling((minimum - Minimum) / (double)Step) <= (maximum - Minimum) / Step;
}

public sealed record FanProfile
{
    public string Name { get; init; } = "Balanced";
    public FanMode Mode { get; init; } = FanMode.SharedCurve;
    public int MinimumRpm { get; init; } = 300;
    public int MaximumRpm { get; init; } = 2800;
    public int FixedRpm { get; init; } = 1000;
    public string[] SharedSensors { get; init; } = [];
    public string? CpuSensor { get; init; }
    public string? GpuSensor { get; init; }
    public CurvePoint[] SharedCurve { get; init; } = [new(40, 700), new(60, 1100), new(75, 1800), new(85, 2800)];
    public CurvePoint[] CpuCurve { get; init; } = [new(40, 700), new(60, 1100), new(75, 1800), new(85, 2800)];
    public CurvePoint[] GpuCurve { get; init; } = [new(35, 700), new(55, 1100), new(70, 1800), new(82, 2800)];
    public Lighting Lighting { get; init; } = new();

    [JsonIgnore]
    public IEnumerable<string> ActiveSensors => Mode switch
    {
        FanMode.SharedCurve => SharedSensors,
        FanMode.SeparateCurves => new[] { CpuSensor, GpuSensor }.OfType<string>(),
        FanMode.Automatic => [CoolingPresets.CpuInput, CoolingPresets.GpuInput],
        _ => []
    };

    public string? Validate(SpeedLimits speeds, bool requireSensors)
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 64)
            return "Use a profile name between 1 and 64 characters.";
        if (!Enum.IsDefined(Mode) || !speeds.ContainsStep(MinimumRpm, MaximumRpm))
            return $"Set valid RPM limits between {speeds.Minimum} and {speeds.Maximum}.";
        if (Mode == FanMode.Fixed && (FixedRpm < MinimumRpm || FixedRpm > MaximumRpm))
            return "Fixed RPM must be within the profile limits.";
        if (SharedSensors is null || SharedSensors.Any(string.IsNullOrWhiteSpace))
            return "Select valid temperature sensors.";
        if (!ValidCurve(SharedCurve, speeds) || !ValidCurve(CpuCurve, speeds) || !ValidCurve(GpuCurve, speeds))
            return $"Each curve needs at least two increasing temperatures from 0 to 120°C, with speeds from {speeds.Minimum} to {speeds.Maximum} RPM.";
        if (requireSensors && Mode == FanMode.SharedCurve && SharedSensors.Length == 0)
            return "Select at least one temperature sensor.";
        if (requireSensors && Mode == FanMode.SeparateCurves &&
            (string.IsNullOrWhiteSpace(CpuSensor) || string.IsNullOrWhiteSpace(GpuSensor) || CpuSensor == GpuSensor))
            return "Select a CPU temperature and a separate GPU temperature.";
        if (Lighting is null || !Lighting.IsValid())
            return "Check the lighting color, brightness and effect speed.";
        return null;
    }

    private static bool ValidCurve(CurvePoint[]? points, SpeedLimits speeds)
    {
        if (points is not { Length: >= 2 and <= 32 }) return false;
        double previous = -1;
        foreach (var point in points)
        {
            if (!double.IsFinite(point.Celsius) || point.Celsius <= previous || point.Celsius is < 0 or > 120 ||
                point.Rpm < speeds.Minimum || point.Rpm > speeds.Maximum) return false;
            previous = point.Celsius;
        }
        return true;
    }
}
