using System.Buffers.Binary;

namespace UnifiedPicoModule.Protocol;

public enum TongueState : uint { Present = 1, Absent = 2, Uncertain = 4, Invalid = 8, Stop = 16 }

public sealed record TjrPacket(ulong SessionId, ulong SourceSeq, ulong MouthSensorNs, ulong EmittedPcNs,
    float Left, float Right, float Out, float SourceAgeMs, TongueState State)
{
    public const int Size = 64;

    public static bool TryParse(ReadOnlySpan<byte> bytes, out TjrPacket? packet, out string error)
    {
        packet = null;
        if (bytes.Length != Size) { error = "length"; return false; }
        if (!bytes[..4].SequenceEqual("TJR1"u8) || BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]) != Size)
        { error = "header"; return false; }
        if (BinaryPrimitives.ReadUInt32LittleEndian(bytes[60..]) != 0)
        { error = "reserved"; return false; }
        var parsed = new TjrPacket(BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]), BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[40..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[44..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[48..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[52..]), (TongueState)BinaryPrimitives.ReadUInt32LittleEndian(bytes[56..]));
        if (parsed.SessionId == 0) { error = "session-zero"; return false; }
        if (parsed.State is not (TongueState.Present or TongueState.Absent or TongueState.Uncertain or TongueState.Invalid or TongueState.Stop))
        { error = "state"; return false; }
        if (!Unit(parsed.Left) || !Unit(parsed.Right) || !Unit(parsed.Out) ||
            !float.IsFinite(parsed.SourceAgeMs) || parsed.SourceAgeMs < 0)
        { error = "numeric"; return false; }
        packet = parsed; error = ""; return true;
    }

    private static bool Unit(float value) => float.IsFinite(value) && value is >= 0 and <= 1;

    public byte[] Encode()
    {
        var bytes = new byte[Size];
        "TJR1"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), Size);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), SessionId);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), SourceSeq);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24), MouthSensorNs);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), EmittedPcNs);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(40), Left);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(44), Right);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(48), Out);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(52), SourceAgeMs);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), (uint)State);
        return bytes;
    }
}
