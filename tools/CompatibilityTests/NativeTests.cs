#if NATIVE_HOST
using System.Buffers.Binary;
using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using UnifiedPicoModule.Configuration;
using UnifiedPicoModule.Core;
using VRCFaceTracking;
using VRCFaceTracking.Core.Contracts;
using VRCFaceTracking.Core.OSC;
using VRCFaceTracking.Core.OSC.Query;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Services;

namespace CompatibilityTests;

internal static partial class Program
{
    private static void NativeTests()
    {
        Run("native-official-key-order-and-quantization", NativeKeyOrder);
        Run("native-module-eye-directions-and-single-blinks", NativeEyes);
        Run("native-pico-brows-mouth-and-postprocessing", NativeProcessing);
        Run("native-mixed-owners-ipc-and-interruption", NativeMixed);
        Run("native-avatar-capability-and-legacy-formulas", NativeRouting);
        Run("native-pupil-measured-versus-unavailable", NativePupils);
        Run("native-pupil-official-serializer-vectors", NativePupilVectors);
        Run("native-pupil-hold-and-timeout-neutralization", NativePupilExpiry);
        Run("native-disposed-message-keeps-valid-face-output", NativeDisposedMessage);
        Run("native-upstream-sender-start-stop-boundaries", NativeSenderBoundaries);
        ModuleLoaderTests();
    }

    // Protocol facts from VRChat's independent 102-key fixture, not an encoder.
    private static byte Quantized(float value) => (byte)(Math.Clamp(value, 0, 1) * 255);
    private static byte[] Expected(params (int Index, float Value)[] values)
    {
        var bytes = new byte[102];
        foreach (var (index, value) in values) bytes[index] = Quantized(value);
        return bytes;
    }
    private static void Bytes(byte[] actual, byte[] expected, string label)
    {
        Check(actual.Length == 102, $"{label}: blob must be exactly 102 bytes");
        for (int i = 0; i < expected.Length; i++)
            Check(actual[i] == expected[i], $"{label}: slot {i}: expected {expected[i]}, actual {actual[i]}");
    }

