using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Llano.Core;
using Microsoft.Win32.SafeHandles;

namespace Llano.App;

internal sealed record HidIdentity(ushort Revision, ushort UsagePage, ushort Usage, ushort Input, ushort Output, ushort Feature)
{
    public override string ToString() =>
        $"374A:B101  ·  Revision {Revision:X4}  ·  Usage {UsagePage:X4}/{Usage:X4}  ·  Reports in {Input}, out {Output}, feature {Feature} bytes";
}

internal sealed record HidCooler(HidIdentity Identity, string DevicePath)
{
    private const string Filter = "HID\\VID_374A&PID_B101";
    private const string InterfacePrefix = @"\\?\hid#vid_374a&pid_b101";
    private const uint BufferSmall = 0x1A;
    private const uint HidSuccess = 0x00110000;
    private bool Supported => Identity is { Revision: 0x1601, UsagePage: 0xFF00, Usage: 1, Input: 65, Output: 65, Feature: 9 };

    public static IReadOnlyList<HidCooler> Find()
    {
        HidD_GetHidGuid(out Guid hid);
        for (int attempt = 0; attempt < 4; attempt++)
        {
            CheckConfigurationResult(CM_Get_Device_Interface_List_Size(out uint length, ref hid, null, 0));
            if (length > 512 * 1024) throw new IOException("Windows returned an unusually large HID interface list.");
            var paths = new char[length];
            uint status = CM_Get_Device_Interface_List(ref hid, null, paths, length, 0);
            if (status == BufferSmall) continue;
            CheckConfigurationResult(status);
            var matches = new List<HidCooler>();
            ReadOnlySpan<char> remaining = paths;
            while (remaining.IndexOf('\0') is > 0 and int separator)
            {
                var path = remaining[..separator];
                if (path.StartsWith(InterfacePrefix, StringComparison.OrdinalIgnoreCase) &&
                    Inspect(path.ToString()) is { } cooler) matches.Add(cooler);
                remaining = remaining[(separator + 1)..];
            }
            return matches;
        }
        throw new IOException("The HID device list kept changing. Reconnect the cooler and try again.");
    }

    public V12UltraStatus? ReadStatus()
    {
        if (!Supported) return null;
        using var handle = CreateFile(DevicePath, 0x80000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        return ReadStatus(handle, new byte[Identity.Feature]);
    }

    private static V12UltraStatus? ReadStatus(SafeFileHandle handle, byte[] report)
    {
        report[0] = 0;
        return !handle.IsInvalid && HidD_GetFeature(handle, report, report.Length) ? V12UltraStatus.Decode(report) : null;
    }

    public V12UltraStatus? SetPower(V12UltraStatus observed, bool powered, Func<bool> isCurrent, bool verify = true)
    {
        if (!Supported || !isCurrent()) return null;
        using var handle = Open(DevicePath, access: 0xC0000000, flags: 0);
        byte[] report = observed.EncodePowerCommand(powered);
        if (!isCurrent()) return null;
        WriteFeature(handle, report);
        if (!verify) return null;

        var response = new byte[Identity.Feature];
        for (int attempt = 0; attempt < 3 && isCurrent(); attempt++)
        {
            Thread.Sleep(80);
            if (!isCurrent()) return null;
            if (ReadStatus(handle, response) is { } status && status.Powered == powered) return status;
        }
        if (!isCurrent()) return null;
        throw new IOException("The cooler did not confirm its power state.");
    }

    public V12UltraStatus? Apply(V12UltraStatus observed, int? rpm, Lighting? lighting, Func<bool> isCurrent)
    {
        if (!Supported || !observed.Powered) throw new IOException("The cooler must be connected and powered on.");
        using var handle = Open(DevicePath, access: 0xC0000000, flags: 0);
        byte[] report = observed.EncodeCommand(rpm, lighting);
        if (!isCurrent()) return observed;
        WriteFeature(handle, report);

        var response = new byte[Identity.Feature];
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Thread.Sleep(80);
            if (!isCurrent()) return null;
            var updated = ReadStatus(handle, response);
            if (!isCurrent()) return updated;
            if (updated is null || (rpm is int target && (!updated.SoftwareControl || updated.SettingRpm != target))) continue;
            if (lighting is null || (updated.LightingEnabled == lighting.Enabled &&
                updated.LightingEffectIndex == (int)lighting.Effect &&
                (lighting.Effect is LightingEffect.ColorGradient or LightingEffect.ColorChase || updated.LightingColorIndex == report[5]) &&
                (lighting.Speed is null || updated.LightingSpeed == report[6]) && updated.Brightness == report[7])) return updated;
        }
        throw new IOException("The cooler did not confirm the requested setting.");
    }

