using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Llano.Core;

namespace Llano.App;

public partial class CurveEditor : UserControl
{
    private const int MaximumPoints = 32;
    private readonly ObservableCollection<PointRow> rows = [];
    private CurvePoint[]? points;

    public CurveEditor()
    {
        InitializeComponent();
        Points.ItemsSource = rows;
    }

    public event EventHandler? PointsChanged;
    internal CurvePoint[]? Values => points;

    public void Load(IEnumerable<CurvePoint> values)
    {
        rows.Clear();
        foreach (var value in values) rows.Add(new(value, ValidatePoints));
        ValidatePoints();
    }

    private void AddPoint(object sender, RoutedEventArgs e)
    {
        if (points is not { Length: >= 2 and < MaximumPoints } valid) return;
        int index = Points.SelectedIndex >= 0 ? Points.SelectedIndex : valid.Length - 1;
        var left = valid[index];
        CurvePoint point;
        if (index == valid.Length - 1 && left.Celsius < 120)
        {
            point = left with { Celsius = Math.Min(left.Celsius + 5, 120) };
        }
        else
        {
            if (index == valid.Length - 1) left = valid[--index];
            var right = valid[index + 1];
            point = new((left.Celsius + right.Celsius) / 2, (left.Rpm + right.Rpm) / 2);
        }
        var row = new PointRow(point, ValidatePoints);
        rows.Insert(index + 1, row);
        Points.SelectedItem = row;
        Points.ScrollIntoView(row);
        ValidatePoints();
    }

    private void RemovePoint(object sender, RoutedEventArgs e)
    {
        if (rows.Count > 2 && Points.SelectedItem is PointRow row)
        {
            rows.Remove(row);
            ValidatePoints();
        }
    }

    private void SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateButtons();

    private void UpdateButtons()
    {
        if (RemoveButton is null) return;
        RemoveButton.IsEnabled = rows.Count > 2 && Points.SelectedIndex >= 0;
        AddButton.IsEnabled = points is { Length: >= 2 and < MaximumPoints };
    }

    private void ValidatePoints()
    {
        string? error = null;
        var parsed = new CurvePoint[rows.Count];
        var limits = V12UltraStatus.SpeedRange;
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if ((!double.TryParse(row.Celsius, NumberStyles.Float, CultureInfo.CurrentCulture, out double temperature) &&
                 !double.TryParse(row.Celsius, NumberStyles.Float, CultureInfo.InvariantCulture, out temperature)) ||
                !double.IsFinite(temperature) || temperature is < 0 or > 120)
                error = $"Point {i + 1} needs a temperature between 0 and 120°C.";
            else if (!int.TryParse(row.Rpm, out int rpm) || rpm < limits.Minimum || rpm > limits.Maximum)
                error = $"Point {i + 1} needs a speed between {limits.Minimum} and {limits.Maximum} RPM.";
            else if (i > 0 && temperature <= parsed[i - 1].Celsius)
                error = "Temperatures must increase from top to bottom.";
            else parsed[i] = new(temperature, rpm);
            if (error is not null) break;
        }
        points = error is null ? parsed : null;
        ErrorText.Text = error;
        ErrorText.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        UpdateButtons();
        PointsChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed class PointRow(CurvePoint point, Action changed)
    {
        private string celsius = point.Celsius.ToString("R", CultureInfo.CurrentCulture);
        private string rpm = point.Rpm.ToString(CultureInfo.CurrentCulture);

        public string Celsius { get => celsius; set { celsius = value; changed(); } }
        public string Rpm { get => rpm; set { rpm = value; changed(); } }
    }
}