    private sealed class LoopbackTarget : IOscTarget
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        public bool IsConnected { get; set; }
        public int InPort { get; set; }
        public string DestinationAddress { get; set; } = "127.0.0.1";
        public int OutPort { get; set; }
        public void Connect(int port) { OutPort = port; PropertyChanged?.Invoke(this, new(nameof(OutPort))); }
    }
    private sealed class NativeSession : IDisposable
    {
        private readonly UdpClient receiver = new(new IPEndPoint(IPAddress.Loopback, 0));
        private readonly OscSendService send;
        private readonly ParameterSenderService parameters;
        internal readonly OscQueryService Query = new(NullLogger<OscQueryService>.Instance,
            null!, null!, null!, null!, null!, null!, null!, null!, null!);
        internal readonly BinaryFaceDataSender Sender;
        public NativeSession()
        {
            ParameterSenderService.Clear();
            var target = new LoopbackTarget(); send = new(NullLogger<OscSendService>.Instance, target);
            target.Connect(((IPEndPoint)receiver.Client.LocalEndPoint!).Port);
            parameters = new(send, null!);
            Sender = new(Query); Sender.StartAsync(default).GetAwaiter().GetResult();
            Query.AvatarInfo = Avatar(true);
        }
        internal byte[] Emit()
        {
            UnifiedTracking.OnUnifiedDataUpdated(UnifiedTracking.Data);
            parameters.FlushAsync(default).GetAwaiter().GetResult();
            using var deadline = new CancellationTokenSource(3000);
            var datagram = receiver.ReceiveAsync(deadline.Token).AsTask().GetAwaiter().GetResult().Buffer;
            // Native eye parameters may share the bundle. Locate the face message;
            // "one blob argument" constrains that message, not the entire datagram.
            Check(Encoding.ASCII.GetString(datagram, 0, 8) == "#bundle\0", "Official sender produces a bundle");
            var faces = new List<byte[]>();
            for (int offset = 16; offset < datagram.Length;)
            {
                int length = BinaryPrimitives.ReadInt32BigEndian(datagram.AsSpan(offset, 4)); offset += 4;
                var entry = datagram.AsSpan(offset, length);
                int terminator = entry.IndexOf((byte)0);
                if (Encoding.ASCII.GetString(entry[..terminator]) == "/tracking/face/v1") faces.Add(entry.ToArray());
                offset += length;
            }
            Check(faces.Count == 1, "Exactly one face message in the host's OSC bundle");
            var message = faces[0].AsSpan();
            Check(message.Length == 132, "Native face OSC message is exactly 132 bytes");
            Check(message[..20].SequenceEqual(Encoding.ASCII.GetBytes("/tracking/face/v1\0\0\0")), "Native OSC address and alignment");
            Check(message.Slice(20, 4).SequenceEqual(new byte[] { (byte)',', (byte)'b', 0, 0 }), "Exactly one blob argument");
            Check(BinaryPrimitives.ReadInt32BigEndian(message.Slice(24, 4)) == 102, "Big-endian blob payload length");
            Check(message[130] == 0 && message[131] == 0, "OSC padding is not part of the blob");
            return message.Slice(28, 102).ToArray();
        }
        public void Dispose()
        {
            Sender.StopAsync(default).GetAwaiter().GetResult();
            // The pinned upstream retains its anonymous capability-event subscription.
            // No more Query events are raised after cleanup in this test-only instance.
            foreach (var callback in UnifiedTracking.OnUnifiedDataUpdated.GetInvocationList().Where(d => d.Target == Sender))
                UnifiedTracking.OnUnifiedDataUpdated = (Action<UnifiedTrackingData>)Delegate.Remove(UnifiedTracking.OnUnifiedDataUpdated, callback)!;
            Get<OscMessage>(Sender, "_blobMessage").Dispose();
            ParameterSenderService.Clear();
            Get<Socket>(send, "_sendSocket").Dispose(); receiver.Dispose();
        }
    }
    private static OscQueryAvatarInfo Avatar(bool? native)
    {
        var nodes = new Dictionary<string, OscQueryNode> {
            ["parameters"] = new() { Contents = new() {
                ["v2/JawOpen"] = new() { FullPath = "/avatar/parameters/v2/JawOpen", OscType = "f" },
                ["v2/BrowInnerUp"] = new() { FullPath = "/avatar/parameters/v2/BrowInnerUp", OscType = "f" }
            } },
            ["change"] = new() { Value = ["avtr_compatibility_fixture"] }
        };
        if (native.HasValue) nodes["avatarfullfacetracking"] = new() { Value = [native.Value] };
        return new(new OscQueryNode { FullPath = "/avatar", Contents = nodes });
    }
    private static void NativeKeyOrder()
    {
        var keys = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "face-v1-keys.txt"));
        Check(keys.Length == 102 && UnifiedTrackingData.SerializedLength == 102, "Official protocol fixture size");
        using var session = new NativeSession();
        for (int slot = 14; slot < 102; slot++)
        {
            ResetData(); var channel = Enum.Parse<UnifiedExpressions>(keys[slot]);
            Check((int)channel == slot - 14, $"Host expression order: {keys[slot]}");
            Weight(channel, 1);
            Bytes(session.Emit(), Expected((slot, 1)), keys[slot]);
        }
        foreach (float value in new[] { -0.1f, 0, 0.001f, 0.1f, 0.5f, 0.999f, 1, 1.1f })
        {
            ResetData(); Weight(UnifiedExpressions.JawOpen, value);
            Bytes(session.Emit(), Expected((36, value)), $"Clamped/truncated quantization {value}");
        }
    }
    private static void NativeEyes()
    {
        using var session = new NativeSession();
        var vectors = new[] {
            (PicoShape.EyeLookOutR, 0), (PicoShape.EyeLookInR, 1), (PicoShape.EyeLookUpR, 2), (PicoShape.EyeLookDownR, 3),
            (PicoShape.EyeLookOutL, 4), (PicoShape.EyeLookInL, 5), (PicoShape.EyeLookUpL, 6), (PicoShape.EyeLookDownL, 7),
            (PicoShape.EyeBlinkR, 8), (PicoShape.EyeBlinkL, 9) };
        foreach (var (shape, slot) in vectors)
        {
            ResetData(); var source = new SnapshotSource(Snapshot(Face(MonotonicClock.Now, values: [(shape, 1)])));
            var module = ActiveModule(new(), true, true, source);
            try { module.Update(); Bytes(session.Emit(), Expected((slot, 1)), shape.ToString()); }
            finally { module.Teardown(); }
        }
    }
    private static void NativeProcessing()
    {
        ResetData(); using var session = new NativeSession();
        var config = new ModuleConfig(); config.EyeGain.LeftX = 2;
        config.PostProcessing.ChannelGain.Gains = new() { ["EyeWideLeft"] = 3, ["JawOpen"] = 1.5f, ["TongueOut"] = 0.5f };
        config.PostProcessing.TongueJawGate = new() { Enabled = true, JawThreshold = 0.5f, TransitionWidth = 0, MaxReduction = 0.5f };
        var source = new SnapshotSource(Snapshot(Face(MonotonicClock.Now, independent: new(0.7f, 1, 1), values: [
            (PicoShape.EyeLookInL, 0.3f), (PicoShape.BrowInnerUp, 0.2f), (PicoShape.BrowOuterUpL, 0.4f),
            (PicoShape.BrowOuterUpR, 0.8f), (PicoShape.EyeWideL, 0.2f), (PicoShape.JawOpen, 0.2f), (PicoShape.TongueOut, 0.8f)])));
        var module = ActiveModule(config, true, true, source);
        try
        {
            module.Update(); var expected = Expected((5, 0.6f), (17, 0.6f), (22, 0.7f), (23, 0.2f), (24, 0.8f),
                (25, 0.4f), (36, 0.3f), (86, 0.2f));
            Bytes(session.Emit(), expected, "PC gain/gate and independent brows reach native output");
            module.Update(); Bytes(session.Emit(), expected, "Repeated SDK poll does not compound postprocessing");
        }
        finally { module.Teardown(); }
    }
    private static void NativeMixed()
    {
        using var session = new NativeSession();
        var eyeSource = new SnapshotSource(Snapshot(Face(MonotonicClock.Now, values: [
            (PicoShape.EyeLookOutR, 0.4f), (PicoShape.BrowOuterUpL, 0.8f), (PicoShape.BrowOuterUpR, 0.6f), (PicoShape.JawOpen, 0.1f)])));
        var mouthSource = new SnapshotSource(Snapshot(Face(MonotonicClock.Now, values: [
            (PicoShape.JawOpen, 0.7f), (PicoShape.TongueOut, 0.5f), (PicoShape.BrowOuterUpL, 0.1f)])));
        var eyes = ActiveModule(new(), true, false, eyeSource);
        var mouth = ActiveModule(new(), false, true, mouthSource);
        try
        {
            ResetData(); eyes.Update(); var eyePacket = Capture();
            ResetData(); Weight(UnifiedExpressions.BrowOuterUpLeft, 0xFFFFFFFF); mouth.Update();
            Near(Weight(UnifiedExpressions.BrowOuterUpLeft), 0xFFFFFFFF, "Mouth owner leaves foreign eye sentinel intact");
            var mouthPacket = Capture();
            foreach (bool eyeFirst in new[] { true, false })
            {
                ResetData();
                if (eyeFirst) { eyePacket.UpdateGlobalEyeState(); mouthPacket.UpdateGlobalExpressionState(); }
                else { mouthPacket.UpdateGlobalExpressionState(); eyePacket.UpdateGlobalEyeState(); }
                Bytes(session.Emit(), Expected((0, 0.4f), (24, 0.6f), (25, 0.8f), (36, 0.7f), (86, 0.5f)), "Sentinel-preserving mixed IPC");
            }
            // A faulted source leaves the configured default held output while the other
            // source continues; Teardown neutralizes only the mouth-owned channels.
            mouthSource.Snapshot = mouthSource.Snapshot with { State = ConnectionState.Faulted, Face = null };
            mouth.Update(); Near(Weight(UnifiedExpressions.JawOpen), 0.7f, "Mouth interruption holds last value");
            mouth.Teardown(); eyes.Update();
            Bytes(session.Emit(), Expected((0, 0.4f), (24, 0.6f), (25, 0.8f)), "Mouth stop preserves eye output");
            Console.WriteLine("MIXED: synthetic independent mouth owner; real Cymple module is not exercised.");
        }
        finally { mouth.Teardown(); eyes.Teardown(); }
    }
    private static void NativeRouting()
    {
        using var session = new NativeSession();
        Check(!Avatar(null).FullFaceTracking && !Avatar(false).FullFaceTracking && Avatar(true).FullFaceTracking,
            "Actual OSCQuery capability parsing");
        var legacy = Avatar(false); var native = Avatar(true);
        var parameters = UnifiedExpressionsParameters.ExpressionParameters;
        try
        {
            foreach (var parameter in parameters) parameter.ResetParam(legacy.Parameters, legacy);
            session.Query.AvatarInfo = legacy;
            ParameterSenderService.Clear(); ResetData(); Weight(UnifiedExpressions.JawOpen, 0.6f);
            Weight(UnifiedExpressions.BrowInnerUpLeft, 1); Weight(UnifiedExpressions.BrowInnerUpRight, 0);
            UnifiedTracking.OnUnifiedDataUpdated(UnifiedTracking.Data);
            var messages = SendQueue.ToArray();
            Near((float)messages.Single(m => m.Address == "/avatar/parameters/v2/JawOpen").Value, 0.6f, "Actual legacy JawOpen formula");
            Near((float)messages.Single(m => m.Address == "/avatar/parameters/v2/BrowInnerUp").Value, 0.5f, "Actual legacy brow-average formula");
            Check(messages.All(m => m.Address != "/tracking/face/v1"), "Capability event disables binary sender for legacy avatar");
            foreach (var parameter in parameters) parameter.ResetParam(native.Parameters, native);
            session.Query.AvatarInfo = native; ParameterSenderService.Clear();
            Bytes(session.Emit(), Expected((22, 0), (23, 1), (36, 0.6f)), "Native capability disables legacy output");
            foreach (var parameter in parameters) parameter.ResetParam(legacy.Parameters, legacy);
            session.Query.AvatarInfo = legacy; ParameterSenderService.Clear(); Weight(UnifiedExpressions.JawOpen, 0.3f);
            UnifiedTracking.OnUnifiedDataUpdated(UnifiedTracking.Data);
            Near((float)SendQueue.Single(m => m.Address == "/avatar/parameters/v2/JawOpen").Value, 0.3f, "Legacy output resumes after avatar switch");
            Check(SendQueue.All(m => m.Address != "/tracking/face/v1"), "Switching back to legacy disables binary output again");
        }
        finally
        {
            foreach (var parameter in parameters) parameter.ResetParam([], native);
            ParameterSenderService.Clear();
        }
    }
    private static void NativePupils()
    {
        using var session = new NativeSession();
        foreach (bool measured in new[] { false, true })
        {
            ResetData(); var config = new ModuleConfig(); var time = MonotonicClock.Now;
            var snapshot = Snapshot(Face(time));
            if (measured)
            {
                var left = new NativeEye(new(0, 0, 1), false, 1, false, 7.5f, true);
                var right = left with { PupilMillimeters = 2.5f };
                snapshot = snapshot with { Eye = new(left, right, new(0, 0, 1), false, 1, time), EyeSequence = 1 };
            }
            var resolved = new UnifiedPicoModule.Runtime.TrackingPipeline(config).Evaluate(snapshot, time);
            Check(resolved.Left.RealPupil == measured && resolved.Right.RealPupil == measured,
                "Measured provenance is not inferred from a nonzero millimeter default");
            var module = ActiveModule(config, true, true, new(snapshot));
            try
            {
                module.Update();
                Near(UnifiedTracking.Data.Eye.Left.PupilDiameter_MM, measured ? 7.5f : 0, "Measured millimeters or SDK unavailable sentinel");
                Near(UnifiedTracking.Data.Eye.Right.PupilDiameter_MM, measured ? 2.5f : 0, "Independent right pupil");
                Bytes(session.Emit(), measured ? Expected((11, 0.5f), (12, 0.5f)) : Expected(),
                    "Real per-eye pupils reach native output; estimates remain neutral");
            }
            finally { module.Teardown(); }
        }
    }
    private static void NativePupilVectors()
    {
        using var session = new NativeSession();
        foreach (var (diameter, dilation, constriction) in new[] {
            (0f, 0f, 0f), (-1f, 0f, 0f), (float.NaN, 0f, 0f), (float.PositiveInfinity, 0f, 0f),
            (5f, 0f, 0f), (2.5f, 0f, 0.5f), (7.5f, 0.5f, 0f), (1.25f, 0f, 0.75f), (10f, 1f, 0f), (12f, 1f, 0f) })
        {
            ResetData(); UnifiedTracking.Data.Eye.Left.PupilDiameter_MM = diameter;
            UnifiedTracking.Data.Eye.Right.PupilDiameter_MM = diameter;
            Bytes(session.Emit(), Expected((10, dilation), (11, dilation), (12, constriction), (13, constriction)),
                $"Official 6 pupil convention: {diameter}");
        }
    }
    private static void NativePupilExpiry()
    {
        using var session = new NativeSession();
        foreach (bool neutralize in new[] { false, true })
        {
            ResetData(); var time = MonotonicClock.Now;
            var eye = new NativeEye(new(0, 0, 1), false, 1, false, 7.5f, true);
            var config = new ModuleConfig(); config.Stale.NeutralizeOnTimeout = neutralize;
            var source = new SnapshotSource(Snapshot(Face(time)) with {
                Eye = new(eye, eye with { PupilValid = false }, new(0, 0, 1), false, 1, time), EyeSequence = 1 });
            var module = ActiveModule(config, true, true, source);
            try
            {
                module.Update(); Bytes(session.Emit(), Expected((11, 0.5f)), "Invalid right pupil does not borrow left measurement");
                source.Snapshot = source.Snapshot with { Eye = source.Snapshot.Eye! with { Arrival = time - 1 } };
                // Age the previously consumed pupil without sleeping or refreshing its source.
                var pipeline = Get<UnifiedPicoModule.Runtime.TrackingPipeline>(module, "pipeline");
                var output = pipeline.Evaluate(source.Snapshot, time + 1);
                typeof(UnifiedPicoModule.Sdk.UnifiedPicoTrackingModule).GetMethod("Write", Hidden)!.Invoke(module, [output]);
                Bytes(session.Emit(), neutralize ? Expected() : Expected((11, 0.5f)), "Configured pupil holding versus timeout neutralization");
                module.Teardown(); Bytes(session.Emit(), Expected(), "Teardown clears pupil output to unavailable");
            }
            finally { module.Teardown(); }
        }
    }
    private static void NativeSenderBoundaries()
    {
        using var session = new NativeSession();
        session.Sender.StopAsync(default).GetAwaiter().GetResult();
        ParameterSenderService.Clear(); UnifiedTracking.OnUnifiedDataUpdated(UnifiedTracking.Data);
        Check(SendQueue.All(m => m.Address != "/tracking/face/v1"), "Stop detaches data callback");
        session.Query.AvatarInfo = Avatar(false); session.Query.AvatarInfo = Avatar(true);
        UnifiedTracking.OnUnifiedDataUpdated(UnifiedTracking.Data);
        Check(SendQueue.Count(m => m.Address == "/tracking/face/v1") == 1, "Pinned upstream capability callback can resubscribe after Stop");
        Console.WriteLine("KNOWN-UPSTREAM: capability changes can resubscribe a stopped BinaryFaceDataSender.");
        ParameterSenderService.Clear();
        session.Sender.StopAsync(default).GetAwaiter().GetResult();
        var query = new OscQueryService(NullLogger<OscQueryService>.Instance,
            null!, null!, null!, null!, null!, null!, null!, null!, null!) { AvatarInfo = Avatar(false) };
        var started = new BinaryFaceDataSender(query);
        try
        {
            started.StartAsync(default).GetAwaiter().GetResult();
            UnifiedTracking.OnUnifiedDataUpdated(UnifiedTracking.Data);
            Check(SendQueue.Count(m => m.Address == "/tracking/face/v1") == 1, "Pinned sender starts subscribed even with an existing legacy capability");
            Console.WriteLine("KNOWN-UPSTREAM: native sender subscribes at startup until the next avatar capability event.");
        }
        finally { started.StopAsync(default).GetAwaiter().GetResult(); Get<OscMessage>(started, "_blobMessage").Dispose(); ParameterSenderService.Clear(); }
    }

    private static void NativeDisposedMessage()
    {
        using var session = new NativeSession(); ResetData();
        using var disposed = OscMessage.CreateBlob("/compat/disposed", new byte[102], 102);
        disposed.Dispose(); ParameterSenderService.Enqueue(disposed);
        Weight(UnifiedExpressions.JawOpen, 0.5f);
        Bytes(session.Emit(), Expected((36, 0.5f)), "Official sender skips disposed messages without losing the valid face blob");
    }
}
#endif