    private static void WriteFeature(SafeFileHandle handle, byte[] report)
    {
        if (!HidD_SetFeature(handle, report, report.Length))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "USB write failed.");
    }

    public async Task<string> CaptureAsync(CancellationToken stop)
    {
        if (Identity is not { UsagePage: 0xFF00, Usage: 1, Input: > 0 and <= 1024 })
            return "This HID collection has no supported input report.";
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stop);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));

        using var handle = Open(DevicePath, access: 0x80000000, flags: 0x40000000);
        using var stream = new FileStream(handle, FileAccess.Read, bufferSize: 1, isAsync: true);
        var elapsed = Stopwatch.StartNew();
        var buffer = new byte[Identity.Input];
        var reports = new Dictionary<string, long>();
        int total = 0;
        try
        {
            while (total < 2048)
            {
                int read = await stream.ReadAsync(buffer, deadline.Token).ConfigureAwait(false);
                if (read == 0) break;
                reports.TryAdd(Convert.ToHexString(buffer.AsSpan(0, read)), elapsed.ElapsedMilliseconds);
                total++;
            }
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
        return $"Passive HID input collected {total} reports in {elapsed.ElapsedMilliseconds} ms.\n" +
            string.Join(Environment.NewLine, reports.Take(16).Select(report => $"+{report.Value} ms  {report.Key}"));
    }

    private static HidCooler? Inspect(string path)
    {
        using var handle = CreateFile(path, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        var attributes = new Attributes { Size = Marshal.SizeOf<Attributes>() };
        if (handle.IsInvalid || !HidD_GetAttributes(handle, ref attributes) ||
            attributes.Vendor != 0x374A || attributes.Product != 0xB101 ||
            !HidD_GetPreparsedData(handle, out var data)) return null;
        try
        {
            if (HidP_GetCaps(data, out var caps) != HidSuccess || caps is not { UsagePage: 0xFF00, Usage: 1 }) return null;
            return new(new(attributes.Version, caps.UsagePage, caps.Usage, caps.Input, caps.Output, caps.Feature), path);
        }
        finally { HidD_FreePreparsedData(data); }
    }

    private static SafeFileHandle Open(string path, uint access, uint flags)
    {
        var handle = CreateFile(path, access, 3, IntPtr.Zero, 3, flags, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        var error = new Win32Exception(Marshal.GetLastWin32Error());
        handle.Dispose();
        throw error;
    }

    private static void CheckConfigurationResult(uint result)
    {
        if (result != 0) throw new IOException($"Windows HID enumeration returned 0x{result:X} for {Filter}.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Attributes
    {
        public int Size;
        public ushort Vendor, Product, Version;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Caps
    {
        public ushort Usage, UsagePage, Input, Output, Feature;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
        public ushort LinkCount, InputButtons, InputValues, InputIndices;
        public ushort OutputButtons, OutputValues, OutputIndices, FeatureButtons, FeatureValues, FeatureIndices;
    }

    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_List_SizeW", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_Interface_List_Size(out uint length, ref Guid guid, string? device, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_Interface_ListW", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_Interface_List(ref Guid guid, string? device, [Out] char[] buffer, uint length, uint flags);
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("hid.dll")] private static extern void HidD_GetHidGuid(out Guid guid);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetAttributes(SafeFileHandle handle, ref Attributes attributes);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetPreparsedData(SafeFileHandle handle, out IntPtr data);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_FreePreparsedData(IntPtr data);
    [DllImport("hid.dll")] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_GetFeature(SafeFileHandle handle, [In, Out] byte[] buffer, int length);
    [DllImport("hid.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.U1)]
    private static extern bool HidD_SetFeature(SafeFileHandle handle, byte[] buffer, int length);
    [DllImport("hid.dll")] private static extern uint HidP_GetCaps(IntPtr data, out Caps caps);
}
