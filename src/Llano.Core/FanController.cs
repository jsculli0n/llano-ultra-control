namespace Llano.Core;

public readonly record struct Temperature(double Celsius, TimeSpan SampledAt)
{
    internal static readonly TimeSpan MaximumAge = TimeSpan.FromSeconds(5);
    public string? Source { get; init; }
    public bool IsFresh(TimeSpan now) => double.IsFinite(Celsius) && Celsius is >= -40 and <= 150 &&
        now >= SampledAt && now - SampledAt <= MaximumAge;
}

public enum FanState { Paused, WaitingForCooler, WaitingForSensors, Fixed, Smart, SensorFallback, DialOverride, StabilizingSensors }
public readonly record struct FanDecision(FanState State, int? TargetRpm, int? NewCommand)
{
    public FanMotion Motion { get; init; }
}

public sealed class FanController(SpeedLimits limits)
{
    private static readonly TimeSpan Cooldown = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SignInWarmup = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan StartSensorLossGrace = TimeSpan.FromSeconds(3);
    private const double CoolingHysteresis = 3;
    private readonly AutomaticFanResponse automatic = new(limits);
    private FanProfile? profile;
    private int? target;
    private TimeSpan? coolingSince;
    private TimeSpan? armedAt;
    private bool waitForStableTemperatures;
    private TimeSpan? firstWarmupSample;
    private TimeSpan? latestWarmupSample;
    private TimeSpan? missingSince;
    private bool dialOverride;

    public bool Enabled => profile is not null;

    public void Start(FanProfile next, bool waitForStableTemperatures = false)
    {
        if (next.Validate(limits, requireSensors: true) is { } error) throw new ArgumentException(error, nameof(next));
        profile = next;
        this.waitForStableTemperatures = waitForStableTemperatures;
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
        armedAt = firstWarmupSample = latestWarmupSample = missingSince = null;
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
                return Missing(current, now);
            }
            if (!Ready(cpu.Value.SampledAt)) return new(FanState.StabilizingSensors, null, null);
            armedAt ??= now;
            missingSince = null;
            bool confirmExtreme = InSignInWarmup(now);
            var (rpm, motion) = automatic.Update(current, now, cpu, FreshReading(CoolingPresets.GpuInput), target, currentRpm, confirmExtreme);
            return Apply(FanState.Smart, rpm) with { Motion = motion };
        }
        TimeSpan oldestSample = now;
        foreach (string id in current.ActiveSensors)
        {
            if (FreshReading(id) is not { } reading) return Missing(current, now);
            if (reading.SampledAt < oldestSample) oldestSample = reading.SampledAt;
        }
        if (!Ready(oldestSample)) return new(FanState.StabilizingSensors, null, null);
        armedAt ??= now;
        missingSince = null;
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

    private FanDecision Missing(FanProfile current, TimeSpan now)
    {
        coolingSince = null;
        firstWarmupSample = latestWarmupSample = null;
        if (!armedAt.HasValue) return new(FanState.WaitingForSensors, null, null);
        if (InSignInWarmup(missingSince ?? now))
        {
            missingSince ??= now;
            if (now - missingSince.Value < StartSensorLossGrace)
                return new(FanState.StabilizingSensors, null, null);
        }
        return Apply(FanState.SensorFallback, Rpm(current, current.MaximumRpm));
    }

    private bool InSignInWarmup(TimeSpan time) =>
        waitForStableTemperatures && armedAt is { } started && time - started < SignInWarmup;

    private bool Ready(TimeSpan sample)
    {
        if (!waitForStableTemperatures || armedAt.HasValue) return true;
        if (latestWarmupSample is not { } previous || sample < previous || sample - previous > Temperature.MaximumAge)
            firstWarmupSample = sample;
        latestWarmupSample = sample;
        return sample - (firstWarmupSample ?? sample) >= SignInWarmup;
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
