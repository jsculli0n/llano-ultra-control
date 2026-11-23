using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Llano.Core;

namespace Llano.App;

public partial class MainWindow
{
    private readonly CoolingHistory liveHistory = new();
    private CoolingHistory demoHistory = new();
    private DateTimeOffset nextHistorySave;
    private Task? historySave;
    private bool historyChanged;
    private TimeSpan historyRange = TimeSpan.FromMinutes(5);

    private void LoadHistory()
    {
        try { liveHistory.Load(CoolingHistoryFile.Read(), DateTimeOffset.UtcNow); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Debug.WriteLine(error); }
        nextHistorySave = DateTimeOffset.UtcNow.AddMinutes(2);
    }

    private void RecordHistory(int? rpm, SensorReading? cpu, SensorReading? gpu)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var history = simulate ? demoHistory : liveHistory;
        bool recorded = history.Record(new(now, rpm, (float?)cpu?.Value, (float?)gpu?.Value), out bool archived);
        if (!simulate && archived) historyChanged = true;
        if (historyChanged && now >= nextHistorySave && historySave is not { IsCompleted: false })
        {
            historyChanged = false;
            nextHistorySave = now.AddMinutes(2);
            var data = liveHistory.Archive();
            historySave = Task.Run(() => WriteHistory(data));
        }
        if (recorded && (historyRange <= TimeSpan.FromMinutes(5) || archived) && SpeedHistory.IsVisible)
            ShowHistory(now);
    }

    private static void WriteHistory(CoolingSample[] data)
    {
        try { CoolingHistoryFile.Write(data); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { Debug.WriteLine(error); }
    }

    private void SaveHistoryOnExit()
    {
        historySave?.GetAwaiter().GetResult();
        if (historyChanged) WriteHistory(liveHistory.Archive());
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
