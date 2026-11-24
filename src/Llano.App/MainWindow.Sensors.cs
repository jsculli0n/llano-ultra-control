using System.ComponentModel;
using System.Windows.Controls;
using Llano.Core;

namespace Llano.App;

public partial class MainWindow
{
    private void ChangeFilter(object sender, SelectionChangedEventArgs e)
    {
        if (initialized && Navigation.SelectedItem == SensorsPage) UpdateReadingsGrid();
    }

    private void ChangeSensorSearch(object sender, TextChangedEventArgs e)
    {
        if (!initialized) return;
        ClearSensorSearch.Visibility = VisibilityFor(SensorSearch.Text.Length > 0);
        UpdateReadingsGrid();
    }

    private void ClearSearch(object sender, System.Windows.RoutedEventArgs e)
    {
        SensorSearch.Clear();
        SensorSearch.Focus();
    }

    private void ChangeReadingType(object sender, System.Windows.RoutedEventArgs e)
    {
        if (initialized && Navigation.SelectedItem == SensorsPage) UpdateReadingsGrid();
    }

    internal void ShowSensors() => Navigation.SelectedItem = SensorsPage;
    private void OpenSensors(object sender, System.Windows.RoutedEventArgs e) => ShowSensors();

    private void UpdateSensorSummaries(SensorReading? cpu, SensorReading? gpu, bool scanCompleted)
    {
        SensorCpuValue.Text = cpu?.Display ?? "—";
        SensorGpuValue.Text = gpu?.Display ?? "—";
        SensorCpuCaption.Text = cpu?.Caption ?? "Temperature unavailable";
        SensorGpuCaption.Text = gpu?.Caption ?? "Temperature unavailable";
        SensorCpuCaption.ToolTip = cpu?.Caption;
        SensorGpuCaption.ToolTip = gpu?.Caption;
        CpuAccessNotice.Visibility = VisibilityFor(!simulate && scanCompleted && !hasUsableCpuTemperature);
    }

