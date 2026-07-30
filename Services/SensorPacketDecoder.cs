using System.Buffers.Binary;
using BLE_APP.Models;

namespace BLE_APP.Services;

public sealed class SensorPacketDecoder
{
    public const int PacketLength = SensorPacketProtocol.PacketLength;

    public SensorReading Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length is not SensorPacketProtocol.PacketLength and not SensorPacketProtocol.LegacyPacketLength)
        {
            throw new SensorPacketFormatException(
                $"Incompatible firmware packet: expected {SensorPacketProtocol.PacketLength} or {SensorPacketProtocol.LegacyPacketLength} bytes, received {payload.Length} bytes.");
        }

        // Bytes 0..1: sequence number, UInt16 little-endian.
        // Bytes 2..9: PM1.0, PM2.5, PM4.0, PM10.0, UInt16 tenths, little-endian.
        // Bytes 10..13: humidity and temperature, Int16 scaled by 100 and 200.
        // Bytes 14..17: NOx and VOC, Int16 tenths.
        // Bytes 18..19: CO2, UInt16 ppm.
        // Bytes 20..21: selected radar distance in mm, UInt16 little-endian.
        // Byte 22: active manual label, 0 = No Smoking, 1 = Smoking.
        // Byte 23: model prediction, 0 = No Smoking, 1 = Smoking, 255 = Not Ready.
        // Convert radar distance to metres by dividing by 1000.0.
        // A value of zero means no valid presence distance.
        ushort sequence = BinaryPrimitives.ReadUInt16LittleEndian(payload[0..2]);
        ushort pm1 = BinaryPrimitives.ReadUInt16LittleEndian(payload[2..4]);
        ushort pm25 = BinaryPrimitives.ReadUInt16LittleEndian(payload[4..6]);
        ushort pm4 = BinaryPrimitives.ReadUInt16LittleEndian(payload[6..8]);
        ushort pm10 = BinaryPrimitives.ReadUInt16LittleEndian(payload[8..10]);
        short humidity = BinaryPrimitives.ReadInt16LittleEndian(payload[10..12]);
        short temperature = BinaryPrimitives.ReadInt16LittleEndian(payload[12..14]);
        short nox = BinaryPrimitives.ReadInt16LittleEndian(payload[14..16]);
        short voc = BinaryPrimitives.ReadInt16LittleEndian(payload[16..18]);
        ushort co2 = BinaryPrimitives.ReadUInt16LittleEndian(payload[18..20]);
        ushort radarDistanceRaw = BinaryPrimitives.ReadUInt16LittleEndian(payload[SensorPacketProtocol.RadarDistanceOffset..(SensorPacketProtocol.RadarDistanceOffset + 2)]);
        byte manualLabelRaw = payload[SensorPacketProtocol.ManualLabelOffset];
        ManualLabelState manualLabel = SensorPacketProtocol.TryDecodeManualLabel(manualLabelRaw, out var decodedLabel)
            ? decodedLabel
            : ManualLabelState.Unknown;
        byte predictedRaw = payload.Length > SensorPacketProtocol.PredictedOffset
            ? payload[SensorPacketProtocol.PredictedOffset]
            : SensorPacketProtocol.PredictionNotReadyRaw;
        ManualLabelState predicted = SensorPacketProtocol.TryDecodeManualLabel(predictedRaw, out var decodedPrediction)
            ? decodedPrediction
            : ManualLabelState.Unknown;

        return new SensorReading(
            DateTimeOffset.Now,
            sequence,
            ScaleUnsignedTenths(pm1),
            ScaleUnsignedTenths(pm25),
            ScaleUnsignedTenths(pm4),
            ScaleUnsignedTenths(pm10),
            ScaleSigned(humidity, 100.0),
            ScaleSigned(temperature, 200.0),
            ScaleSignedTenths(nox),
            ScaleSignedTenths(voc),
            co2 == 0xFFFF ? null : co2,
            radarDistanceRaw / 1000.0,
            manualLabel,
            manualLabelRaw,
            predicted,
            predictedRaw);
    }

    private static double? ScaleUnsignedTenths(ushort value)
        => value == 0xFFFF ? null : value / 10.0;

    private static double? ScaleSignedTenths(short value)
        => value == 0x7FFF ? null : value / 10.0;

    private static double? ScaleSigned(short value, double scale)
        => value == 0x7FFF ? null : value / scale;
}

public sealed class SensorPacketFormatException : FormatException
{
    public SensorPacketFormatException(string message)
        : base(message)
    {
    }
}
