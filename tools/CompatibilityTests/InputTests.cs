using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using UnifiedPicoModule.Configuration;
using UnifiedPicoModule.Core;
using UnifiedPicoModule.Protocol;
using UnifiedPicoModule.Runtime;
using UnifiedPicoModule.Transport;

namespace CompatibilityTests;

internal static partial class Program
{
    internal static void InputTests()
    {
        Run("input-x-v2-wire-clocks-and-validation", InputWireValidation);
        Run("input-x-v2-per-group-timestamp-deduplication", InputTimestampDeduplication);
        Run("input-x-v2-updated-mask-does-not-consume-held-clocks", InputUpdatedMask);
        foreach (int version in new[] { 0, 1, 2 })
        {
            string mode = version == 0 ? "f" : $"x-v{version}";
            Run($"input-{mode}-pipeline-gains-and-cached-output", () => InputGains(version));
            Run($"input-{mode}-tongue-hold-expiry-without-reprocessing", () => InputTongueHold(version));
        }
        foreach (bool neutralize in new[] { false, true })
            Run($"input-x-v2-invalid-and-stale-neutralize-{neutralize}", () => InputInvalidGroups(neutralize));
        Run("input-source-timeout-and-explicit-session-reset", InputSessionReset);
    }

    // Synthetic full-channel wire fixtures only. Decoding, group extraction, clock
    // deduplication and processing all execute methods from the referenced module DLL.
    private static byte[] InputBytes(int version = 2, long left = 10, long right = 20, long mouth = 30,
        uint updated = 7, int leftState = 1, int rightState = 1, byte valid = 3, float rightInner = 0.7f,
        params (PicoShape Shape, float Value)[] values)
    {
        const int extension = 227;
        var bytes = new byte[extension + (version == 0 ? 0 : version == 2 ? 36 : 16)];
        bytes[0] = version == 0 ? (byte)'F' : (byte)'X';
        bytes[1] = 3; bytes[2] = valid;
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(3), 1);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(11), left);
        foreach (var (shape, value) in values)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(19 + (int)shape * 4), value);
        if (version == 0) return bytes;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(extension), version);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(extension + 4), rightInner);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(extension + 8), leftState);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(extension + 12), rightState);
        if (version == 2)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(extension + 16), updated);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(extension + 20), mouth);
            BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(extension + 28), right);
        }
        return bytes;
    }

    private static DecodedPacket InputDecode(byte[] bytes, double arrival)
    {
        Check(new BridgeSplitDecoder().TryDecode(bytes, arrival, out var packet), "Fixture must decode");
        return packet!;
    }

    private sealed class InputCommitSource() : UdpTrackingSource(SourceKind.Bridge, _ => { })
    {
        private static readonly MethodInfo CommitMethod = typeof(UdpTrackingSource).GetMethod("Commit", Hidden)!;
        public bool Feed(byte[] bytes, double arrival) =>
            (bool)CommitMethod.Invoke(this, [InputDecode(bytes, arrival)])!;
        public void Restart(bool timeout) =>
            Reset(InputProtocol.BridgeSplit, new(), ConnectionState.Searching, "Compatibility test reset", timeout);
        protected override void OpenSockets() => throw new InvalidOperationException("Network forbidden in input tests");
        protected override void Tick(double now) => throw new InvalidOperationException("Network forbidden in input tests");
        protected override void Receive(Socket socket, IPEndPoint sender, ReadOnlySpan<byte> bytes, double now) =>
            throw new InvalidOperationException("Network forbidden in input tests");
    }

    private static void InputProcessed(TrackingPipeline pipeline, int left, int right, int mouth)
    {
        string report = pipeline.Diagnostics.Report();
        Check(report.Contains($"processedLRM={left}/{right}/{mouth} ", StringComparison.Ordinal), report);
    }

    private static void InputWireValidation()
    {
        var bytes = InputBytes(left: 101, right: 202, mouth: 303, rightInner: 0.65f,
            values: [(PicoShape.BrowInnerUp, 0.2f), (PicoShape.JawOpen, 0.4f)]);
        var packet = InputDecode(bytes, 100);
        var face = packet.Face!; var extension = face.IndependentEyes!;
        Check(bytes.Length == 263 && packet.Epoch == 1 && packet.ChannelMask == 3, "Full X v2 header");
        Check(extension.Version == 2 && extension.GroupUpdated == 7, "X v2 extension metadata");
        Check(face.SourceTimestamp == 101 && extension.RightSourceTimestamp == 202 &&
            extension.MouthSourceTimestamp == 303, "Distinct L/R/mouth clocks survive decoding");
        Near(extension.RightInnerUp, 0.65f, "Right inner brow extension");
        Near(face[PicoShape.JawOpen], 0.4f, "Shape payload");
        Check(InputDecode(InputBytes(left: 0, right: 0, mouth: 0, updated: 0), 101)
            .Face!.IndependentEyes!.GroupUpdated == 0, "Unchanged groups may omit their clocks");
        foreach (var invalid in new[] {
            InputBytes(updated: 8), InputBytes(left: 0, updated: 1),
            InputBytes(right: 0, updated: 2), InputBytes(mouth: 0, updated: 4),
            InputBytes(left: -1, updated: 0), InputBytes(rightState: 2), bytes[..^1] })
            Check(!new BridgeSplitDecoder().TryDecode(invalid, 100, out _), "Malformed X v2 must be rejected");
    }

    private static void InputTimestampDeduplication()
    {
        using var source = new InputCommitSource();
        Check(source.Feed(InputBytes(left: 100, right: 200, mouth: 300), 100), "Initial groups accepted");
        var first = source.Snapshot;
        Check(first.GroupedExpressions && first.FaceSequence == 1 && first.RightFaceSequence == 1 &&
            first.MouthSequence == 1, "Three initial group identities");
        Check(source.Feed(InputBytes(left: 100, right: 201, mouth: 300,
            values: [(PicoShape.EyeLookInL, 0.9f), (PicoShape.EyeLookOutR, 0.8f), (PicoShape.JawOpen, 0.9f)]), 100.05),
            "Same left source and duplicate mouth must not block new right");
        var second = source.Snapshot;
        Check(second.FaceSequence == 1 && second.RightFaceSequence == 2 && second.MouthSequence == 1,
            "Only right sequence advances");
        Check(ReferenceEquals(second.Face, first.Face) && ReferenceEquals(second.Mouth, first.Mouth),
            "Rejected groups retain their original object, arrival and values");
        Check(second.Face!.Arrival == 100 && second.Mouth!.Arrival == 100 &&
            second.RightFace!.Arrival == 100.05 && second.RightFace!.SourceTimestamp == 201, "Per-group arrival times");
        Near(second.RightFace![PicoShape.EyeLookOutR], 0.8f, "New right payload");
        Check(source.Feed(InputBytes(left: 101, right: 200, mouth: 301), 100.1),
            "Backward right must not block new left and mouth");
        var third = source.Snapshot;
        Check(third.FaceSequence == 2 && third.RightFaceSequence == 2 && third.MouthSequence == 2 &&
            ReferenceEquals(third.RightFace, second.RightFace), "Right duplicate/backward leaves its identity unchanged");
        Check(!source.Feed(InputBytes(left: 101, right: 201, mouth: 301, updated: 4), 100.15), "Duplicate mouth rejected");
        var duplicate = source.Snapshot;
        Check(duplicate.MouthSequence == third.MouthSequence && ReferenceEquals(duplicate.Mouth, third.Mouth) &&
            duplicate.DroppedPackets == third.DroppedPackets + 1, "Duplicate mouth advances neither sequence nor arrival");
        Check(!source.Feed(InputBytes(left: 100, right: 200, mouth: 300), 100.2), "All backward groups rejected");
        Check(source.Snapshot.FaceSequence == 2 && source.Snapshot.RightFaceSequence == 2 &&
            source.Snapshot.MouthSequence == 2, "Rejected packet advances no group");
    }

    private static void InputUpdatedMask()
    {
        using var source = new InputCommitSource();
        Check(source.Feed(InputBytes(), 100), "Initial groups accepted");
        var first = source.Snapshot;
        Check(source.Feed(InputBytes(left: 11, right: 900, mouth: 900, updated: 1), 100.05), "Left-only update accepted");
        var leftOnly = source.Snapshot;
        Check(leftOnly.FaceSequence == 2 && leftOnly.RightFaceSequence == 1 && leftOnly.MouthSequence == 1 &&
            ReferenceEquals(leftOnly.RightFace, first.RightFace) && ReferenceEquals(leftOnly.Mouth, first.Mouth),
            "Unmarked right and mouth neither publish nor refresh");
        Check(source.Feed(InputBytes(left: 0, right: 21, mouth: 31, updated: 6), 100.1),
            "Unmarked future clock values did not consume right/mouth clocks");
        var next = source.Snapshot;
        Check(next.FaceSequence == 2 && next.RightFaceSequence == 2 && next.MouthSequence == 2 &&
            ReferenceEquals(next.Face, leftOnly.Face), "Right/mouth update preserves left identity");
        Check(source.Feed(InputBytes(left: 0, right: 0, mouth: 0, updated: 0), 100.2), "Zero mask is a valid no-op");
        var idle = source.Snapshot;
        Check(idle.FaceSequence == next.FaceSequence && idle.RightFaceSequence == next.RightFaceSequence &&
            idle.MouthSequence == next.MouthSequence && ReferenceEquals(idle.Face, next.Face) &&
            ReferenceEquals(idle.RightFace, next.RightFace) && ReferenceEquals(idle.Mouth, next.Mouth),
            "No-op packet refreshes no group");
    }

    private static void InputGains(int version)
    {
        var config = new ModuleConfig { EyeGain = new() { LeftX = 2, LeftY = 0.5f, RightX = 0.5f,
            RightY = 2, LeftOpenness = 0.5f, RightOpenness = 0.75f } };
        config.PostProcessing.ChannelGain.Gains = new() { ["EyeWideLeft"] = 3, ["EyeWideRight"] = 2,
            ["JawOpen"] = 1.5f, ["TongueOut"] = 0.5f };
        config.PostProcessing.TongueJawGate = new() { Enabled = true, JawThreshold = 0.5f, TransitionWidth = 0, MaxReduction = 0.5f };
        using var source = new InputCommitSource();
        // Independent quality states override the absent combined eye-valid bit in X.
        Check(source.Feed(InputBytes(version, valid: version == 0 ? (byte)3 : (byte)2, values: [
            (PicoShape.EyeLookInL, 0.3f), (PicoShape.EyeLookOutL, 0.1f), (PicoShape.EyeLookUpL, 0.6f),
            (PicoShape.EyeLookDownL, 0.2f), (PicoShape.EyeLookOutR, 0.7f), (PicoShape.EyeLookInR, 0.3f),
            (PicoShape.EyeLookUpR, 0.1f), (PicoShape.EyeLookDownR, 0.3f), (PicoShape.EyeBlinkL, 0.2f),
            (PicoShape.EyeBlinkR, 0.6f), (PicoShape.EyeWideL, 0.2f), (PicoShape.EyeWideR, 0.3f),
            (PicoShape.BrowInnerUp, 0.2f), (PicoShape.JawOpen, 0.2f), (PicoShape.TongueOut, 0.8f)]), 100), "Mode input accepted");
        Check(source.Snapshot.GroupedExpressions == (version == 2), "Only X v2 has independent group clocks");
        var pipeline = new TrackingPipeline(config);
        var output = pipeline.Evaluate(source.Snapshot, 100);
        Near(output.Left.GazeX, 0.4f, "Left horizontal EyeGain"); Near(output.Left.GazeY, 0.2f, "Left vertical EyeGain");
        Near(output.Right.GazeX, 0.2f, "Right horizontal EyeGain"); Near(output.Right.GazeY, -0.4f, "Right vertical EyeGain");
        Near(output.Left.Openness, 0.4f, "Left openness EyeGain"); Near(output.Right.Openness, 0.3f, "Right openness EyeGain");
        Near(output.Expressions[ExpressionChannel.BrowInnerUpLeft], 0.2f, "Left inner brow");
        Near(output.Expressions[ExpressionChannel.BrowInnerUpRight], version == 0 ? 0.2f : 0.7f, "F shared versus X independent inner brow");
        InputProcessed(pipeline, 1, 1, 1);
        foreach (double now in new[] { 100.05, 100.1 })
        {
            output = pipeline.Evaluate(source.Snapshot, now);
            Near(output.Expressions[ExpressionChannel.EyeWideLeft], 0.6f, "Left ChannelGain applied once");
            Near(output.Expressions[ExpressionChannel.EyeWideRight], 0.6f, "Right ChannelGain applied once");
            Near(output.Expressions[ExpressionChannel.JawOpen], 0.3f, "Mouth ChannelGain applied once");
            Near(output.Expressions[ExpressionChannel.TongueOut], 0.2f, "Tongue gate then gain applied once");
            Near(output.Left.GazeX, 0.4f, "Held gaze gain does not compound");
            Near(output.Right.Openness, 0.3f, "Held openness gain does not compound");
        }
        InputProcessed(pipeline, 0, 0, 0);
    }

    private static void InputTongueHold(int version)
    {
        var config = new ModuleConfig(); config.Stale.FaceMs = 500;
        config.PostProcessing.TongueHold = new() { Enabled = true, HoldMs = 100 };
        config.PostProcessing.TongueJawGate = new() { Enabled = true, JawThreshold = 0.5f, TransitionWidth = 0, MaxReduction = 0.5f };
        config.PostProcessing.ChannelGain.Gains["TongueOut"] = 0.5f;
        var pipeline = new TrackingPipeline(config);
        using var source = new InputCommitSource();
        Check(source.Feed(InputBytes(version, values: [(PicoShape.TongueOut, 0.8f), (PicoShape.JawOpen, 0.2f),
            (PicoShape.EyeLookInL, 0.3f)]), 100), "High tongue sample");
        Near(pipeline.Evaluate(source.Snapshot, 100).Expressions[ExpressionChannel.TongueOut], 0.2f, "High tongue after gate/gain");
        InputProcessed(pipeline, 1, 1, 1);
        Check(source.Feed(InputBytes(version, left: 11, right: 21, mouth: 31,
            values: [(PicoShape.TongueOut, 0.04f), (PicoShape.JawOpen, 0.2f), (PicoShape.EyeLookInL, 0.3f)]), 100.05), "Measured tongue drop");
        var snapshot = source.Snapshot;
        Near(pipeline.Evaluate(snapshot, 100.05).Expressions[ExpressionChannel.TongueOut], 0.2f, "Drop starts hold");
        InputProcessed(pipeline, 1, 1, 1);
        Near(pipeline.Evaluate(snapshot, 100.1).Expressions[ExpressionChannel.TongueOut], 0.2f, "Repeated poll retains hold");
        InputProcessed(pipeline, 0, 0, 0);
        if (version == 2)
        {
            Check(source.Feed(InputBytes(left: 12, right: 22, mouth: 900, updated: 3,
                values: [(PicoShape.TongueOut, 0.9f), (PicoShape.JawOpen, 1), (PicoShape.EyeLookInL, 0.6f)]), 100.12), "New eye groups only");
            snapshot = source.Snapshot;
        }
        else
        {
            var eye = new NativeEye(new(0.9f, 0.8f, 1), true, 0.01f, true, 4, true);
            snapshot = snapshot with { Eye = new(eye, eye, new(0.9f, 0.8f, 1), true, 99, 100.12), EyeSequence = 1 };
        }
        var eyeOnly = pipeline.Evaluate(snapshot, 100.12);
        Near(eyeOnly.Expressions[ExpressionChannel.TongueOut], 0.2f, "Eye update neither resets nor renews mouth hold");
        Near(eyeOnly.Left.GazeX, version == 2 ? 0.6f : 0.3f, "Native eye gaze cannot replace expression gaze");
        InputProcessed(pipeline, version == 2 ? 1 : 0, version == 2 ? 1 : 0, 0);
        Near(pipeline.Evaluate(snapshot, 100.149).Expressions[ExpressionChannel.TongueOut], 0.2f, "Before original hold deadline");
        Near(pipeline.Evaluate(snapshot, 100.151).Expressions[ExpressionChannel.TongueOut], 0.01f, "Expiry uses finalized low tongue without a new mouth sample");
        Near(pipeline.Evaluate(snapshot, 100.18).Expressions[ExpressionChannel.TongueOut], 0.01f, "Repeated expiry does not reapply gate/gain");
        InputProcessed(pipeline, 0, 0, 0);
    }

    private static void InputInvalidGroups(bool neutralize)
    {
        var config = new ModuleConfig(); config.Stale.FaceMs = 200; config.Stale.NeutralizeOnTimeout = neutralize;
        var pipeline = new TrackingPipeline(config);
        using var source = new InputCommitSource();
        Check(source.Feed(InputBytes(values: [(PicoShape.EyeLookInL, 0.3f), (PicoShape.EyeLookOutR, 0.6f),
            (PicoShape.EyeBlinkL, 0.2f), (PicoShape.BrowInnerUp, 0.2f), (PicoShape.JawOpen, 0.4f)]), 100), "Initial valid groups");
        pipeline.Evaluate(source.Snapshot, 100); InputProcessed(pipeline, 1, 1, 1);
        Check(source.Feed(InputBytes(left: 11, right: 21, updated: 3, leftState: 0, rightInner: 0.5f,
            values: [(PicoShape.EyeLookInL, 0.9f), (PicoShape.EyeLookOutR, 0.8f), (PicoShape.BrowInnerUp, 0.9f)]), 100.1), "Invalid left and valid right are new observations");
        var invalid = pipeline.Evaluate(source.Snapshot, 100.1);
        Near(invalid.Left.GazeX, 0.3f, "Invalid left keeps its own prior gaze");
        Near(invalid.Left.Openness, 0.8f, "Invalid left keeps its own prior eyelid");
        Near(invalid.Expressions[ExpressionChannel.BrowInnerUpLeft], 0.2f, "Invalid left never borrows right brow");
        Near(invalid.Right.GazeX, 0.8f, "Valid right continues independently");
        Near(invalid.Expressions[ExpressionChannel.JawOpen], 0.4f, "Invalid eye does not invalidate held mouth");
        InputProcessed(pipeline, 1, 1, 0);
        var expired = pipeline.Evaluate(source.Snapshot, 100.21);
        Near(expired.Left.GazeX, neutralize ? 0 : 0.3f, "Invalid left did not refresh last usable gaze age");
        Near(expired.Left.Openness, neutralize ? 1 : 0.8f, "Per-eye stale eyelid policy");
        Near(expired.Expressions[ExpressionChannel.BrowInnerUpLeft], neutralize ? 0 : 0.2f, "Per-eye stale expression policy");
        Near(expired.Expressions[ExpressionChannel.JawOpen], neutralize ? 0 : 0.4f, "Mouth expires on its own clock");
        Near(expired.Right.GazeX, 0.8f, "Right remains fresh while left/mouth expire");
        Check(source.Feed(InputBytes(left: 0, right: 0, mouth: 31, updated: 4, valid: 0,
            values: [(PicoShape.JawOpen, 0.9f)]), 100.22), "New invalid mouth observation accepted");
        var lostMouth = pipeline.Evaluate(source.Snapshot, 100.22);
        Near(lostMouth.Expressions[ExpressionChannel.JawOpen], 0, "Explicit mouth invalidity clears it even with timeout holding");
        Near(lostMouth.Right.GazeX, 0.8f, "Mouth invalidity leaves right eye intact");
        Check(source.Feed(InputBytes(left: 12, right: 0, mouth: 0, updated: 1,
            values: [(PicoShape.EyeLookInL, 0.5f), (PicoShape.BrowInnerUp, 0.4f)]), 100.23), "Left recovery accepted");
        var recovered = pipeline.Evaluate(source.Snapshot, 100.23);
        Near(recovered.Left.GazeX, 0.5f, "Left recovers without right/mouth observations");
        Near(recovered.Expressions[ExpressionChannel.JawOpen], 0, "Eye recovery cannot restore invalid mouth");
        var rightExpired = pipeline.Evaluate(source.Snapshot, 100.31);
        Near(rightExpired.Left.GazeX, 0.5f, "Recovered left remains fresh");
        Near(rightExpired.Right.GazeX, neutralize ? 0 : 0.8f, "Right expiry is not refreshed by left recovery");
        Near(rightExpired.Expressions[ExpressionChannel.BrowInnerUpRight], neutralize ? 0 : 0.5f, "Right expression expires independently");
        InputProcessed(pipeline, 1, 0, 1);
    }

    private static void InputSessionReset()
    {
        foreach (bool neutralize in new[] { false, true })
        {
            var config = new ModuleConfig(); config.Stale.NeutralizeOnTimeout = neutralize;
            var pipeline = new TrackingPipeline(config);
            using var source = new InputCommitSource();
            var bytes = InputBytes(values: [(PicoShape.EyeLookInL, 0.3f), (PicoShape.JawOpen, 0.4f)]);
            Check(source.Feed(bytes, 100), "Initial session input");
            pipeline.Evaluate(source.Snapshot, 100);
            long outputGeneration = source.Snapshot.OutputGeneration;
            source.Restart(timeout: true);
            Check(source.Snapshot.OutputGeneration == outputGeneration, "Timeout preserves output identity");
            var timeout = pipeline.Evaluate(source.Snapshot, 100.1);
            Near(timeout.Left.GazeX, neutralize ? 0 : 0.3f, "Timeout obeys gaze hold policy");
            Near(timeout.Expressions[ExpressionChannel.JawOpen], neutralize ? 0 : 0.4f, "Timeout obeys expression hold policy");
            Check(source.Feed(InputBytes(left: 1, right: 2, mouth: 3,
                values: [(PicoShape.EyeLookInL, 0.5f), (PicoShape.JawOpen, 0.6f)]), 100.2), "Restart clears all deduplication clocks");
            Near(pipeline.Evaluate(source.Snapshot, 100.2).Expressions[ExpressionChannel.JawOpen], 0.6f, "New session sample consumed");
            source.Restart(timeout: false);
            Check(source.Snapshot.OutputGeneration == outputGeneration + 1, "Explicit session change advances output identity");
            var reset = pipeline.Evaluate(source.Snapshot, 100.21);
            Near(reset.Left.GazeX, 0, "Explicit session change clears gaze regardless of hold policy");
            Near(reset.Expressions[ExpressionChannel.JawOpen], 0, "Explicit session change clears mouth regardless of hold policy");
            Check(source.Feed(bytes, 100.22), "Post-reset input accepted"); pipeline.Evaluate(source.Snapshot, 100.22);
            var changedSource = source.Snapshot with { Source = SourceKind.Streaming, Face = null, RightFace = null, Mouth = null };
            var changed = pipeline.Evaluate(changedSource, 100.23);
            Near(changed.Left.GazeX, 0, "Whole-source switch clears old gaze");
            Near(changed.Expressions[ExpressionChannel.JawOpen], 0, "Whole-source switch clears old mouth");
        }
    }
}