    private void UpdateReadingsGrid()
    {
        int selected = ComponentFilter.SelectedIndex;
        string query = SensorSearch.Text.Trim();
        var visible = TemperaturesOnly.IsChecked == true ? temperatureSensors : readings;
        var filtered = (selected <= 0 && query.Length == 0) ? visible :
            visible.Where(r => (selected <= 0 || (int)r.Component == selected - 1) &&
                (query.Length == 0 || r.Hardware.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    r.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                    r.Component.ToString().Contains(query, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (displayedRows.Length != filtered.Length ||
            displayedRows.Where((row, index) => !row.Matches(filtered[index])).Any())
        {
            displayedRows = filtered.Select(reading => new SensorRow(reading, IsFresh(reading))).ToArray();
            ReadingsGrid.ItemsSource = displayedRows;
        }
        else
        {
            for (int i = 0; i < displayedRows.Length; i++) displayedRows[i].Update(filtered[i], IsFresh(filtered[i]));
        }
        SensorCount.Text = readings.Length == 0 ? "Waiting for sensor readings…" :
            filtered.Length == 0 ? "No sensors match the filters." : $"{filtered.Length:N0} of {readings.Length:N0} sensors";
    }

    private bool IsFresh(SensorReading reading) =>
        reading.SampledAt <= readingTime && readingTime - reading.SampledAt <= TimeSpan.FromSeconds(5) &&
        reading.SampledAt >= sensorValidAfter;

    private bool TemperatureIsFresh(SensorReading reading) =>
        reading.Value is >= -40 and <= 150 && IsFresh(reading);

    private SensorReading? PreferredTemperature(Component component) =>
        temperatureSensors.Where(r => r.Component == component && TemperatureIsFresh(r))
            .Select(reading => (Reading: reading, Rank: TemperatureRank(reading)))
            .Where(candidate => candidate.Rank < int.MaxValue)
            .OrderBy(candidate => component == Component.Gpu ? GraphicsPriority(candidate.Reading) : 0)
            .ThenBy(candidate => candidate.Rank)
            .ThenByDescending(candidate => candidate.Reading.Id.StartsWith("lhm:", StringComparison.Ordinal))
            .ThenByDescending(candidate => candidate.Reading.Value)
            .Select(candidate => candidate.Reading).FirstOrDefault();

    private static int TemperatureRank(SensorReading reading)
    {
        string name = reading.Name.Trim().ToUpperInvariant();
        if (reading.Component == Component.Gpu)
            return name switch
            {
                "GPU CORE" or "GPU TEMPERATURE" or "GPU EDGE" or "GPU EDGE TEMPERATURE" or "TEMPERATURE" => 0,
                "GPU HOT SPOT" or "GPU HOT SPOT TEMPERATURE" or "GPU HOTSPOT TEMPERATURE" or "GPU JUNCTION TEMPERATURE" => 1,
                _ => int.MaxValue
            };
        return name switch
        {
            "CPU PACKAGE" or "CPU (TCTL/TDIE)" or "CORE (TCTL/TDIE)" or "CPU (TDIE)" or "CORE (TDIE)" or "PACKAGE" => 0,
            "CORE MAX" or "CPU DIE (AVERAGE)" or "CCDS MAX (TDIE)" => 1,
            "CPU (TCTL)" or "CORE (TCTL)" or "CORE AVERAGE" => 2,
            _ => int.MaxValue
        };
    }

    private static int GraphicsPriority(SensorReading reading)
    {
        if (reading.Id.StartsWith("lhm:/gpu-intel/", StringComparison.Ordinal) ||
            reading.Id.StartsWith("lhm:/gpu-nvidia/", StringComparison.Ordinal) ||
            reading.Hardware.Contains("GeForce", StringComparison.OrdinalIgnoreCase) ||
            reading.Hardware.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase) ||
            reading.Hardware.Contains("Radeon Pro", StringComparison.OrdinalIgnoreCase)) return 0;
        return reading.Id.StartsWith("lhm:/gpu-intel-integrated/", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
    }

    private void UpdateSensorSelectors(string[]? sharedIds = null, string? cpuId = null, string? gpuId = null, bool force = false)
    {
        var current = temperatureSensors;
        if (simulate && current.All(r => r.Id != DemoCpuId))
            current = [new(DemoCpuId, Component.Cpu, "Simulated CPU", "Package", "°C", null, clock.Elapsed), .. current];
        if (!force && sensorKeys.Length == current.Length && !current.Where((r, index) => r.Id != sensorKeys[index]).Any()) return;
        sensorKeys = current.Select(r => r.Id).ToArray();
        var options = current.ToList();
        sharedIds ??= SharedSensors.SelectedItems.Cast<SensorReading>().Select(r => r.Id).ToArray();
        if (simulate)
        {
            sharedIds = sharedIds.Where(id => id is DemoCpuId or DemoGpuId).ToArray();
            cpuId = DemoCpuId;
            gpuId = DemoGpuId;
        }
        else
        {
            cpuId ??= CpuSensor.SelectedValue as string;
            gpuId ??= GpuSensor.SelectedValue as string;
        }
        if (sharedIds.Length == 0)
            sharedIds = new[] { PreferredTemperature(Component.Cpu)?.Id, PreferredTemperature(Component.Gpu)?.Id }.OfType<string>().ToArray();
        cpuId ??= PreferredTemperature(Component.Cpu)?.Id;
        gpuId ??= PreferredTemperature(Component.Gpu)?.Id;
        foreach (string id in sharedIds.Concat(new[] { cpuId, gpuId }.OfType<string>()).Distinct())
        {
            if (options.Any(r => r.Id == id)) continue;
            var old = (SharedSensors.ItemsSource as IEnumerable<SensorReading>)?.FirstOrDefault(r => r.Id == id);
            options.Add(new(id, id == gpuId ? Component.Gpu : old?.Component ?? Component.Cpu,
                old?.Hardware ?? "Unavailable sensor", old?.Name ?? id, "°C", null, clock.Elapsed));
        }
        SharedSensors.ItemsSource = options;
        CpuSensor.ItemsSource = options.Where(r => r.Component == Component.Cpu).ToArray();
        GpuSensor.ItemsSource = options.Where(r => r.Component == Component.Gpu).ToArray();
        foreach (var item in options.Where(r => sharedIds.Contains(r.Id))) SharedSensors.SelectedItems.Add(item);
        CpuSensor.SelectedValue = cpuId;
        GpuSensor.SelectedValue = gpuId;
    }

    private sealed class SensorRow(SensorReading reading, bool fresh) : INotifyPropertyChanged
    {
        public string Id { get; } = reading.Id;
        private Component Category { get; } = reading.Component;
        public string Component => Category switch
        {
            Llano.App.Component.Cpu => "CPU",
            Llano.App.Component.Gpu => "GPU",
            _ => Category.ToString()
        };
        public string Hardware { get; } = reading.Hardware;
        public string Name { get; } = reading.Name;
        public string Display { get; private set; } = fresh ? reading.Display : "Unavailable";
        public event PropertyChangedEventHandler? PropertyChanged;

        public bool Matches(SensorReading next) => Id == next.Id && Category == next.Component &&
            Hardware == next.Hardware && Name == next.Name;

        public void Update(SensorReading next, bool isFresh)
        {
            string display = isFresh ? next.Display : "Unavailable";
            if (display == Display) return;
            Display = display;
            PropertyChanged?.Invoke(this, new(nameof(Display)));
        }
    }
}
