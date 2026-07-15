using System.Buffers.Binary;
using BLE_APP.Models;

namespace BLE_APP.Services;

public sealed class SensorPacketDecoder
{
    public const int PacketLength = 22;

    public SensorReading Decode(ReadOnlySpan<byte> payload)
    {
        if (payload.Length != PacketLength)
        {
            throw new SensorPacketFormatException($"Expected {PacketLength} bytes, received {payload.Length}.");
        }

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
        ushort distanceRaw = BinaryPrimitives.ReadUInt16LittleEndian(payload[20..22]);

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
            distanceRaw / 1000.0);
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
