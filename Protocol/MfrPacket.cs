using System.Buffers.Binary;

namespace UnifiedPicoModule.Protocol;

// pico-mouth50-v1: preserve MouthLink's 44 fields, then append six independent lip fields.
public enum MouthChannel
{
    CheekPuff, CheekSquintL, CheekSquintR, JawForward, JawLeft, JawOpen, JawRight,
    MouthClose, MouthDimpleL, MouthDimpleR, MouthFrownL, MouthFrownR, MouthFunnel,
    MouthLeft, MouthLowerDownL, MouthLowerDownR, MouthPressL, MouthPressR, MouthPucker,
    MouthRight, MouthRollLower, MouthRollUpper, MouthShrugLower, MouthShrugUpper,
    MouthSmileL, MouthSmileR, MouthStretchL, MouthStretchR, MouthUpperUpL, MouthUpperUpR,
    NoseSneerL, NoseSneerR, TongueOut, TongueLeft, TongueRight, TongueUp, TongueDown,
    CheekPuffLeft, CheekPuffRight, CheekSuckLeft, CheekSuckRight,
    MouthTightenerLeft, MouthTightenerRight, TongueRoll,
    MouthUpperLeft, MouthUpperRight, MouthLowerLeft, MouthLowerRight, LipFunnelUpper, LipFunnelLower
}

public enum MouthState : uint { Fresh = 0, Release = 1 }

public sealed record MfrPacket(ulong SessionId, ulong SourceSeq, ulong MouthSensorNs, ulong EmittedPcNs,
    ulong ValidMask, float SourceAgeMs, MouthState State, ReadOnlyMemory<float> Values)
{
    public const string Schema = "pico-mouth50-v1";
    public const int Size = 256, ChannelCount = 50;
    public const ulong AllValid = (1UL << ChannelCount) - 1;
    public bool Has(MouthChannel channel) => (ValidMask & (1UL << (int)channel)) != 0;
    public float this[MouthChannel channel] => Values.Span[(int)channel];

    public static bool TryParse(ReadOnlySpan<byte> bytes, out MfrPacket? packet, out string error)
    {
        packet = null;
        if (bytes.Length != Size) { error = "length"; return false; }
        if (!bytes[..4].SequenceEqual("MFR1"u8) || BinaryPrimitives.ReadUInt16LittleEndian(bytes[4..]) != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[6..]) != Size)
        { error = "header"; return false; }
        ulong session = BinaryPrimitives.ReadUInt64LittleEndian(bytes[8..]);
        ulong mask = BinaryPrimitives.ReadUInt64LittleEndian(bytes[40..]);
        float age = BinaryPrimitives.ReadSingleLittleEndian(bytes[48..]);
        var state = (MouthState)BinaryPrimitives.ReadUInt32LittleEndian(bytes[52..]);
        if (session == 0) { error = "session-zero"; return false; }
        if (state is not (MouthState.Fresh or MouthState.Release)) { error = "state"; return false; }
        if ((mask & ~AllValid) != 0 || (state == MouthState.Fresh ? mask == 0 : mask != 0))
        { error = "mask"; return false; }
        if (!float.IsFinite(age) || age < 0) { error = "numeric"; return false; }
        var values = new float[ChannelCount];
        for (int i = 0; i < values.Length; i++)
        {
            float value = BinaryPrimitives.ReadSingleLittleEndian(bytes[(56 + i * 4)..]);
            if (!float.IsFinite(value) || value < 0 || value > 1 || ((mask >> i) & 1) == 0 && value != 0)
            { error = "numeric"; return false; }
            values[i] = value;
        }
        packet = new(session, BinaryPrimitives.ReadUInt64LittleEndian(bytes[16..]),
            BinaryPrimitives.ReadUInt64LittleEndian(bytes[24..]), BinaryPrimitives.ReadUInt64LittleEndian(bytes[32..]),
            mask, age, state, values);
        error = ""; return true;
    }

    public byte[] Encode()
    {
        var bytes = new byte[Size];
        "MFR1"u8.CopyTo(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), Size);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), SessionId);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(16), SourceSeq);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(24), MouthSensorNs);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(32), EmittedPcNs);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(40), ValidMask);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(48), SourceAgeMs);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(52), (uint)State);
        for (int i = 0; i < ChannelCount; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(56 + i * 4), Values.Span[i]);
        return bytes;
    }
}
