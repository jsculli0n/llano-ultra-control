using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Text;
using LibreHardwareMonitor.Hardware;
using Llano.Core;

namespace Llano.App;

internal enum Component { Cpu, Gpu, Memory, Motherboard, Storage }

internal sealed record SensorReading(string Id, Component Component, string Hardware, string Name,
    string Unit, double? Value, TimeSpan SampledAt)
{
    public string Caption => $"{Hardware} / {Name}";
    public string Display => Value is { } value ? $"{value:0.#} {Unit}".TrimEnd() : "Unavailable";
}

internal sealed class SensorSnapshot(SensorReading[] readings, string status)
{
    public static SensorSnapshot Empty { get; } = new([], "Waiting for hardware readings…");

    public SensorReading[] Readings { get; } = readings;
    public string Status { get; } = status;
    public SensorReading[] TemperatureSensors { get; } = readings.Where(reading => reading.Unit == "°C").ToArray();
}

internal sealed class SensorMonitor
{
    private readonly Stopwatch clock;
    private SensorSnapshot snapshot = SensorSnapshot.Empty;
    public SensorSnapshot Snapshot => Volatile.Read(ref snapshot);

    public SensorMonitor(Stopwatch clock) => this.clock = clock;

    public async Task RunAsync(CancellationToken stop)
    {
        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true
        };
        string? error = null;
        try { computer.Open(); }
        catch (Exception e) { error = e.Message; }

        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            do
            {
                var readings = new List<SensorReading>();
                var sampledAt = clock.Elapsed;
                if (error is null)
                {
                    try
                    {
                        foreach (var hardware in computer.Hardware)
                        {
                            Component? component = hardware.HardwareType switch
                            {
                                HardwareType.Cpu => Component.Cpu,
                                HardwareType.GpuAmd or HardwareType.GpuIntel or HardwareType.GpuNvidia => Component.Gpu,
                                HardwareType.Memory => Component.Memory,
                                HardwareType.Motherboard or HardwareType.SuperIO => Component.Motherboard,
                                HardwareType.Storage => Component.Storage,
                                _ => null
                            };
                            if (component is { } kind) ReadHardware(hardware, kind, readings, sampledAt);
                        }
                    }
                    catch (Exception e) when (!stop.IsCancellationRequested)
                    {
                        Debug.WriteLine(e);
                    }
                }
                bool shared = HwinfoTemperatures.TryRead(readings, clock.Elapsed);
                var unique = readings.DistinctBy(reading => reading.Id)
                    .OrderBy(reading => reading.Component).ThenBy(reading => reading.Hardware)
                    .ThenBy(reading => reading.Name).ToArray();
                string status = shared ? "LibreHardwareMonitor and HWiNFO shared memory" : "LibreHardwareMonitor";
                if (!unique.Any(r => r is { Component: Component.Cpu, Unit: "°C", Value: not null }))
                    status += " · CPU temperature unavailable. See Sensors for help.";
                Volatile.Write(ref snapshot, new(unique, error is null ? status : $"LibreHardwareMonitor: {error}. {status}"));
            } while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { }
        finally
        {
            try { computer.Close(); }
            catch (Exception e) { Debug.WriteLine(e); }
        }
    }

    private static void ReadHardware(IHardware hardware, Component component,
        List<SensorReading> readings, TimeSpan sampledAt)
    {
        try
        {
            hardware.Update();
            foreach (var sensor in hardware.Sensors)
            {
                string? unit = sensor.SensorType switch
                {
                    SensorType.Temperature => "°C",
                    SensorType.Load or SensorType.Control or SensorType.Level => "%",
                    SensorType.Fan => "RPM",
                    SensorType.Clock => "MHz",
                    SensorType.Power => "W",
                    SensorType.Voltage => "V",
                    SensorType.Data => "GB",
                    SensorType.SmallData => "MB",
                    SensorType.Throughput => "B/s",
                    _ => null
                };
                if (unit is not null)
                {
                    double? value = sensor.Value is { } v && float.IsFinite(v) ? v : null;
                    readings.Add(new($"lhm:{sensor.Identifier}", component, hardware.Name, sensor.Name, unit, value, sampledAt));
                }
            }
        }
        catch (Exception e) { Debug.WriteLine(e); }

        foreach (var child in hardware.SubHardware) ReadHardware(child, component, readings, sampledAt);
    }
}

