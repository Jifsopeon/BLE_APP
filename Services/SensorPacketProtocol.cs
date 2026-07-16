using BLE_APP.Models;

namespace BLE_APP.Services;

public static class SensorPacketProtocol
{
    public const int PacketLength = 23;
    public const int ManualLabelOffset = 22;
    public const byte ManualLabelNoSmokingRaw = 0;
    public const byte ManualLabelSmokingRaw = 1;
    public const byte StartStreamingCommand = 0x01;
    public const byte SetManualLabelCommand = 0x06;

    public static string FormatManualLabel(ManualLabelState label)
        => label switch
        {
            ManualLabelState.NoSmoking => "No Smoking",
            ManualLabelState.Smoking => "Smoking",
            _ => throw new ArgumentOutOfRangeException(nameof(label), label, "Unsupported Manual label state.")
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
