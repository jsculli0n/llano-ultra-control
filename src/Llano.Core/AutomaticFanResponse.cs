namespace Llano.Core;

public enum FanMotion { None, Steady, RampingUp, Holding, RampingDown, HighTemperature }

internal sealed class AutomaticFanResponse(SpeedLimits limits)
{
    private static readonly TimeSpan RiseDwell = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FallDwell = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan RiseInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan FallInterval = TimeSpan.FromSeconds(3);
    private TemperatureFilter cpuFilter;
    private TemperatureFilter gpuFilter;
    private TimeSpan? risingSince;
    private TimeSpan? fallingSince;
    private TimeSpan? hotSince;
    private TimeSpan? criticalSince;
    private TimeSpan? lastChange;

    public void Reset()
    {
        ResetReadings();
        lastChange = null;
    }

    public void ResetReadings()
    {
        cpuFilter = gpuFilter = default;
        risingSince = fallingSince = hotSince = criticalSince = null;
    }

    public (int Rpm, FanMotion Motion) Update(FanProfile profile, TimeSpan now,
        Temperature? cpu, Temperature? gpu, int? target, int? currentRpm)
    {
        double? filteredCpu = cpuFilter.Update(cpu);
        double? filteredGpu = gpuFilter.Update(gpu);
        int minimum = limits.Quantize(profile.MinimumRpm, profile.MinimumRpm, profile.MaximumRpm);
        int maximum = limits.Quantize(profile.MaximumRpm, profile.MinimumRpm, profile.MaximumRpm);
        int demand = Demand(filteredCpu, filteredGpu);
        bool hot = Sustained(ref hotSince, LatestAbove(cpu, 92, gpu, 84));
        bool critical = Sustained(ref criticalSince, LatestAbove(cpu, 98, gpu, 89));
        if (critical || cpu is { Celsius: >= 102 } || gpu is { Celsius: >= 92 })
        {
            risingSince = fallingSince = null;
            return Change(maximum, FanMotion.HighTemperature, now);
        }

        if (target is null && currentRpm is null)
            return Change(demand, FanMotion.Steady, now);

        int previous = target ?? limits.Quantize(currentRpm!.Value, limits.Minimum, limits.Maximum);
        lastChange ??= now;
        if (hot)
            demand = Math.Max(demand, Demand(cpu?.Celsius, gpu?.Celsius));

        int band = 2 * limits.Step;
        if (demand >= previous + band || (demand > previous && (hot || demand == maximum)))
        {
            fallingSince = null;
            risingSince ??= now;
            TimeSpan interval = hot ? TimeSpan.FromSeconds(1) : RiseInterval;
            if ((hot || now - risingSince >= RiseDwell) && now - lastChange >= interval)
                return Change(Math.Min(demand, previous + (hot ? 4 : 2) * limits.Step), FanMotion.RampingUp, now);
            return (previous, FanMotion.Holding);
        }
        risingSince = null;

        // Require extra headroom before stepping down, then let RPM fall in small steps.
        int coolerDemand = Demand(filteredCpu + 1.5, filteredGpu + 1.5);
        if (!hot && (coolerDemand <= previous - band || (demand == minimum && coolerDemand < previous)))
        {
            fallingSince ??= now;
            if (now - fallingSince >= FallDwell && now - lastChange >= FallInterval)
                return Change(Math.Max(coolerDemand, previous - limits.Step), FanMotion.RampingDown, now);
            return (previous, FanMotion.Holding);
        }
        fallingSince = null;
        return (previous, FanMotion.Steady);

        int Demand(double? cpuValue, double? gpuValue) => limits.Quantize(
            Math.Max(cpuValue is double c ? FanController.Interpolate(profile.CpuCurve, c) : 0,
                gpuValue is double g ? FanController.Interpolate(profile.GpuCurve, g) : 0),
            profile.MinimumRpm, profile.MaximumRpm);
    }

    private (int, FanMotion) Change(int rpm, FanMotion motion, TimeSpan now)
    {
        lastChange = now;
        return (rpm, motion);
    }

    private static TimeSpan? LatestAbove(Temperature? cpu, double cpuLimit, Temperature? gpu, double gpuLimit)
    {
        TimeSpan? cpuSample = cpu is { } c && c.Celsius >= cpuLimit ? c.SampledAt : null;
        TimeSpan? gpuSample = gpu is { } g && g.Celsius >= gpuLimit ? g.SampledAt : null;
        return cpuSample is null || gpuSample > cpuSample ? gpuSample : cpuSample;
    }

    private static bool Sustained(ref TimeSpan? since, TimeSpan? sample)
    {
        if (sample is not { } time)
        {
            since = null;
            return false;
        }
        if (since is null || time < since) since = time;
        return time - since >= RiseDwell;
    }

    private struct TemperatureFilter
    {
        private Temperature? previous;
        private double value;

        public double? Update(Temperature? reading)
        {
            if (reading is not { } sample)
            {
                previous = null;
                return null;
            }
            if (previous is not { } last || last.Source != sample.Source || sample.SampledAt - last.SampledAt > TimeSpan.FromSeconds(5))
                value = sample.Celsius;
            else if (sample.SampledAt <= last.SampledAt)
                return value;
            else
            {
                double seconds = (sample.SampledAt - last.SampledAt).TotalSeconds;
                value += (sample.Celsius - value) * (1 - Math.Exp(-seconds / 5));
            }
            previous = sample;
            return value;
        }
    }
}
