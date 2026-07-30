using BLE_APP.Models;

namespace BLE_APP.Services;

public static class SensorPacketProtocol
{
    public const int LegacyPacketLength = 23;
    public const int PacketLength = 24;
    public const int RadarDistanceOffset = 20;
    public const int ManualLabelOffset = 22;
    public const int PredictedOffset = 23;
    public const byte ManualLabelNoSmokingRaw = 0;
    public const byte ManualLabelSmokingRaw = 1;
    public const byte PredictionNotReadyRaw = 0xFF;
    public const byte StartStreamingCommand = 0x01;
    public const byte SetManualLabelCommand = 0x06;

    public static string FormatManualLabel(ManualLabelState label)
        => label switch
        {
            ManualLabelState.NoSmoking => "No Smoking",
            ManualLabelState.Smoking => "Smoking",
            ManualLabelState.Unknown => "Unknown",
            _ => throw new ArgumentOutOfRangeException(nameof(label), label, "Unsupported Manual label state.")
        };

    public static string FormatPrediction(ManualLabelState prediction)
        => prediction switch
        {
            ManualLabelState.NoSmoking => "No Smoking",
            ManualLabelState.Smoking => "Smoking",
            ManualLabelState.Unknown => "Not Ready",
            _ => throw new ArgumentOutOfRangeException(nameof(prediction), prediction, "Unsupported prediction state.")
        };

    public static bool TryDecodeManualLabel(byte rawValue, out ManualLabelState label)
    {
        switch (rawValue)
        {
            case ManualLabelNoSmokingRaw:
                label = ManualLabelState.NoSmoking;
                return true;
            case ManualLabelSmokingRaw:
                label = ManualLabelState.Smoking;
                return true;
            default:
                label = default;
                return false;
        }
    }

    public static byte EncodeManualLabel(ManualLabelState label)
        => label switch
        {
            ManualLabelState.NoSmoking => ManualLabelNoSmokingRaw,
            ManualLabelState.Smoking => ManualLabelSmokingRaw,
            _ => throw new ArgumentOutOfRangeException(nameof(label), label, "Unsupported Manual label state.")
        };
}
