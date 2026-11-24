using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Llano.Core;

namespace Llano.App;

public partial class MainWindow
{
    private readonly CoolingHistory liveHistory = new();
    private readonly string historyPath = Path.Combine(AppDataFiles.DirectoryPath, AppDataFiles.HistoryName);
    private CoolingHistory demoHistory = new();
    private TimeSpan nextHistorySave;
    private Task<bool>? historySave;
    private bool historyChanged;
    private bool historyReadFailed;
    private bool historySaveFailed;
    private TimeSpan historyRange = TimeSpan.FromMinutes(5);

    private void LoadHistory()
    {
        try { liveHistory.Load(CoolingHistoryFile.Read(historyPath), DateTimeOffset.UtcNow); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            historyReadFailed = true;
            Debug.WriteLine($"Cooling history load failed ({error.GetType().Name}, 0x{error.HResult:X8}).");
        }
        nextHistorySave = clock.Elapsed + TimeSpan.FromMinutes(2);
    }

    private void RecordHistory(int? rpm, SensorReading? cpu, SensorReading? gpu)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var history = simulate ? demoHistory : liveHistory;
        bool recorded = history.Record(new(now, rpm, (float?)cpu?.Value, (float?)gpu?.Value), out bool archived);
        if (!simulate && archived) historyChanged = true;
        if (historySave is { IsCompleted: true } completed)
        {
            historySaveFailed = !completed.GetAwaiter().GetResult();
            historyChanged |= historySaveFailed;
            historySave = null;
        }
        if (historyChanged && clock.Elapsed >= nextHistorySave && historySave is null)
        {
            historyChanged = false;
            nextHistorySave = clock.Elapsed + TimeSpan.FromMinutes(2);
            var data = liveHistory.Archive();
            historySave = Task.Run(() => WriteHistory(data));
        }
        if ((archived || recorded && historyRange <= TimeSpan.FromMinutes(5)) && SpeedHistory.IsVisible)
            ShowHistory(now);
    }

    private bool WriteHistory(CoolingSample[] data)
    {
        try
        {
            CoolingHistoryFile.Write(historyPath, data);
            return true;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Debug.WriteLine($"Cooling history save failed ({error.GetType().Name}, 0x{error.HResult:X8}).");
            return false;
        }
    }

    private void SaveHistoryOnExit()
    {
        var previous = historySave;
        if (!historyChanged && previous is null) return;
        bool dirty = historyChanged;
        var data = liveHistory.Archive();
        var flush = Task.Run(async () =>
        {
            bool saved = previous is null || await previous;
            if (dirty || !saved) WriteHistory(data);
        });
        if (!flush.Wait(TimeSpan.FromSeconds(1))) Debug.WriteLine("Cooling history save exceeded the exit time budget.");
    }

    private void ChangeHistoryRange(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || HistoryRange.SelectedItem is not ComboBoxItem { Tag: string minutes }) return;
        historyRange = TimeSpan.FromMinutes(int.Parse(minutes, CultureInfo.InvariantCulture));
        ShowHistory(DateTimeOffset.UtcNow);
    }

    private void ShowHistory(DateTimeOffset now)
    {
        var history = simulate ? demoHistory : liveHistory;
        SpeedHistory.Show(history.View(now, historyRange), now, historyRange);
    }
}
