using Llano.Core;

namespace Llano.App;

internal static class CoolingHistoryFile
{
    private const int Format = 1;
    private const int RecordBytes = 20;
    private const int MaximumRecords = 8641;

    public static CoolingSample[] Read(string path)
    {
        FileStream stream;
        try { stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete); }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) { return []; }
        using var reader = new BinaryReader(stream);
        long length = stream.Length;
        if (length < sizeof(int) || (length - sizeof(int)) % RecordBytes != 0 ||
            length > sizeof(int) + MaximumRecords * RecordBytes) return [];
        if (reader.ReadInt32() != Format) return [];
        var result = new CoolingSample[(int)(length - sizeof(int)) / RecordBytes];
        for (int i = 0; i < result.Length; i++)
        {
            long ticks = reader.ReadInt64();
            if (ticks < DateTimeOffset.MinValue.Ticks || ticks > DateTimeOffset.MaxValue.Ticks) return [];
            int rpm = reader.ReadInt32();
            float cpu = reader.ReadSingle(), gpu = reader.ReadSingle();
            result[i] = new(new DateTimeOffset(ticks, TimeSpan.Zero),
                rpm is >= 0 and <= 2800 ? rpm : null, ValidTemperature(cpu), ValidTemperature(gpu));
        }
        return result;
    }

    public static void Write(string path, CoolingSample[] samples)
    {
        string directory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, $"{Guid.NewGuid():N}.tmp");
        try
        {
            using (var writer = new BinaryWriter(File.Open(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)))
            {
                writer.Write(Format);
                foreach (var sample in samples)
                {
                    writer.Write(sample.At.UtcTicks);
                    writer.Write(sample.Rpm ?? -1);
                    writer.Write(sample.Cpu ?? float.NaN);
                    writer.Write(sample.Gpu ?? float.NaN);
                }
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    private static float? ValidTemperature(float value) => value is >= -40 and <= 150 ? value : null;
}
