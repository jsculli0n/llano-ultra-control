using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Llano.App;

internal sealed class WindowsCpuClock
{
    private const uint FormatDoubleWithoutCap = 0x00000200 | 0x00008000;
    private IntPtr query;
    private IntPtr performance;
    private double nominalMhz;
    private long nextSample;
    private double? lastMhz;

    public double? ReadMhz()
    {
        long now = Stopwatch.GetTimestamp();
        if (now < nextSample) return lastMhz;
        nextSample = now + Stopwatch.Frequency * 3 / 4;

        if (query == IntPtr.Zero)
        {
            if (!Open())
            {
                Stop();
                nextSample = now + Stopwatch.Frequency * 30;
            }
            return null;
        }

        // The first collection primes the counter. Later samples measure performance
        // relative to nominal speed, including percentages above 100 during boost.
        if (PdhCollectQueryData(query) != 0 ||
            PdhGetFormattedCounterValue(performance, FormatDoubleWithoutCap, IntPtr.Zero, out var sample) != 0 ||
            sample.Status > 1 || !double.IsFinite(sample.Value) || sample.Value <= 0)
            return lastMhz = null;
        return lastMhz = nominalMhz * sample.Value / 100;
    }

    public void Stop()
    {
        if (query != IntPtr.Zero) PdhCloseQuery(query);
        query = IntPtr.Zero;
        nextSample = 0;
        lastMhz = null;
    }

    private bool Open()
    {
        if (ReadNominalMhz() is not double nominal ||
            PdhOpenQuery(IntPtr.Zero, UIntPtr.Zero, out query) != 0 ||
            PdhAddEnglishCounter(query, @"\Processor Information(_Total)\% Processor Performance", UIntPtr.Zero, out performance) != 0 ||
            PdhCollectQueryData(query) != 0) return false;
        nominalMhz = nominal;
        return true;
    }

    private static double? ReadNominalMhz()
    {
        const int processorInformation = 11;
        var processors = new ProcessorPowerInformation[GetActiveProcessorCount(ushort.MaxValue)];
        if (processors.Length == 0 || CallNtPowerInformation(processorInformation, IntPtr.Zero, 0,
            processors, (uint)(processors.Length * Marshal.SizeOf<ProcessorPowerInformation>())) != 0) return null;
        return processors.Where(cpu => cpu.MaxMhz > 0).Average(cpu => (double?)cpu.MaxMhz);
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct FormattedCounter
    {
        [FieldOffset(0)] public uint Status;
        [FieldOffset(8)] public double Value;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessorPowerInformation
    {
        public uint Number, MaxMhz, CurrentMhz, MhzLimit, MaxIdleState, CurrentIdleState;
    }

    [DllImport("kernel32.dll")]
    private static extern uint GetActiveProcessorCount(ushort group);

    [DllImport("powrprof.dll")]
    private static extern uint CallNtPowerInformation(int level, IntPtr input, uint inputLength,
        [Out] ProcessorPowerInformation[] output, uint outputLength);

    [DllImport("pdh.dll", EntryPoint = "PdhOpenQueryW")]
    private static extern uint PdhOpenQuery(IntPtr dataSource, UIntPtr userData, out IntPtr handle);

    [DllImport("pdh.dll", EntryPoint = "PdhAddEnglishCounterW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint PdhAddEnglishCounter(IntPtr handle, string path, UIntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr handle);

    [DllImport("pdh.dll")]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, IntPtr type, out FormattedCounter value);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr handle);
}