// Based on the MIT-licensed Llano Ultra Monitor shared-memory reader.
internal static class HwinfoTemperatures
{
    private const uint Signature = 0x53695748;
    private const int HeaderLength = 44;
    private const int SensorPrefixSize = 264;
    private const int ReadingPrefixSize = 292;
    private const int MaximumLength = 16 * 1024 * 1024;

    public static bool TryRead(List<SensorReading> readings, TimeSpan monotonicNow)
    {
        try
        {
            using var map = MemoryMappedFile.OpenExisting(@"Global\HWiNFO_SENS_SM2", MemoryMappedFileRights.Read);
            using var view = map.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
            if (view.Capacity < HeaderLength || view.ReadUInt32(0) != Signature || view.ReadUInt32(4) is not (1 or 2))
                return false;

            long timestamp = view.ReadInt64(12);
            long age = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - timestamp;
            if (timestamp <= 0 || age is < -1 or > 5) return false;
            TimeSpan measuredAt = monotonicNow - TimeSpan.FromSeconds(Math.Max(0, age));
            uint sensorOffset = view.ReadUInt32(20), sensorStride = view.ReadUInt32(24), sensorCount = view.ReadUInt32(28);
            uint readingOffset = view.ReadUInt32(32), readingStride = view.ReadUInt32(36), readingCount = view.ReadUInt32(40);
            if (!SectionFits(view.Capacity, sensorOffset, sensorStride, sensorCount, SensorPrefixSize) ||
                !SectionFits(view.Capacity, readingOffset, readingStride, readingCount, ReadingPrefixSize) ||
                sensorOffset + (long)sensorStride * sensorCount > readingOffset) return false;

            var buffer = new byte[128];
            var devices = new (string Name, uint Id, uint Instance, Component? Kind)[sensorCount];
            for (int i = 0; i < devices.Length; i++)
            {
                long p = sensorOffset + (long)i * sensorStride;
                string name = ReadText(view, p + 8, buffer, 128);
                Component? kind = name.StartsWith("CPU [", StringComparison.OrdinalIgnoreCase) ? Component.Cpu :
                    name.StartsWith("GPU [", StringComparison.OrdinalIgnoreCase) ? Component.Gpu : null;
                devices[i] = (name, view.ReadUInt32(p), view.ReadUInt32(p + 4), kind);
            }

            var shared = new List<SensorReading>();
            for (int i = 0; i < readingCount; i++)
            {
                long p = readingOffset + (long)i * readingStride;
                if (view.ReadUInt32(p) != 1) continue;
                uint index = view.ReadUInt32(p + 4);
                if (index >= devices.Length || devices[index].Kind is not { } kind) continue;
                var device = devices[index];
                double value = view.ReadDouble(p + 284);
                string unit = ReadText(view, p + 268, buffer, 16);
                if (unit.EndsWith('F')) value = (value - 32) * 5 / 9;
                else if (!unit.EndsWith('C')) continue;
                if (!double.IsFinite(value) || value is < -40 or > 150) continue;

                string name = ReadText(view, p + 12, buffer, 128);
                uint id = view.ReadUInt32(p + 8);
                shared.Add(new($"hwinfo:{device.Id:X}:{device.Instance:X}:{id:X}", kind,
                    $"{device.Name} (HWiNFO)", name, "°C", value, measuredAt));
            }
            if (view.ReadUInt32(0) != Signature || view.ReadInt64(12) != timestamp ||
                view.ReadUInt32(28) != sensorCount || view.ReadUInt32(40) != readingCount) return false;
            readings.AddRange(shared);
            return shared.Count > 0;
        }
        catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool SectionFits(long capacity, uint offset, uint stride, uint count, int minimumStride) =>
        offset >= HeaderLength && stride >= minimumStride && stride <= MaximumLength && count <= 16384 &&
        offset + (long)stride * count <= Math.Min(capacity, MaximumLength);

    private static string ReadText(MemoryMappedViewAccessor view, long position, byte[] buffer, int length)
    {
        view.ReadArray(position, buffer, 0, length);
        int end = Array.IndexOf(buffer, (byte)0, 0, length);
        return Encoding.UTF8.GetString(buffer, 0, end < 0 ? length : end).Trim();
    }
}
