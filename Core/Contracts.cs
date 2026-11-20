using System.Diagnostics;
using System.Numerics;

namespace UnifiedPicoModule.Core;

public enum SourceKind { Bridge, Streaming }
public enum InputProtocol { BridgeSplit, DaemonLegacy536, StreamingHeader16, StreamingBodyOnly }
public enum ConnectionState { Searching, Connected, MissingFrames, Stopped, Faulted }
public enum SourceSelection { Auto, Bridge, Streaming }

// These numeric identifiers describe the PICO wire format, not an SDK enum.
public enum PicoShape
{
    EyeLookDownL, NoseSneerL, EyeLookInL, BrowInnerUp, BrowDownR, MouthClose,
    MouthLowerDownR, JawOpen, MouthUpperUpR, MouthShrugUpper, MouthFunnel,
    EyeLookInR, EyeLookDownR, NoseSneerR, MouthRollUpper, JawRight, BrowDownL,
    MouthShrugLower, MouthRollLower, MouthSmileL, MouthPressL, MouthSmileR,
    MouthPressR, MouthDimpleR, MouthLeft, JawForward, EyeSquintL, MouthFrownL,
    EyeBlinkL, CheekSquintL, BrowOuterUpL, EyeLookUpL, JawLeft, MouthStretchL,
    MouthPucker, EyeLookUpR, BrowOuterUpR, CheekSquintR, EyeBlinkR, MouthUpperUpL,
    MouthFrownR, EyeSquintR, MouthStretchR, CheekPuff, EyeLookOutL, EyeLookOutR,
    EyeWideR, EyeWideL, MouthRight, MouthDimpleL, MouthLowerDownL, TongueOut
}

// Module-owned expression identifiers; translated by name only at the SDK boundary.
public enum ExpressionChannel
{
    EyeSquintLeft, EyeSquintRight, EyeWideLeft, EyeWideRight,
    BrowPinchLeft, BrowPinchRight, BrowLowererLeft, BrowLowererRight,
    BrowInnerUpLeft, BrowInnerUpRight, BrowOuterUpLeft, BrowOuterUpRight,
    CheekSquintLeft, CheekSquintRight, CheekPuffLeft, CheekPuffRight,
    JawOpen, JawLeft, JawRight, JawForward, MouthClosed,
    LipSuckUpperLeft, LipSuckUpperRight, LipSuckLowerLeft, LipSuckLowerRight,
    LipFunnelUpperLeft, LipFunnelUpperRight, LipFunnelLowerLeft, LipFunnelLowerRight,
    LipPuckerUpperLeft, LipPuckerUpperRight, LipPuckerLowerLeft, LipPuckerLowerRight,
    MouthUpperUpLeft, MouthUpperUpRight, MouthUpperDeepenLeft, MouthUpperDeepenRight,
    NoseSneerLeft, NoseSneerRight, MouthLowerDownLeft, MouthLowerDownRight,
    MouthUpperLeft, MouthUpperRight, MouthLowerLeft, MouthLowerRight,
    MouthCornerPullLeft, MouthCornerPullRight, MouthCornerSlantLeft, MouthCornerSlantRight,
    MouthFrownLeft, MouthFrownRight, MouthStretchLeft, MouthStretchRight,
    MouthDimpleLeft, MouthDimpleRight, MouthRaiserUpper, MouthRaiserLower,
    MouthPressLeft, MouthPressRight, TongueOut
}

