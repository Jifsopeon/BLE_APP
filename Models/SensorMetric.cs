using CommunityToolkit.Mvvm.ComponentModel;

namespace BLE_APP.Models;

public sealed partial class SensorMetric : ObservableObject
{
    [ObservableProperty]
    private string _value = "--";

    public SensorMetric(string name, string unit)
    {
        Name = name;
        Unit = unit;
    }

    public string Name { get; }

    public string Unit { get; }
}
