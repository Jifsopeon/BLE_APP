using System.Globalization;
using System.Text;
using BLE_APP.Models;

namespace BLE_APP.Services;

public static class SensorCsvFormatter
{
    public const string Header = "Sample,Date,Time,TimestampISO8601,ElapsedSeconds,PM1_0,PM2_5,PM4_0,PM10,Humidity,Temperature,VOC,NOx,CO2,ManualLabel,ManualLabelRaw";

    public static string FormatRow(long sample, DateTimeOffset sessionStart, SensorReading reading)
    {
        var local = reading.Timestamp.ToLocalTime();
        var elapsedSeconds = (reading.Timestamp - sessionStart).TotalSeconds;
        var columns = new[]
        {
            sample.ToString(CultureInfo.InvariantCulture),
            local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            local.ToString("HH:mm:ss", CultureInfo.InvariantCulture),
            reading.Timestamp.ToString("O", CultureInfo.InvariantCulture),
            elapsedSeconds.ToString("0.###", CultureInfo.InvariantCulture),
            FormatNullable(reading.Pm1, "0.###"),
            FormatNullable(reading.Pm25, "0.###"),
            FormatNullable(reading.Pm4, "0.###"),
            FormatNullable(reading.Pm10, "0.###"),
            FormatNullable(reading.Humidity, "0.###"),
            FormatNullable(reading.Temperature, "0.###"),
            FormatNullable(reading.Voc, "0.###"),
            FormatNullable(reading.Nox, "0.###"),
            reading.Co2?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            SensorPacketProtocol.FormatManualLabel(reading.ManualLabel),
            reading.ManualLabelRaw.ToString(CultureInfo.InvariantCulture)
        };

        return string.Join(",", columns.Select(Escape));
    }

    private static string FormatNullable(double? value, string format)
        => value.HasValue ? value.Value.ToString(format, CultureInfo.InvariantCulture) : string.Empty;

    private static string Escape(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\r') && !value.Contains('\n'))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var ch in value)
        {
            if (ch == '"')
            {
                builder.Append('"');
            }

            builder.Append(ch);
        }

        builder.Append('"');
        return builder.ToString();
    }
}