public static class MonotonicClock
{
    public static double Now => (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
    public static bool Fresh(double arrival, double now, int milliseconds) =>
        arrival > 0 && now >= arrival && (now - arrival) * 1000 <= milliseconds;
}

// EPI1: -1 is unknown, 0 is unusable, 1 is usable; v2 adds L/R/mouth source clocks.
public sealed record IndependentEyeExpression(float RightInnerUp, int LeftState, int RightState,
    int Version = 1, uint GroupUpdated = 7, ulong? MouthSourceTimestamp = null,
    ulong? RightSourceTimestamp = null);

public sealed class RawFace
{
    private readonly float[] values;
    private readonly bool[] present;
    private readonly float[] extra;
    private readonly bool eyeExpressionsEnabled;
    public ulong? SourceTimestamp { get; }
    public double Arrival { get; }
    public bool EyeValid { get; }
    public bool FaceValid { get; }
    public IndependentEyeExpression? IndependentEyes { get; }
    public RawFace(float[] values, bool[] present, bool eyeValid, bool faceValid,
        ulong? sourceTimestamp, double arrival, float[]? extraVisemes = null,
        IndependentEyeExpression? independentEyes = null, bool eyeExpressionsEnabled = true)
    {
        if (values.Length != 52 || present.Length != 52) throw new ArgumentException("Expected 52 PICO shapes.");
        this.values = (float[])values.Clone(); this.present = (bool[])present.Clone();
        extra = extraVisemes is null ? [] : (float[])extraVisemes.Clone();
        EyeValid = eyeValid; FaceValid = faceValid; SourceTimestamp = sourceTimestamp; Arrival = arrival;
        IndependentEyes = independentEyes; this.eyeExpressionsEnabled = eyeExpressionsEnabled;
    }
    public float this[PicoShape shape] => values[(int)shape];
    public bool Has(PicoShape shape) => present[(int)shape] && float.IsFinite(this[shape]) &&
        (IsEyeShape(shape) ? EyeAvailable(IsLeftEyeShape(shape)) : FaceValid);
    public bool EyeAvailable(bool isLeft) => eyeExpressionsEnabled && (IndependentEyes is { } independent
        ? (isLeft ? independent.LeftState : independent.RightState) != 0 : EyeValid);
    public bool HasInnerBrow(bool isLeft) => !isLeft && IndependentEyes is { } independent
        ? EyeAvailable(false) && float.IsFinite(independent.RightInnerUp) : Has(PicoShape.BrowInnerUp);
    public float InnerBrow(bool isLeft) => !isLeft && IndependentEyes is { } independent
        ? independent.RightInnerUp : this[PicoShape.BrowInnerUp];
    public ReadOnlySpan<float> ExtraVisemes => extra;
    public RawFace WithValidity(bool eyeEnabled, bool faceEnabled) =>
        new(values, present, EyeValid && eyeEnabled, FaceValid && faceEnabled, SourceTimestamp, Arrival, extra,
            IndependentEyes, eyeExpressionsEnabled && eyeEnabled);
    public RawFace MouthObservation() => new(values, present, false, FaceValid,
        IndependentEyes!.MouthSourceTimestamp, Arrival, extra, eyeExpressionsEnabled: false);
    public RawFace EyeObservation(bool isLeft) => new(values, present, EyeValid, false,
        isLeft ? SourceTimestamp : IndependentEyes!.RightSourceTimestamp, Arrival, extra,
        isLeft ? IndependentEyes! with { RightState = 0 } : IndependentEyes! with { LeftState = 0 }, eyeExpressionsEnabled);
    private static bool IsLeftEyeShape(PicoShape shape) => shape is PicoShape.EyeLookDownL or PicoShape.EyeLookInL or
        PicoShape.BrowInnerUp or PicoShape.BrowDownL or PicoShape.EyeSquintL or PicoShape.EyeBlinkL or
        PicoShape.BrowOuterUpL or PicoShape.EyeLookUpL or PicoShape.EyeLookOutL or PicoShape.EyeWideL;
    public static bool IsEyeShape(PicoShape shape) => shape is PicoShape.EyeLookDownL or PicoShape.EyeLookInL or
        PicoShape.BrowInnerUp or PicoShape.BrowDownR or PicoShape.EyeLookInR or PicoShape.EyeLookDownR or
        PicoShape.BrowDownL or PicoShape.EyeSquintL or PicoShape.EyeBlinkL or PicoShape.BrowOuterUpL or
        PicoShape.EyeLookUpL or PicoShape.EyeLookUpR or PicoShape.BrowOuterUpR or PicoShape.EyeBlinkR or
        PicoShape.EyeSquintR or PicoShape.EyeLookOutL or PicoShape.EyeLookOutR or PicoShape.EyeWideR or PicoShape.EyeWideL;
}

public sealed record NativeEye(Vector3 Gaze, bool GazeValid, float Openness, bool OpennessValid,
    float PupilMillimeters, bool PupilValid);
public sealed record RawEye(NativeEye Left, NativeEye Right, Vector3 CombinedGaze, bool CombinedValid,
    ulong? SourceTimestamp, double Arrival);
public sealed record CapabilitySnapshot(bool ModeKnown = false, bool Enhanced = false,
    bool IndependentPerEye = false, string Plugin = "off");
public sealed record SourceSnapshot(SourceKind Source, long Generation, InputProtocol? Protocol,
    ConnectionState State, CapabilitySnapshot Capabilities, RawEye? Eye, long EyeSequence,
    RawFace? Face, long FaceSequence, double LastControl, long DroppedPackets, string Detail,
    long OutputGeneration = 0, RawFace? Mouth = null, long MouthSequence = 0, bool GroupedExpressions = false,
    RawFace? RightFace = null, long RightFaceSequence = 0)
{
    public static SourceSnapshot Empty(SourceKind kind) => new(kind, 0, null, ConnectionState.Searching,
        new(), null, 0, null, 0, 0, 0, "Waiting for validated input");
}
public interface ITrackingSource : IDisposable
{
    SourceSnapshot Snapshot { get; }
    void Start(CancellationToken cancellationToken);
}

public sealed class ExpressionFrame
{
    private readonly float[] values = new float[Enum.GetValues<ExpressionChannel>().Length];
    public float this[ExpressionChannel channel] { get => values[(int)channel]; set => values[(int)channel] = value; }
    public ExpressionFrame Clone()
    {
        var copy = new ExpressionFrame(); Array.Copy(values, copy.values, values.Length); return copy;
    }
    public void Clamp(float maximum = 1)
    {
        for (var i = 0; i < values.Length; i++) values[i] = float.IsFinite(values[i]) ? Math.Clamp(values[i], 0, maximum) : 0;
    }
    public static bool IsEye(ExpressionChannel channel) => (int)channel <= (int)ExpressionChannel.BrowOuterUpRight;
    public static bool IsLeftEye(ExpressionChannel channel) => channel is ExpressionChannel.EyeSquintLeft or
        ExpressionChannel.EyeWideLeft or ExpressionChannel.BrowPinchLeft or ExpressionChannel.BrowLowererLeft or
        ExpressionChannel.BrowInnerUpLeft or ExpressionChannel.BrowOuterUpLeft;
}
public sealed record ResolvedEye(float GazeX = 0, float GazeY = 0, float Openness = 1,
    float PupilMillimeters = 3.5f, string GazeOrigin = "neutral", bool RealPupil = false,
    string OpennessOrigin = "neutral");
public sealed record TrackingOutput(ExpressionFrame Expressions, ResolvedEye Left, ResolvedEye Right);
