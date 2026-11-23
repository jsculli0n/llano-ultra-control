namespace Llano.Core;

public readonly record struct Temperature(double Celsius, TimeSpan SampledAt)
{
    public string? Source { get; init; }
    public bool IsFresh(TimeSpan now) => double.IsFinite(Celsius) && Celsius is >= -40 and <= 150 &&
        now >= SampledAt && now - SampledAt <= TimeSpan.FromSeconds(5);
}

public enum FanState { Paused, WaitingForCooler, WaitingForSensors, Fixed, Smart, SensorFallback, DialOverride }
public readonly record struct FanDecision(FanState State, int? TargetRpm, int? NewCommand)
{
    public FanMotion Motion { get; init; }
}

public sealed class FanController(SpeedLimits limits)
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(10);
    private const double CoolingHysteresis = 3;
    private readonly AutomaticFanResponse automatic = new(limits);
    private FanProfile? profile;
    private int? target;
    private TimeSpan? coolingSince;
    private bool armed;
    private bool dialOverride;

    public bool Enabled => profile is not null;

    public void Start(FanProfile next)
    {
        if (next.Validate(limits, requireSensors: true) is { } error) throw new ArgumentException(error, nameof(next));
        profile = next;
        dialOverride = false;
        InvalidateConnection();
    }

    public void Pause()
    {
        profile = null;
        dialOverride = false;
        InvalidateConnection();
    }

    public void InvalidateConnection()
    {
        armed = false;
        target = null;
        coolingSince = null;
        automatic.Reset();
    }

    public FanDecision Update(TimeSpan now, IReadOnlyDictionary<string, Temperature> readings,
        bool connected, bool physicalOverride = false, int? currentRpm = null)
    {
        if (physicalOverride && profile is not null)
        {
            Pause();
            dialOverride = true;
        }
        if (profile is not { } current) return new(dialOverride ? FanState.DialOverride : FanState.Paused, null, null);
        if (!connected)
        {
            InvalidateConnection();
            return new(FanState.WaitingForCooler, null, null);
        }
        if (current.Mode == FanMode.Fixed)
            return Apply(FanState.Fixed, Rpm(current, current.FixedRpm));

        if (current.Mode == FanMode.Automatic)
        {
            var cpu = FreshReading(CoolingPresets.CpuInput);
            if (cpu is null)
            {
                automatic.ResetReadings();
                return Missing(current);
            }
            armed = true;
            var (rpm, motion) = automatic.Update(current, now, cpu, FreshReading(CoolingPresets.GpuInput), target, currentRpm);
            return Apply(FanState.Smart, rpm) with { Motion = motion };
        }
        if (current.ActiveSensors.Any(id => FreshReading(id) is null)) return Missing(current);

        armed = true;
        int requested = RequestedRpm(0);
        if (target is not { } previous || requested >= previous)
        {
            coolingSince = null;
            return Apply(FanState.Smart, requested);
        }

        int withHysteresis = RequestedRpm(CoolingHysteresis);
        if (withHysteresis >= previous)
        {
            coolingSince = null;
            return new(FanState.Smart, previous, null);
        }
        coolingSince ??= now;
        if (now - coolingSince < Cooldown) return new(FanState.Smart, previous, null);

        coolingSince = null;
        return Apply(FanState.Smart, withHysteresis);

        Temperature? FreshReading(string id) =>
            readings.TryGetValue(id, out var sample) && sample.IsFresh(now) ? sample : null;

        int RequestedRpm(double offset) => Rpm(current, Evaluate(current, readings, offset));
    }

    private FanDecision Missing(FanProfile current)
    {
        coolingSince = null;
        return armed ? Apply(FanState.SensorFallback, Rpm(current, current.MaximumRpm)) :
            new(FanState.WaitingForSensors, null, null);
    }

    public static double Interpolate(IReadOnlyList<CurvePoint> points, double temperature)
    {
        if (temperature <= points[0].Celsius) return points[0].Rpm;
        for (int i = 1; i < points.Count; i++)
        {
            var next = points[i];
            if (temperature > next.Celsius) continue;
            var previous = points[i - 1];
            var fraction = (temperature - previous.Celsius) / (next.Celsius - previous.Celsius);
            return previous.Rpm + (next.Rpm - previous.Rpm) * fraction;
        }
        return points[^1].Rpm;
    }

    private int Rpm(FanProfile current, double raw) => limits.Quantize(raw, current.MinimumRpm, current.MaximumRpm);

    private static double Evaluate(FanProfile current, IReadOnlyDictionary<string, Temperature> readings, double offset) =>
        current.Mode == FanMode.SharedCurve
            ? Interpolate(current.SharedCurve, current.SharedSensors.Max(id => readings[id].Celsius) + offset)
            : Math.Max(Interpolate(current.CpuCurve, readings[current.CpuSensor!].Celsius + offset),
                Interpolate(current.GpuCurve, readings[current.GpuSensor!].Celsius + offset));

    private FanDecision Apply(FanState state, int rpm)
    {
        var changed = target != rpm;
        target = rpm;
        return new(state, rpm, changed ? rpm : null);
    }
}
