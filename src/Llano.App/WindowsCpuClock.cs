using System.Runtime.InteropServices;

namespace Llano.App;

internal static class WindowsCpuClock
{
    private static readonly ProcessorPowerInformation[] Processors = new ProcessorPowerInformation[GetActiveProcessorCount(ushort.MaxValue)];

    public static double? ReadMhz()
    {
        const int processorInformation = 11;
        if (Processors.Length == 0 || CallNtPowerInformation(processorInformation, IntPtr.Zero, 0,
            Processors, (uint)(Processors.Length * Marshal.SizeOf<ProcessorPowerInformation>())) != 0) return null;
        return Processors.Where(cpu => cpu.CurrentMhz > 0).Average(cpu => (double?)cpu.CurrentMhz);
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
}
