using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using UnifiedPicoModule.Configuration;
using UnifiedPicoModule.Core;
using UnifiedPicoModule.Protocol;
using UnifiedPicoModule.Runtime;
using UnifiedPicoModule.Sdk;
using UnifiedPicoModule.Transport;
using VRCFaceTracking;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;

var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Action test) => tests.Add((name, () => { test(); return Task.CompletedTask; }));
void Check(bool value, string message = "assertion") { if (!value) throw new InvalidOperationException(message); }
void Near(float value, float expected) => Check(Math.Abs(value - expected) < .00001f, $"{value} != {expected}");
TjrPacket Packet(ulong seq = 10, TongueState state = TongueState.Present, ulong session = 100, float age = 0) =>
    new(session, seq, 1_000_000_000 + seq * 11_111_111, 12345, .6f, .1f, .8f, age, state);
ReceiveResult Send(TongueReceiver receiver, TjrPacket packet) => receiver.Receive(packet.Encode());
const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
void Set(object target, string field, object? value) => target.GetType().GetField(field, Hidden)!.SetValue(target, value);
T? Get<T>(object target, string field) => (T?)target.GetType().GetField(field, Hidden)!.GetValue(target);
void Call(UnifiedPicoTrackingModule module, string method, params object[] values) =>
    typeof(UnifiedPicoTrackingModule).GetMethod(method, Hidden)!.Invoke(module, values);
UnifiedPicoTrackingModule Module(bool ownsFace = true) {
    var module = new UnifiedPicoTrackingModule { Logger = NullLogger.Instance, ModuleInformation = new() };
    Set(module, "ownsFace", ownsFace);
    return module;
}
TrackingOutput Output(float outValue) {
    var frame = new ExpressionFrame(); frame[ExpressionChannel.TongueOut] = outValue;
    frame[ExpressionChannel.JawOpen] = .23f;
    return new(frame, new(), new());
}
void Sentinel() {
    var data = new UnifiedTrackingData();
    foreach (ref var shape in data.Shapes.AsSpan()) shape.Weight = .731f;
    UnifiedTracking.Data = data;
}
float Weight(UnifiedExpressions channel) => UnifiedTracking.Data.Shapes[(int)channel].Weight;
MfrPacket MouthPacket(ulong seq = 10, ulong mask = MfrPacket.AllValid, ulong session = 100,
    MouthState state = MouthState.Fresh, float age = 0) =>
    new(session, seq, 1_000_000_000 + seq * 11_111_111, 12345, mask, age, state,
        Enumerable.Range(0, 50).Select(i => (mask & (1UL << i)) == 0 ? 0 : (i + 1) / 50f).ToArray());
ulong Mask(params MouthChannel[] channels) => channels.Aggregate(0UL, (mask, channel) => mask | 1UL << (int)channel);
ReceiveResult SendMouth(TongueReceiver receiver, MfrPacket packet) => receiver.Receive(packet.Encode());
async Task Wait(Func<bool> predicate) {
    var elapsed = Stopwatch.StartNew();
    while (!predicate()) { if (elapsed.ElapsedMilliseconds > 2000) throw new TimeoutException("loopback test"); await Task.Delay(2); }
}

// Deterministic batch API for external producers. Each JSONL row contains packet (base64,
// optional for expiry-only steps), advanceMs, ownsFace, accepted/reason, and expected SDK weights.
if (args.FirstOrDefault() == "--mfr-batch") {
    var module = Module(); Sentinel(); double now = 1000;
    using var receiver = new TongueReceiver(() => now); Set(module, "tongue", receiver);
    var rows = new List<object>();
    foreach (string line in File.ReadLines(args[1])) {
        using var doc = JsonDocument.Parse(line); var row = doc.RootElement;
        now += row.TryGetProperty("advanceMs", out var advance) ? advance.GetDouble() : 0;
        bool owns = !row.TryGetProperty("ownsFace", out var face) || face.GetBoolean(); Set(module, "ownsFace", owns);
        ReceiveResult? result = row.TryGetProperty("packet", out var encoded)
            ? receiver.Receive(Convert.FromBase64String(encoded.GetString()!)) : null;
        Call(module, "Write", Output(.17f));
        if (row.TryGetProperty("accepted", out var accepted)) Check(result?.Accepted == accepted.GetBoolean(), "batch accepted");
        if (row.TryGetProperty("reason", out var reason)) Check(result?.Reason == reason.GetString(), "batch reason: " + result?.Reason);
        if (row.TryGetProperty("expected", out var expected))
            foreach (var shape in expected.EnumerateObject()) Near(Weight(Enum.Parse<UnifiedExpressions>(shape.Name)), shape.Value.GetSingle());
        // MFR never changes PICO eyes, regardless of face ownership.
        Near(Weight(UnifiedExpressions.EyeWideLeft), .731f);
        rows.Add(new { accepted = result?.Accepted, reason = result?.Reason, status = receiver.Snapshot().Status,
            shapes = Enum.GetValues<UnifiedExpressions>().ToDictionary(c => c.ToString(), Weight) });
    }
    var report = new { passed = true, sdk = typeof(UnifiedTrackingData).Assembly.GetName().Version!.ToString(),
        schema = MfrPacket.Schema, channels = Enum.GetNames<MouthChannel>(), rows };
    File.WriteAllText(args[2], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    Console.WriteLine($"PASS MFR batch {rows.Count} steps"); return 0;
}

// A separate synthetic loopback process for checking another producer's actual wire encoder.
if (args.FirstOrDefault() == "--probe") {
    var module = Module(!args.Contains("--no-face")); Sentinel();
    Set(module, "pipeline", new TrackingPipeline(new ModuleConfig()));
    using var receiver = new TongueReceiver(); Set(module, "tongue", receiver);
    var packets = new ConcurrentQueue<ReceiveResult>();
    int port;
    await using (var udp = new TjrUdpReceiver(receiver, 0, packets.Enqueue)) {
        port = udp.Port; Console.WriteLine(JsonSerializer.Serialize(new { ready = true, port,
            schema = MfrPacket.Schema, channels = Enum.GetNames<MouthChannel>() }));
        var elapsed = Stopwatch.StartNew(); bool finished = false;
        while (!finished && elapsed.Elapsed.TotalSeconds < 10) {
            Call(module, "Write", Output(.17f));
            while (packets.TryDequeue(out var packet)) {
                Call(module, "Write", Output(.17f));
                Console.WriteLine(JsonSerializer.Serialize(new { accepted = packet.Accepted, reason = packet.Reason,
                    state = receiver.Snapshot().Status, @out = Weight(UnifiedExpressions.TongueOut),
                    left = Weight(UnifiedExpressions.TongueLeft), right = Weight(UnifiedExpressions.TongueRight),
                    up = Weight(UnifiedExpressions.TongueUp), down = Weight(UnifiedExpressions.TongueDown),
                    roll = Weight(UnifiedExpressions.TongueRoll),
                    shapes = Enum.GetValues<UnifiedExpressions>().ToDictionary(c => c.ToString(), Weight) }));
                finished |= packet.Accepted && packet.Packet?.State == TongueState.Stop;
                finished |= args.Contains("--finish-on-release") && packet.Accepted && packet.MouthPacket?.State == MouthState.Release;
            }
            await Task.Delay(2);
        }
        Check(finished, "Python producer did not terminate its synthetic session");
    }
    module.Teardown();
    using var rebound = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
    Console.WriteLine(JsonSerializer.Serialize(new { socket_released = true }));
    return 0;
}

Test("actual_official_sdk5_or_sdk6_and_tongue_fields", () => {
    var version = typeof(UnifiedTrackingData).Assembly.GetName().Version;
    Check(version == new Version(5, 4, 5, 0) || version == new Version(6, 0, 0, 0));
    Check(typeof(UnifiedPicoTrackingModule).Assembly.GetReferencedAssemblies()
        .Single(a => a.Name == "VRCFaceTracking.Core").Version == new Version(5, 4, 5, 0),
        "The module must compile against the pinned SDK, not an archived host DLL.");
    foreach (string name in new[] { "TongueLeft", "TongueRight", "TongueOut", "TongueUp", "TongueDown" })
        Check(Enum.IsDefined(typeof(UnifiedExpressions), name));
});
Test("host_pupil_convention_preserves_measurements_and_foreign_eye_ownership", () => {
    Sentinel(); var module = Module(false); Set(module, "ownsEye", true);
    var left = new ResolvedEye(PupilMillimeters: 7.5f, RealPupil: true);
    var right = new ResolvedEye(PupilMillimeters: 4);
    float unavailable = typeof(UnifiedTrackingData).Assembly.GetName().Version!.Major >= 6 ? 0 : 4;
    Call(module, "Write", new TrackingOutput(new(), left, right));
    Near(UnifiedTracking.Data.Eye.Left.PupilDiameter_MM, 7.5f);
    Near(UnifiedTracking.Data.Eye.Right.PupilDiameter_MM, unavailable);
    Call(module, "Write", new TrackingOutput(new(), right, left with { PupilMillimeters = 2.5f }));
    Near(UnifiedTracking.Data.Eye.Left.PupilDiameter_MM, unavailable);
    Near(UnifiedTracking.Data.Eye.Right.PupilDiameter_MM, 2.5f);
    Set(module, "ownsEye", false); Call(module, "Write", Output(.17f));
    Near(UnifiedTracking.Data.Eye.Left.PupilDiameter_MM, unavailable);
    Near(UnifiedTracking.Data.Eye.Right.PupilDiameter_MM, 2.5f);
    module.Teardown();
});
Test("mfr_exact_layout_all_50_fields_and_roundtrip", () => {
    var packet = MouthPacket(); var bytes = packet.Encode();
    Check(bytes.Length == 256 && bytes.AsSpan(0, 4).SequenceEqual("MFR1"u8));
    Check(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)) == 1);
    Check(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6)) == 256);
    Check(BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(40)) == MfrPacket.AllValid);
    Near(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(48)), 0);
    Check(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(52)) == 0);
    Check(MfrPacket.TryParse(bytes, out var decoded, out _));
    Check(decoded!.Encode().SequenceEqual(bytes));
    Check(Enum.GetNames<MouthChannel>().Length == 50 && (int)MouthChannel.MouthUpperLeft == 44);
    foreach (var c in Enum.GetValues<MouthChannel>()) Near(decoded[c], ((int)c + 1) / 50f);
});
Test("mfr_active_overrides_valid_channels_preserves_unset_native_and_release_or_expiry_clears_extensions", () => {
    var module = Module(); Sentinel(); double now = 1000;
    using var receiver = new TongueReceiver(() => now); Set(module, "tongue", receiver);
    var values = new float[50];
    values[(int)MouthChannel.MouthLeft] = .31f;
    values[(int)MouthChannel.MouthUpperLeft] = .81f;
    values[(int)MouthChannel.CheekSuckLeft] = .44f;
    values[(int)MouthChannel.TongueUp] = .52f;
    var partial = MouthPacket(seq: 1, mask: Mask(MouthChannel.MouthLeft, MouthChannel.MouthUpperLeft,
        MouthChannel.CheekSuckLeft, MouthChannel.TongueUp)) with { Values = values };
    Check(!partial.CoversAllNativeMouth, "Partial mask must not claim full native mouth coverage");
    SendMouth(receiver, partial);
    Call(module, "Write", Output(.17f));
    Near(Weight(UnifiedExpressions.MouthUpperLeft), .81f);
    Near(Weight(UnifiedExpressions.MouthLowerLeft), .31f);
    Near(Weight(UnifiedExpressions.CheekSuckLeft), .44f);
    Near(Weight(UnifiedExpressions.TongueUp), .52f);
    // Partial MFR preserves unset native Bridge/pipeline channels instead of zeroing them.
    Near(Weight(UnifiedExpressions.JawOpen), .23f);
    Near(Weight(UnifiedExpressions.TongueOut), .17f);
    Near(Weight(UnifiedExpressions.EyeWideLeft), .731f);

    // Explicit off (valid bit=1, value=0) still zeros the targeted native channel.
    var explicitOff = MouthPacket(seq: 2, mask: Mask(MouthChannel.JawOpen, MouthChannel.CheekSuckLeft))
        with { Values = new float[50] };
    SendMouth(receiver, explicitOff);
    Call(module, "Write", Output(.17f));
    Near(Weight(UnifiedExpressions.JawOpen), 0);
    Near(Weight(UnifiedExpressions.TongueOut), .17f);
    Near(Weight(UnifiedExpressions.CheekSuckLeft), 0);
    Near(Weight(UnifiedExpressions.TongueUp), 0);

    // Explicit release restores direct pipeline mouth values and clears non-native extensions.
    SendMouth(receiver, MouthPacket(seq: 3, mask: Mask(MouthChannel.MouthLeft, MouthChannel.MouthUpperLeft,
        MouthChannel.CheekSuckLeft, MouthChannel.TongueUp)) with { Values = values });
    Call(module, "Write", Output(.17f));
    SendMouth(receiver, MouthPacket(seq: 3, mask: 0, state: MouthState.Release));
    Call(module, "Write", Output(.17f));
    Near(Weight(UnifiedExpressions.JawOpen), .23f);
    Near(Weight(UnifiedExpressions.TongueOut), .17f);
    Near(Weight(UnifiedExpressions.CheekSuckLeft), 0);
    Near(Weight(UnifiedExpressions.TongueUp), 0);
    Near(Weight(UnifiedExpressions.EyeWideLeft), .731f);

    // Expiry after another active MFR packet behaves identically.
    SendMouth(receiver, MouthPacket(seq: 4, mask: Mask(MouthChannel.MouthLeft, MouthChannel.MouthUpperLeft,
        MouthChannel.CheekSuckLeft, MouthChannel.TongueUp)) with { Values = values });
    Call(module, "Write", Output(.17f));
    Near(Weight(UnifiedExpressions.JawOpen), .23f);
    Near(Weight(UnifiedExpressions.TongueUp), .52f);
    now += 351;
    Call(module, "Write", Output(.17f));
    Near(Weight(UnifiedExpressions.JawOpen), .23f);
    Near(Weight(UnifiedExpressions.TongueOut), .17f);
    Near(Weight(UnifiedExpressions.CheekSuckLeft), 0);
    Near(Weight(UnifiedExpressions.TongueUp), 0);
    Set(module, "ownsFace", false); Sentinel(); Call(module, "Write", Output(.17f));
    Check(UnifiedTracking.Data.Shapes.All(s => s.Weight == .731f)); module.Teardown();
});
Test("mfr_rejects_lengths_headers_mask_states_and_nonfinite_or_unknown_values", () => {
    var packet = MouthPacket(mask: 1); var bytes = packet.Encode();
    for (int size = 0; size < 256; size++) Check(!MfrPacket.TryParse(bytes.AsSpan(0, size), out _, out _));
    Check(!MfrPacket.TryParse(new byte[257], out _, out _));
    foreach (int offset in new[] { 0, 4, 6, 52 }) {
        var bad = (byte[])bytes.Clone(); bad[offset] = 99; Check(!MfrPacket.TryParse(bad, out _, out _));
    }
    foreach (var p in new[] { packet with { SessionId = 0 }, packet with { ValidMask = 1UL << 50 },
        packet with { ValidMask = 0 }, packet with { State = MouthState.Release }, packet with { SourceAgeMs = -1 },
        packet with { SourceAgeMs = float.NaN } }) Check(!MfrPacket.TryParse(p.Encode(), out _, out _));
    foreach (float value in new[] { float.NaN, float.PositiveInfinity, -1, 1.001f }) {
        var values = packet.Values.ToArray(); values[0] = value;
        Check(!MfrPacket.TryParse((packet with { Values = values }).Encode(), out _, out _));
    }
    foreach (float value in new[] { .1f, float.NaN }) {
        var values = packet.Values.ToArray(); values[1] = value;
        Check(!MfrPacket.TryParse((packet with { Values = values }).Encode(), out _, out _));
    }
    Check(MfrPacket.TryParse(MouthPacket(mask: 0, state: MouthState.Release).Encode(), out _, out _));
});
Test("mfr_independent_lips_and_cheeks_override_aggregate_fanout_only_where_valid", () => {
    var module = Module(); Sentinel(); using var receiver = new TongueReceiver(); Set(module, "tongue", receiver);
    SendMouth(receiver, MouthPacket(mask: Mask(MouthChannel.CheekPuff, MouthChannel.CheekPuffLeft,
        MouthChannel.MouthLeft, MouthChannel.MouthUpperLeft, MouthChannel.MouthFunnel, MouthChannel.LipFunnelLower)));
    Call(module, "Write", Output(.17f));
    Near(Weight(UnifiedExpressions.CheekPuffLeft), .76f); Near(Weight(UnifiedExpressions.CheekPuffRight), .02f);
    Near(Weight(UnifiedExpressions.MouthUpperLeft), .9f); Near(Weight(UnifiedExpressions.MouthLowerLeft), .28f);
    Near(Weight(UnifiedExpressions.LipFunnelUpperLeft), .26f); Near(Weight(UnifiedExpressions.LipFunnelUpperRight), .26f);
    Near(Weight(UnifiedExpressions.LipFunnelLowerLeft), 1); Near(Weight(UnifiedExpressions.LipFunnelLowerRight), 1);
    Near(Weight(UnifiedExpressions.JawOpen), .23f); Near(Weight(UnifiedExpressions.TongueOut), .17f);
    Near(Weight(UnifiedExpressions.CheekSuckLeft), .731f); Near(Weight(UnifiedExpressions.TongueUp), .731f);
    Near(Weight(UnifiedExpressions.EyeWideLeft), .731f); module.Teardown();
});
Test("mfr_upper_only_keeps_valid_aggregate_lower_or_bridge_and_tongue_values_are_not_tjr_scaled", () => {
    var module = Module(); Sentinel(); using var receiver = new TongueReceiver(); Set(module, "tongue", receiver);
    var values = new float[50];
    values[(int)MouthChannel.MouthUpperLeft] = .8f; values[(int)MouthChannel.TongueLeft] = .1f;
    values[(int)MouthChannel.TongueUp] = .2f; values[(int)MouthChannel.TongueOut] = .05f;
    SendMouth(receiver, MouthPacket(mask: Mask(MouthChannel.MouthUpperLeft, MouthChannel.TongueLeft,
        MouthChannel.TongueUp, MouthChannel.TongueOut)) with { Values = values });
    var output = Output(.17f); output.Expressions[ExpressionChannel.MouthLowerLeft] = .37f;
    Call(module, "Write", output);
    Near(Weight(UnifiedExpressions.MouthUpperLeft), .8f); Near(Weight(UnifiedExpressions.MouthLowerLeft), .37f);
    Near(Weight(UnifiedExpressions.TongueOut), .05f); Near(Weight(UnifiedExpressions.TongueLeft), .1f);
    Near(Weight(UnifiedExpressions.TongueUp), .2f); Near(Weight(UnifiedExpressions.TongueRoll), .731f);
    values[(int)MouthChannel.MouthLeft] = .28f;
    SendMouth(receiver, MouthPacket(11, mask: Mask(MouthChannel.MouthLeft, MouthChannel.MouthUpperLeft,
        MouthChannel.TongueLeft, MouthChannel.TongueUp, MouthChannel.TongueOut)) with { Values = values });
    Call(module, "Write", output);
    Near(Weight(UnifiedExpressions.MouthUpperLeft), .8f); Near(Weight(UnifiedExpressions.MouthLowerLeft), .28f);
    values[(int)MouthChannel.TongueOut] = 0; values[(int)MouthChannel.MouthUpperLeft] = 0; values[(int)MouthChannel.MouthLeft] = 0;
    SendMouth(receiver, MouthPacket(12, mask: Mask(MouthChannel.TongueLeft, MouthChannel.TongueUp, MouthChannel.TongueOut))
        with { Values = values });
    Call(module, "Write", output); Near(Weight(UnifiedExpressions.TongueLeft), .1f); Near(Weight(UnifiedExpressions.TongueUp), .2f);
    Near(Weight(UnifiedExpressions.MouthLowerLeft), .37f);
    module.Teardown();
});
Test("mfr_expiry_mask_loss_and_teardown_clear_owned_extensions_and_keep_bridge_native", () => {
    var module = Module(); Sentinel(); double now = 1000; using var receiver = new TongueReceiver(() => now);
    Set(module, "tongue", receiver); Set(module, "pipeline", new TrackingPipeline(new ModuleConfig()));
    SendMouth(receiver, MouthPacket(mask: Mask(MouthChannel.CheekSuckLeft, MouthChannel.MouthTightenerRight, MouthChannel.JawOpen)));
    Call(module, "Write", Output(.17f)); Near(Weight(UnifiedExpressions.CheekSuckLeft), .8f);
    Near(Weight(UnifiedExpressions.JawOpen), .12f);
    SendMouth(receiver, MouthPacket(11, mask: Mask(MouthChannel.MouthTightenerRight)));
    Call(module, "Write", Output(.42f)); Near(Weight(UnifiedExpressions.CheekSuckLeft), 0);
    Near(Weight(UnifiedExpressions.JawOpen), .23f); Near(Weight(UnifiedExpressions.CheekSuckRight), .731f);
    now += 351; Call(module, "Write", Output(.42f)); Near(Weight(UnifiedExpressions.MouthTightenerRight), 0);
    Near(Weight(UnifiedExpressions.TongueLeft), .731f); Near(Weight(UnifiedExpressions.TongueOut), .42f);
    Near(Weight(UnifiedExpressions.JawOpen), .23f);
    now += 1; SendMouth(receiver, MouthPacket(1, session: 200, mask: Mask(MouthChannel.CheekSuckRight)));
    Call(module, "Write", Output(.42f)); module.Teardown(); Near(Weight(UnifiedExpressions.CheekSuckRight), 0);
    Near(Weight(UnifiedExpressions.TongueUp), .731f);
});
Test("mfr_native_coverage_contract_handles_split_pairs_and_partial_six_tongue_mask", () => {
    const ulong sixTongueMask = 0x81F00000000UL;
    Check(sixTongueMask == Mask(MouthChannel.TongueOut, MouthChannel.TongueLeft, MouthChannel.TongueRight,
        MouthChannel.TongueUp, MouthChannel.TongueDown, MouthChannel.TongueRoll));
    Check(!MouthPacket(mask: sixTongueMask).CoversAllNativeMouth);
    Check(MouthPacket(mask: MfrPacket.AllValid).CoversAllNativeMouth);
    Check(MouthPacket(mask: (1UL << 33) - 1).CoversAllNativeMouth);
    ulong splitCovered = ((1UL << 33) - 1) & ~Mask(MouthChannel.CheekPuff, MouthChannel.MouthFunnel,
        MouthChannel.MouthLeft, MouthChannel.MouthRight);
    splitCovered |= Mask(MouthChannel.CheekPuffLeft, MouthChannel.CheekPuffRight,
        MouthChannel.LipFunnelUpper, MouthChannel.LipFunnelLower,
        MouthChannel.MouthUpperLeft, MouthChannel.MouthLowerLeft,
        MouthChannel.MouthUpperRight, MouthChannel.MouthLowerRight);
    Check(MouthPacket(mask: splitCovered).CoversAllNativeMouth, "All 4 split pairs must cover their aggregates");
    Check(!MouthPacket(mask: splitCovered & ~Mask(MouthChannel.CheekPuffRight)).CoversAllNativeMouth,
        "Missing one split child without its aggregate parent must not claim full native coverage");
    Check(!MouthPacket(mask: splitCovered & ~Mask(MouthChannel.JawOpen)).CoversAllNativeMouth);
});
Test("mfr_never_writes_or_clears_foreign_face_after_ownership_loss", () => {
    var module = Module(); Sentinel(); using var receiver = new TongueReceiver(); Set(module, "tongue", receiver);
    SendMouth(receiver, MouthPacket()); Call(module, "Write", Output(.17f));
    Set(module, "ownsFace", false); Sentinel(); receiver.Stop();
    Call(module, "Write", Output(.17f)); Check(UnifiedTracking.Data.Shapes.All(s => s.Weight == .731f));
    Set(module, "pipeline", new TrackingPipeline(new ModuleConfig())); module.Teardown();
    Check(UnifiedTracking.Data.Shapes.All(s => s.Weight == .731f));
});
Test("mfr_shared_order_retirement_release_and_protocol_promotion", () => {
    using var receiver = new TongueReceiver(); Send(receiver, Packet());
    Check(SendMouth(receiver, MouthPacket(11)).Accepted);
    Check(Send(receiver, Packet(99, session: 999)).Reason == "legacy-after-mouth");
    Check(!SendMouth(receiver, MouthPacket(10, mask: 0, state: MouthState.Release)).Accepted);
    Check(receiver.Snapshot().Override);
    var release = SendMouth(receiver, MouthPacket(11, mask: 0, state: MouthState.Release));
    Check(release.Accepted && !release.NewSource && !receiver.Snapshot().Override);
    Check(!SendMouth(receiver, MouthPacket(11)).Accepted);
    Check(SendMouth(receiver, MouthPacket(12)).Accepted);
    Check(SendMouth(receiver, MouthPacket(1, session: 200)).Accepted);
    Check(SendMouth(receiver, MouthPacket(99)).Reason == "retired-session");
    Check(Send(receiver, Packet(99, session: 999)).Reason == "legacy-after-mouth");
});
Test("mfr_age_backlog_repeated_sensor_and_rejected_packets_cannot_extend_lease", () => {
    double now = 1000; using var receiver = new TongueReceiver(() => now);
    Near((float)SendMouth(receiver, MouthPacket(age: 300)).View.RemainingMs, 50);
    now += 49; Check(receiver.Snapshot().Override); now += 1; Check(!receiver.Snapshot().Override);
    Check(SendMouth(receiver, MouthPacket(11, age: 351)).Reason == "stale-source");
    Check(!SendMouth(receiver, MouthPacket(10)).Accepted);
    using var other = new TongueReceiver(() => now); var packet = MouthPacket(); SendMouth(other, packet);
    Check(SendMouth(other, packet with { SourceSeq = 11 }).Reason == "old-sensor");
    Check(!other.Snapshot().Override);
    using var backlog = new TongueReceiver(() => now); SendMouth(backlog, packet);
    for (ulong seq = 11; seq < 17; seq++) { now += 100; SendMouth(backlog, MouthPacket(seq)); }
    Check(!backlog.Snapshot().Override);
});
Test("mfr_is_direct_even_with_legacy_ceiling_and_unwritten_extensions_are_not_owned", () => {
    var module = Module(); Sentinel(); using var receiver = new TongueReceiver(); Set(module, "tongue", receiver);
    Set(module, "config", new ModuleConfig { LegacyExpressionCeiling = true });
    var values = Enumerable.Repeat(1f, 50).ToArray();
    SendMouth(receiver, MouthPacket() with { Values = values });
    Call(module, "Write", Output(.17f));
    foreach (var c in new[] { UnifiedExpressions.JawOpen, UnifiedExpressions.TongueOut,
        UnifiedExpressions.TongueLeft, UnifiedExpressions.CheekSuckLeft, UnifiedExpressions.MouthUpperLeft }) Near(Weight(c), 1);
    module.Teardown();
    module = Module(); Sentinel(); using var other = new TongueReceiver(); Set(module, "tongue", other);
    Set(module, "pipeline", new TrackingPipeline(new ModuleConfig()));
    SendMouth(other, MouthPacket()); // Received, but no SDK Write ever took ownership.
    module.Teardown();
    foreach (var c in new[] { UnifiedExpressions.CheekSuckLeft, UnifiedExpressions.TongueUp,
        UnifiedExpressions.TongueLeft, UnifiedExpressions.MouthTightenerLeft }) Near(Weight(c), .731f);
});
Test("mfr_pause_error_and_source_replacement_release_extensions_and_do_not_replay_old_results", () => {
    foreach (bool fail in new[] { false, true }) {
        var module = Module(); var config = new ModuleConfig(); config.Tongue.Port = 0;
        Set(module, "config", config); Set(module, "pipeline", new TrackingPipeline(config));
        Set(module, "coordinator", new SourceCoordinator(new() { DisableEyeTracking = true, DisableFaceTracking = true }, _ => { }));
        Set(module, "stopped", false); Set(module, "wasActive", true); Set(module, "timerPolicyAttempted", true);
        module.ModuleInformation.Active = fail; module.Status = VRCFaceTracking.Core.Library.ModuleState.Active;
        Call(module, "StartTongue"); var receiver = Get<TongueReceiver>(module, "tongue")!;
        SendMouth(receiver, MouthPacket()); Sentinel(); Call(module, "Write", Output(.17f));
        try {
            module.Update(); Check(!receiver.Snapshot().Override);
            Near(Weight(UnifiedExpressions.CheekSuckLeft), 0); Near(Weight(UnifiedExpressions.TongueUp), 0);
            Check(Get<TjrUdpReceiver>(module, "tongueTransport") is null);
            Call(module, "StartTongue"); var next = Get<TongueReceiver>(module, "tongue")!;
            Check(!ReferenceEquals(receiver, next) && !next.Snapshot().Override);
            // An explicit listener restart permits an intentional return to TJR1/TJR2.
            Check(Send(next, Packet()).Accepted); Call(module, "Write", Output(.17f));
            Near(Weight(UnifiedExpressions.TongueOut), .8f); Near(Weight(UnifiedExpressions.CheekSuckLeft), 0);
        } finally { module.Teardown(); }
    }
});
Test("packet_exact_little_endian_layout", () => {
    var packet = Packet(); var bytes = packet.Encode();
    Check(bytes.Length == 64 && bytes.AsSpan(0, 4).SequenceEqual("TJR1"u8));
    Check(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)) == 1);
    Check(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6)) == 64);
    Check(BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(8)) == packet.SessionId);
    Check(BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(16)) == packet.SourceSeq);
    Check(BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(24)) == packet.MouthSensorNs);
    Check(BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(32)) == packet.EmittedPcNs);
    Near(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(40)), .6f);
    Check(TjrPacket.TryParse(bytes, out var decoded, out _) && decoded == packet);
});
Test("tjr2_exact_layout_and_roundtrip", () => {
    var packet = Packet() with { Extended = true, Up = .7f, Down = .2f, Roll = .4f };
    var bytes = packet.Encode();
    Check(bytes.Length == 80 && bytes.AsSpan(0, 4).SequenceEqual("TJR2"u8));
    Check(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4)) == 2);
    Check(BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6)) == 80);
    Near(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(64)), .7f);
    Near(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(68)), .2f);
    Near(BinaryPrimitives.ReadSingleLittleEndian(bytes.AsSpan(72)), .4f);
    Check(TjrPacket.TryParse(bytes, out var decoded, out _) && decoded == packet);
});
Test("tjr2_rejects_mixed_headers_reserved_and_nonfinite_extensions", () => {
    using var r = new TongueReceiver();
    var packet = Packet() with { Extended = true };
    foreach (var p in new[] { packet with { Up = float.NaN }, packet with { Down = -1 }, packet with { Roll = 1.1f } })
        Check(Send(r, p).Reason == "reject-numeric");
    var bytes = packet.Encode(); bytes[76] = 1;
    Check(r.Receive(bytes).Reason == "reject-reserved");
    bytes = packet.Encode(); bytes[3] = (byte)'1';
    Check(r.Receive(bytes).Reason == "reject-header");
    bytes = packet.Encode(); bytes[6] = 64;
    Check(r.Receive(bytes).Reason == "reject-header");
    Check(!r.Receive(packet.Encode()[..76]).Accepted);
    Check(!r.Snapshot().Extended);
});
Test("tjr2_expiry_clears_every_extended_channel_and_restores_current_native_out", () => {
    double now = 1000; using var r = new TongueReceiver(() => now); Sentinel();
    Send(r, Packet() with { Extended = true, Up = .7f, Down = .2f, Roll = .4f });
    TongueSdkAdapter.Apply(r, UnifiedTracking.Data, true, .17f);
    Near(Weight(UnifiedExpressions.TongueUp), .7f); Near(Weight(UnifiedExpressions.TongueRoll), .4f);
    Near(Weight(UnifiedExpressions.JawOpen), .731f);
    now += 351;
    TongueSdkAdapter.Apply(r, UnifiedTracking.Data, true, .42f);
    Near(Weight(UnifiedExpressions.TongueOut), .42f);
    foreach (var c in new[] { UnifiedExpressions.TongueUp, UnifiedExpressions.TongueDown, UnifiedExpressions.TongueRoll }) Near(Weight(c), 0);
});
Test("tjr2_absent_negative_and_tjr1_switch_do_not_latch_extended_values", () => {
    foreach (var next in new[] { Packet(11, TongueState.Absent) with { Extended = true }, Packet(11),
        Packet(11) with { Extended = true, Out = 0, Up = 1, Down = 1, Roll = 1 } }) {
        using var r = new TongueReceiver(); Sentinel();
        Send(r, Packet() with { Extended = true, Up = 1, Down = 1, Roll = 1 });
        TongueSdkAdapter.Apply(r, UnifiedTracking.Data, true, .17f);
        Send(r, next); TongueSdkAdapter.Apply(r, UnifiedTracking.Data, true, .17f);
        foreach (var c in new[] { UnifiedExpressions.TongueUp, UnifiedExpressions.TongueDown, UnifiedExpressions.TongueRoll }) Near(Weight(c), 0);
    }
});
Test("tjr2_duplicate_old_session_uncertainty_and_stop_share_tjr1_freshness_rules", () => {
    double now = 1000; using var r = new TongueReceiver(() => now);
    var packet = Packet() with { Extended = true, Up = .7f, Roll = .4f }; Send(r, packet);
    now += 20; Send(r, packet with { SourceSeq = 11, MouthSensorNs = packet.MouthSensorNs + 11_111_111, State = TongueState.Uncertain });
    Near(r.Snapshot().Up, .7f);
    now += 101; Check(!r.Snapshot().Override);
    Check(!Send(r, packet).Accepted);
    Send(r, Packet(1, session: 200) with { Extended = true });
    Check(!Send(r, packet with { SourceSeq = 99 }).Accepted);
    Check(Send(r, Packet(1, TongueState.Stop, 200) with { Extended = true }).Accepted);
    Check(!r.Snapshot().Override && r.Snapshot().Roll == 0);
});
Test("tjr2_module_teardown_clears_owned_extensions_after_receiver_disposal", () => {
    var module = Module(); Sentinel(); using var r = new TongueReceiver(); Set(module, "tongue", r);
    Set(module, "pipeline", new TrackingPipeline(new ModuleConfig()));
    Send(r, Packet() with { Extended = true, Up = 1, Down = 1, Roll = 1 });
    Call(module, "Write", Output(.2f)); Near(Weight(UnifiedExpressions.TongueRoll), 1);
    module.Teardown();
    foreach (var c in new[] { UnifiedExpressions.TongueUp, UnifiedExpressions.TongueDown, UnifiedExpressions.TongueRoll }) Near(Weight(c), 0);
});
Test("tjr2_cannot_write_any_foreign_face_shape", () => {
    Sentinel(); using var r = new TongueReceiver();
    Send(r, Packet() with { Extended = true, Up = 1, Down = 1, Roll = 1 });
    TongueSdkAdapter.Apply(r, UnifiedTracking.Data, false, .17f);
    Check(UnifiedTracking.Data.Shapes.All(s => s.Weight == .731f));
});
Test("tjr2_obeys_legacy_ceiling_on_all_extensions", () => {
    Sentinel(); using var r = new TongueReceiver();
    Send(r, Packet() with { Extended = true, Up = 1, Down = 1, Roll = 1 });
    TongueSdkAdapter.Apply(r, UnifiedTracking.Data, true, .17f, true);
    foreach (var c in new[] { UnifiedExpressions.TongueUp, UnifiedExpressions.TongueDown, UnifiedExpressions.TongueRoll }) Near(Weight(c), .99f);
});
Test("10present_11absent_old10_rejected", () => {
    using var r = new TongueReceiver(); Send(r, Packet());
    Check(Send(r, Packet(11, TongueState.Absent)).View.Override);
    Check(!Send(r, Packet()).Accepted); Near(r.Snapshot().Out, 0);
});
Test("duplicate_600ms_does_not_renew", () => {
    double now = 1000; using var r = new TongueReceiver(() => now); Send(r, Packet());
    for (int i = 1; i <= 60; i++) { now = 1000 + i * 10; Check(!Send(r, Packet()).Accepted); }
    Check(!r.Snapshot().Override);
});
Test("new_epoch_seq1_retires_old_epoch", () => {
    using var r = new TongueReceiver(); Send(r, Packet()); Check(Send(r, Packet(1, session: 200)).Accepted);
    Check(!Send(r, Packet(20)).Accepted && r.Snapshot().SessionId == 200);
});
Test("same_seq_stop_releases_and_closes_epoch", () => {
    using var r = new TongueReceiver(); Send(r, Packet());
    var end = Send(r, Packet(state: TongueState.Stop)); Check(end.Accepted && !end.NewSource && !end.View.Override);
    Check(!Send(r, Packet(11)).Accepted); Check(Send(r, Packet(1, session: 200)).Accepted);
});
Test("same_seq_invalid_releases_without_closing_epoch", () => {
    using var r = new TongueReceiver(); Send(r, Packet());
    var end = Send(r, Packet(state: TongueState.Invalid)); Check(end.Accepted && !end.NewSource && !end.View.Override);
    Check(!Send(r, Packet()).Accepted); Check(Send(r, Packet(11)).View.Override);
});
Test("old_stop_cannot_clear_new_result", () => {
    using var r = new TongueReceiver(); Send(r, Packet(11));
    Check(!Send(r, Packet(state: TongueState.Stop)).Accepted); Check(r.Snapshot().Override);
});
Test("numeric_and_strict_wire_rejections", () => {
    using var r = new TongueReceiver();
    foreach (var p in new[] { Packet() with { Left = float.NaN }, Packet() with { Right = float.PositiveInfinity },
        Packet() with { Out = -1 }, Packet() with { Out = 1.01f }, Packet(age: float.NaN), Packet(age: -1) })
        Check(Send(r, p).Reason == "reject-numeric");
    var bytes = Packet().Encode();
    for (int i = 0; i < 64; i++) Check(r.Receive(bytes.AsSpan(0, i)).Reason == "reject-length");
    Check(!r.Receive(new byte[65]).Accepted);
    foreach (var offset in new[] { 0, 4, 6, 60 }) {
        var bad = (byte[])bytes.Clone(); bad[offset] = 99; Check(!r.Receive(bad).Accepted);
    }
    var flags = (byte[])bytes.Clone(); BinaryPrimitives.WriteUInt32LittleEndian(flags.AsSpan(56), 3);
    Check(r.Receive(flags).Reason == "reject-state"); Check(Send(r, Packet(session: 0)).Reason == "reject-session-zero");
    Check(!r.Snapshot().Override);
});
Test("source_age_reduces_deadline_and_stale_new_seq_tombstones", () => {
    double now = 1000; using var r = new TongueReceiver(() => now);
    Near((float)Send(r, Packet(age: 300)).View.RemainingMs, 50);
    now = 1049; Check(r.Snapshot().Override); now = 1050; Check(!r.Snapshot().Override);
    Check(Send(r, Packet(11, age: 351)).Reason == "stale-source"); Check(!Send(r, Packet()).Accepted);
});
Test("backlog_seq_and_repeated_sensor_do_not_refresh", () => {
    double now = 1000; using var r = new TongueReceiver(() => now); Send(r, Packet());
    for (ulong seq = 11; seq <= 16; seq++) { now += 100; Send(r, Packet(seq)); }
    Check(!r.Snapshot().Override);
    using var other = new TongueReceiver(); var original = Packet(); Send(other, original);
    Check(Send(other, original with { SourceSeq = 11 }).Reason == "old-sensor"); Check(!other.Snapshot().Override);
});
Test("uncertainty_lease_never_slides_or_resurrects", () => {
    double now = 1000; using var r = new TongueReceiver(() => now); Send(r, Packet());
    now = 1020; Send(r, Packet(11, TongueState.Uncertain)); now = 1100; Check(Send(r, Packet(12, TongueState.Uncertain)).View.Override);
    now = 1120; Check(!r.Snapshot().Override); now = 1130; Check(!Send(r, Packet(13, TongueState.Uncertain)).View.Override);
    now = 1140; Send(r, Packet(14, TongueState.Absent)); now = 1150; Check(!Send(r, Packet(15, TongueState.Uncertain)).View.Override);
});
Test("adapter_uses_current_native_out_only_when_released", () => {
    Sentinel(); using var r = new TongueReceiver(); Send(r, Packet());
    TongueSdkAdapter.Apply(r, UnifiedTracking.Data, true, .17f); Near(Weight(UnifiedExpressions.TongueOut), .8f);
    Send(r, Packet(11, TongueState.Absent)); TongueSdkAdapter.Apply(r, UnifiedTracking.Data, true, .42f);
    Near(Weight(UnifiedExpressions.TongueOut), 0); r.Stop(); TongueSdkAdapter.Apply(r, UnifiedTracking.Data, true, .42f);
    Near(Weight(UnifiedExpressions.TongueOut), .42f); Near(Weight(UnifiedExpressions.TongueLeft), 0);
    Near(Weight(UnifiedExpressions.TongueUp), .731f); Near(Weight(UnifiedExpressions.TongueDown), .731f);
});
Test("no_face_ownership_preserves_every_foreign_shape", () => {
    Sentinel(); using var r = new TongueReceiver(); Send(r, Packet());
    TongueSdkAdapter.Apply(r, UnifiedTracking.Data, false, .17f);
    Check(UnifiedTracking.Data.Shapes.All(s => s.Weight == .731f));
    var module = Module(false); Set(module, "ownsEye", true); Set(module, "tongue", r);
    Call(module, "Write", Output(.2f));
    var eyes = Enum.GetValues<ExpressionChannel>().Where(ExpressionFrame.IsEye)
        .Select(c => (int)Enum.Parse<UnifiedExpressions>(c.ToString())).ToHashSet();
    for (int i = 0; i < UnifiedTracking.Data.Shapes.Length; i++) if (!eyes.Contains(i)) Near(UnifiedTracking.Data.Shapes[i].Weight, .731f);
    module.Teardown(); Near(Weight(UnifiedExpressions.TongueOut), .731f); Near(Weight(UnifiedExpressions.TongueLeft), .731f);
});
Test("module_write_releases_to_latest_pipeline_without_touching_updown", () => {
    Sentinel(); var module = Module(); using var receiver = new TongueReceiver(); Set(module, "tongue", receiver);
    Send(receiver, Packet()); Call(module, "Write", Output(.2f)); Near(Weight(UnifiedExpressions.TongueOut), .8f);
    Send(receiver, Packet(state: TongueState.Stop)); Call(module, "Write", Output(.43f));
    Near(Weight(UnifiedExpressions.TongueOut), .43f); Near(Weight(UnifiedExpressions.TongueLeft), 0);
    Near(Weight(UnifiedExpressions.JawOpen), .23f); Near(Weight(UnifiedExpressions.TongueUp), .731f); module.Teardown();
});
Test("no_packets_or_disabled_receiver_keeps_native_and_clears_own_directions", () => {
    Sentinel(); var module = Module(); Call(module, "Write", Output(.32f));
    Near(Weight(UnifiedExpressions.TongueOut), .32f); Near(Weight(UnifiedExpressions.TongueLeft), 0);
    Near(Weight(UnifiedExpressions.TongueRight), 0); Near(Weight(UnifiedExpressions.TongueUp), .731f); module.Teardown();
});
Test("pc_tongue_respects_existing_legacy_expression_ceiling", () => {
    Sentinel(); var module = Module(); var config = new ModuleConfig { LegacyExpressionCeiling = true };
    Set(module, "config", config); using var receiver = new TongueReceiver(); Set(module, "tongue", receiver);
    Send(receiver, Packet() with { Out = 1, Left = 1, Right = 1 }); Call(module, "Write", Output(.2f));
    Near(Weight(UnifiedExpressions.TongueOut), .99f); Near(Weight(UnifiedExpressions.TongueLeft), .99f);
    Near(Weight(UnifiedExpressions.TongueRight), .99f); module.Teardown();
});
Test("eye_only_and_disabled_never_bind_tongue_socket", () => {
    foreach (var state in new[] { (false, true), (true, false) }) {
        var module = Module(state.Item1); var config = new ModuleConfig(); config.Tongue.Enabled = state.Item2;
        Set(module, "config", config); Call(module, "StartTongue");
        Check(Get<TjrUdpReceiver>(module, "tongueTransport") is null); module.Teardown();
    }
});
Test("bind_conflict_does_not_break_native_tracking", () => {
    using var occupied = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    var config = new ModuleConfig(); config.Tongue.Port = ((IPEndPoint)occupied.Client.LocalEndPoint!).Port;
    var module = Module(); Set(module, "config", config); Call(module, "StartTongue");
    Check(Get<TjrUdpReceiver>(module, "tongueTransport") is null);
    Sentinel(); Call(module, "Write", Output(.4f)); Near(Weight(UnifiedExpressions.TongueOut), .4f); module.Teardown();
});
Test("config_roundtrip_port_validation_and_obsolete_mouth_keys_ignored", () => {
    string path = Path.Combine(Path.GetTempPath(), "pico-tongue-config-" + Guid.NewGuid() + ".json");
    try {
        ConfigStore.SaveNew(path, new ModuleConfig());
        string cleanJson = File.ReadAllText(path);
        Check(!cleanJson.Contains("MouthMode", StringComparison.Ordinal) && !cleanJson.Contains("HybridChannels", StringComparison.Ordinal));
        File.WriteAllText(path, "{\"ConfigVersion\":1,\"Tongue\":{\"Enabled\":false,\"Port\":45692}}");
        var read = ConfigStore.ReadOnly(path, _ => { }); Check(!read.Tongue.Enabled && read.Tongue.Port == 45692);
        File.WriteAllText(path, "{\"ConfigVersion\":1}"); read = ConfigStore.ReadOnly(path, _ => { });
        Check(read.Tongue.Enabled && read.Tongue.Port == 45682);
        var logs = new List<string>();
        File.WriteAllText(path, "{\"ConfigVersion\":1,\"Tongue\":{\"MouthMode\":\"ModelOnly\",\"HybridChannels\":[\"JawOpen\"]}}");
        read = ConfigStore.ReadOnly(path, logs.Add);
        Check(read.Tongue.Enabled && read.Tongue.Port == 45682 &&
            logs.Any(m => m.Contains("Tongue.MouthMode", StringComparison.Ordinal)) &&
            logs.Any(m => m.Contains("Tongue.HybridChannels", StringComparison.Ordinal)));
        read.Tongue.Port = 0; Check(ConfigValidator.Validate(read, _ => { }).Tongue.Port == 45682);
    } finally { File.Delete(path); }
});
Test("pause_and_update_error_stop_tongue_worker_and_clear_overlay", () => {
    foreach (bool fail in new[] { false, true }) {
        var module = Module(); var config = new ModuleConfig(); config.Tongue.Port = 0;
        Set(module, "config", config); Set(module, "pipeline", new TrackingPipeline(config));
        Set(module, "coordinator", new SourceCoordinator(new() { DisableEyeTracking = true, DisableFaceTracking = true }, _ => { }));
        Set(module, "stopped", false); Set(module, "wasActive", true); Set(module, "timerPolicyAttempted", true);
        module.ModuleInformation.Active = fail; module.Status = VRCFaceTracking.Core.Library.ModuleState.Active;
        Call(module, "StartTongue"); var udp = Get<TjrUdpReceiver>(module, "tongueTransport")!;
        int port = udp.Port; var receiver = Get<TongueReceiver>(module, "tongue")!;
        Send(receiver, Packet()); Sentinel(); Call(module, "Write", Output(.2f));
        try {
            // On fail=true, the missing diagnostic reporter triggers the normal Update catch path.
            module.Update(); Check(!receiver.Snapshot().Override); Near(Weight(UnifiedExpressions.TongueLeft), 0);
            Near(Weight(UnifiedExpressions.TongueOut), 0); Check(Get<TjrUdpReceiver>(module, "tongueTransport") is null);
            using var rebound = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
        } finally { module.Teardown(); }
    }
});
tests.Add(("real_udp_module_write_old_negative_stop_and_socket_release", async () => {
    var module = Module(); var config = new ModuleConfig(); config.Tongue.Port = 0; Set(module, "config", config);
    Call(module, "StartTongue"); var transport = Get<TjrUdpReceiver>(module, "tongueTransport")!;
    var receiver = Get<TongueReceiver>(module, "tongue")!; int port = transport.Port;
    try {
        using var sender = new UdpClient(AddressFamily.InterNetwork); var endpoint = new IPEndPoint(IPAddress.Loopback, port);
        await sender.SendAsync(Packet().Encode(), endpoint); await Wait(() => receiver.Snapshot().Override);
        Sentinel(); Near(Weight(UnifiedExpressions.TongueOut), .731f); // UDP never writes the SDK.
        Call(module, "Write", Output(.17f)); Near(Weight(UnifiedExpressions.TongueOut), .8f);
        await sender.SendAsync(Packet(11, TongueState.Absent).Encode(), endpoint); await Wait(() => receiver.Snapshot().Status == "absent");
        await sender.SendAsync(Packet().Encode(), endpoint); Call(module, "Write", Output(.17f)); Near(Weight(UnifiedExpressions.TongueOut), 0);
        await sender.SendAsync(Packet(1, session: 200).Encode(), endpoint); await Wait(() => receiver.Snapshot().SessionId == 200);
        await sender.SendAsync(Packet(1, TongueState.Stop, 200).Encode(), endpoint); await Wait(() => !receiver.Snapshot().Override);
        Call(module, "Write", Output(.42f)); Near(Weight(UnifiedExpressions.TongueOut), .42f);
    } finally { module.Teardown(); }
    using var rebound = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
}));
tests.Add(("real_udp_duplicate_expiry_and_truncation", async () => {
    using var receiver = new TongueReceiver(); var results = new ConcurrentQueue<ReceiveResult>();
    using var sender = new UdpClient(AddressFamily.InterNetwork);
    await using var udp = new TjrUdpReceiver(receiver, 0, results.Enqueue);
    var endpoint = new IPEndPoint(IPAddress.Loopback, udp.Port); var packet = Packet().Encode();
    await sender.SendAsync(packet, endpoint); await Wait(() => receiver.Snapshot().Override);
    var elapsed = Stopwatch.StartNew(); int sent = 1;
    while (elapsed.ElapsedMilliseconds < 600) { await sender.SendAsync(packet, endpoint); sent++; await Task.Delay(10); }
    await Wait(() => results.Count == sent); Check(results.Count(r => r.Accepted) == 1 && !receiver.Snapshot().Override);
    await sender.SendAsync(packet[..25], endpoint); await Wait(() => results.Count > sent);
    Check(results.Last().Reason == "reject-length");
}));
tests.Add(("module_stop_joins_worker_releases_socket_and_restarts", async () => {
    var module = Module(); var config = new ModuleConfig(); config.Tongue.Port = 0; Set(module, "config", config);
    Set(module, "pipeline", new TrackingPipeline(config)); Call(module, "StartTongue");
    var transport = Get<TjrUdpReceiver>(module, "tongueTransport")!; int port = transport.Port;
    var receiver = Get<TongueReceiver>(module, "tongue")!; Send(receiver, Packet()); Sentinel(); Call(module, "Write", Output(.2f));
    module.Teardown(); Check(!receiver.Snapshot().Override); Near(Weight(UnifiedExpressions.TongueOut), 0);
    Near(Weight(UnifiedExpressions.TongueLeft), 0); Check(Get<TjrUdpReceiver>(module, "tongueTransport") is null);
    using (var rebound = new UdpClient(new IPEndPoint(IPAddress.Loopback, port))) { }
    Set(module, "ownsFace", true); Call(module, "StartTongue");
    try {
        var next = Get<TongueReceiver>(module, "tongue")!; Check(!ReferenceEquals(next, receiver));
        using var sender = new UdpClient(AddressFamily.InterNetwork);
        await sender.SendAsync(Packet().Encode(), new IPEndPoint(IPAddress.Loopback, Get<TjrUdpReceiver>(module, "tongueTransport")!.Port));
        await Wait(() => next.Snapshot().Override);
    } finally { module.Teardown(); }
}));
tests.Add(("real_udp_bridge_yields_face_subscription_during_mfr_without_dropping_eyes", async () => {
    using var fakeBridge = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    int fakePort = ((IPEndPoint)fakeBridge.Client.LocalEndPoint!).Port;
    var settings = new BridgeSettings { IP = "127.0.0.1", ListenAddress = "127.0.0.1", ReconnectMs = 100 };
    using var source = new BridgeUdpSource(settings, _ => { }, eyeEnabled: true, faceEnabled: true, listenPort: 0, peerPort: fakePort);
    using var cts = new CancellationTokenSource();
    byte[] EyePacket(ulong epoch, int mask, ulong ts, float pupilMm) {
        var b = new byte[83];
        b[0] = (byte)'E'; b[1] = (byte)mask; b[2] = 0;
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(3), epoch);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(11), ts);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(19), 0x906u);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(23), 0x906u);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(27), 0x906u);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(67), 0.9f);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(71), 0.9f);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(75), pupilMm * 10f);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(79), pupilMm * 10f);
        return b;
    }
    byte[] XV2Packet(ulong epoch, int mask, ulong leftTs, ulong rightTs, ulong mouthTs, float eyeUp, float jawOpen) {
        int[] shapes = mask == 1 ? BridgeSplitDecoder.EyeAuxiliaryShapes.ToArray() : Enumerable.Range(0, 52).ToArray();
        var b = new byte[19 + shapes.Length * 4 + 36];
        b[0] = (byte)'X'; b[1] = (byte)mask; b[2] = (byte)mask;
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(3), epoch);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(11), leftTs);
        for (int i = 0; i < shapes.Length; i++) {
            float v = shapes[i] == (int)PicoShape.EyeLookUpL ? eyeUp : shapes[i] == (int)PicoShape.JawOpen ? jawOpen : 0f;
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(19 + i * 4), v);
        }
        int ext = 19 + shapes.Length * 4;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(ext), 2);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(ext + 4), 0.25f);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(ext + 8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(ext + 12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(ext + 16), mask == 1 ? 3u : 7u);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(ext + 20), mask == 1 ? 0L : (long)mouthTs);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(ext + 28), (long)rightTs);
        return b;
    }
    async Task<(IPEndPoint Remote, string Id, int Mask)> ExpectDiscoverAndSub(int expectedMask, ulong epoch) {
        string? subId = null; IPEndPoint remote = null!;
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 2000) {
            var res = await fakeBridge.ReceiveAsync(cts.Token).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            remote = res.RemoteEndPoint;
            var msg = System.Text.Encoding.ASCII.GetString(res.Buffer);
            if (msg == "DISCOVER_DAEMON") {
                var modeBytes = System.Text.Encoding.ASCII.GetBytes("PXR_MODE mode=enhance rooted=1 enhance=1 gate=1 plugin=dual");
                await fakeBridge.SendAsync(modeBytes, remote);
            } else if (msg.StartsWith("PXR_SUB ", StringComparison.Ordinal)) {
                int mask = msg[^1] - '0';
                Check(mask == expectedMask, $"Expected PXR_SUB mask={expectedMask}, got '{msg}'");
                subId = msg.Substring(11, 16);
                var ack = System.Text.Encoding.ASCII.GetBytes($"PXR_SUB_ACK id={subId} mask={mask} epoch={epoch:x16}");
                await fakeBridge.SendAsync(ack, remote);
                return (remote, subId, mask);
            }
        }
        throw new TimeoutException($"Timed out waiting for PXR_SUB mask={expectedMask}");
    }

    source.Start(cts.Token);
    var (pcEp, _, _) = await ExpectDiscoverAndSub(3, 1);
    await Wait(() => source.Snapshot.Protocol == InputProtocol.BridgeSplit);
    await fakeBridge.SendAsync(XV2Packet(1, 3, 100, 100, 100, 0.5f, 0.6f), pcEp);
    await Wait(() => source.Snapshot.Mouth is not null);
    await fakeBridge.SendAsync(EyePacket(1, 3, 100, 4.2f), pcEp);
    await Wait(() => source.Snapshot.Eye is not null && source.Snapshot.Mouth is not null);
    long initialGeneration = source.Snapshot.Generation;
    Near(source.Snapshot.Eye!.Left.PupilMillimeters, 4.2f);
    Near(source.Snapshot.Mouth![PicoShape.JawOpen], 0.6f);

    source.SetExternalMouthActive(true);
    Check(source.Snapshot.Mouth is null && source.Snapshot.Eye is not null, "External mouth demand immediately clears cached mouth while preserving eye");
    await ExpectDiscoverAndSub(1, 2);
    await Wait(() => source.Snapshot.Detail.Contains("preserving eyes", StringComparison.Ordinal));
    Check(source.Snapshot.Generation == initialGeneration && source.Snapshot.Eye is not null, "Yielding face subscription must preserve eye generation and pupil");
    await fakeBridge.SendAsync(EyePacket(2, 1, 200, 4.4f), pcEp);
    await fakeBridge.SendAsync(XV2Packet(2, 1, 200, 200, 0, 0.7f, 0.9f), pcEp);
    await Wait(() => Math.Abs(source.Snapshot.Eye!.Left.PupilMillimeters - 4.4f) < 0.01f &&
        source.Snapshot.Face is { } f && Math.Abs(f[PicoShape.EyeLookUpL] - 0.7f) < 0.01f);
    Check(source.Snapshot.Mouth is null, "Mask=1 eye stream carries no mouth data");

    source.SetExternalMouthActive(false);
    await ExpectDiscoverAndSub(3, 3);
    await Wait(() => source.Snapshot.Detail.Contains("effective=3", StringComparison.Ordinal));
    Check(source.Snapshot.Generation == initialGeneration && source.Snapshot.Eye is not null, "Resuming face subscription must preserve eye generation");
    await fakeBridge.SendAsync(XV2Packet(3, 3, 300, 300, 300, 0.5f, 0.55f), pcEp);
    await Wait(() => source.Snapshot.Mouth is not null && Math.Abs(source.Snapshot.Mouth[PicoShape.JawOpen] - 0.55f) < 0.01f);
    cts.Cancel();
}));
tests.Add(("real_udp_partial_six_tongue_mfr_keeps_bridge_face_and_jaw_open_and_full_mfr_yields", async () => {
    using var fakeBridge = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
    int fakePort = ((IPEndPoint)fakeBridge.Client.LocalEndPoint!).Port;
    var config = new ModuleConfig { Source = SourceSelection.Bridge };
    config.Bridge.IP = "127.0.0.1";
    config.Bridge.ListenAddress = "127.0.0.1";
    config.Bridge.ReconnectMs = 100;
    var coordinator = new SourceCoordinator(new() { DisableEyeTracking = true, DisableFaceTracking = true }, _ => { });
    Set(coordinator, "config", config);
    var bridgeSource = new BridgeUdpSource(config.Bridge, _ => { }, eyeEnabled: true, faceEnabled: true, listenPort: 0, peerPort: fakePort);
    var sourcesList = (List<ITrackingSource>)typeof(SourceCoordinator).GetField("sources", Hidden)!.GetValue(coordinator)!;
    var cts = (CancellationTokenSource)typeof(SourceCoordinator).GetField("cancellation", Hidden)!.GetValue(coordinator)!;
    sourcesList.Add(bridgeSource);
    bridgeSource.Start(cts.Token);

    var module = Module(ownsFace: true);
    Set(module, "ownsEye", true);
    Set(module, "config", config);
    Set(module, "pipeline", new TrackingPipeline(config));
    Set(module, "diagnostics", new DiagnosticReporter(_ => { }));
    Set(module, "coordinator", coordinator);
    Set(module, "stopped", false);
    Set(module, "timerPolicyAttempted", true);
    module.ModuleInformation.Active = true;
    module.Status = VRCFaceTracking.Core.Library.ModuleState.Active;
    using var receiver = new TongueReceiver();
    Set(module, "tongue", receiver);
    Sentinel();

    var subMasks = new ConcurrentQueue<int>();
    IPEndPoint? pcEp = null;
    ulong bridgeEpoch = 0;
    using var bgCts = new CancellationTokenSource();
    var bridgeLoop = Task.Run(async () => {
        while (!bgCts.IsCancellationRequested) {
            UdpReceiveResult res;
            try { res = await fakeBridge.ReceiveAsync(bgCts.Token); }
            catch (OperationCanceledException) { break; }
            pcEp = res.RemoteEndPoint;
            var msg = System.Text.Encoding.ASCII.GetString(res.Buffer);
            if (msg == "DISCOVER_DAEMON") {
                var modeBytes = System.Text.Encoding.ASCII.GetBytes("PXR_MODE mode=enhance rooted=1 enhance=1 gate=1 plugin=dual");
                await fakeBridge.SendAsync(modeBytes, pcEp);
            } else if (msg.StartsWith("PXR_SUB ", StringComparison.Ordinal)) {
                int mask = msg[^1] - '0';
                string subId = msg.Substring(11, 16);
                ulong nextEpoch = ++bridgeEpoch;
                subMasks.Enqueue(mask);
                var ack = System.Text.Encoding.ASCII.GetBytes($"PXR_SUB_ACK id={subId} mask={mask} epoch={nextEpoch:x16}");
                await fakeBridge.SendAsync(ack, pcEp);
            }
        }
    });

    byte[] XV2(ulong epoch, int mask, ulong ts, float jawOpen, float tongueOut) {
        int[] shapes = mask == 1 ? BridgeSplitDecoder.EyeAuxiliaryShapes.ToArray() : Enumerable.Range(0, 52).ToArray();
        var b = new byte[19 + shapes.Length * 4 + 36];
        b[0] = (byte)'X'; b[1] = (byte)mask; b[2] = (byte)mask;
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(3), epoch);
        BinaryPrimitives.WriteUInt64LittleEndian(b.AsSpan(11), ts);
        for (int i = 0; i < shapes.Length; i++) {
            float v = shapes[i] == (int)PicoShape.JawOpen ? jawOpen
                : shapes[i] == (int)PicoShape.TongueOut ? tongueOut
                : shapes[i] == (int)PicoShape.EyeLookUpL ? 0.5f : 0f;
            BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(19 + i * 4), v);
        }
        int ext = 19 + shapes.Length * 4;
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(ext), 2);
        BinaryPrimitives.WriteSingleLittleEndian(b.AsSpan(ext + 4), 0.25f);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(ext + 8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(ext + 12), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(ext + 16), mask == 1 ? 3u : 7u);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(ext + 20), mask == 1 ? 0L : (long)ts);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(ext + 28), (long)ts);
        return b;
    }

    try {
        await Wait(() => bridgeEpoch == 1 && pcEp is not null);
        Check(subMasks.TryDequeue(out int initialMask) && initialMask == 3);
        await fakeBridge.SendAsync(XV2(1, 3, 100, 0.60f, 0.15f), pcEp!);
        await Wait(() => bridgeSource.Snapshot.Mouth is not null);
        module.Update();
        Near(Weight(UnifiedExpressions.JawOpen), 0.60f);
        Near(Weight(UnifiedExpressions.TongueOut), 0.15f);

        // Exact tester repro: 6-tongue-only MFR1 (0x81F00000000) must preserve JawOpen=0.60 and keep Bridge mask=3.
        const ulong sixTongueMask = 0x81F00000000UL;
        var tVals = new float[50];
        tVals[(int)MouthChannel.TongueOut] = 0.80f;
        tVals[(int)MouthChannel.TongueLeft] = 0.35f;
        tVals[(int)MouthChannel.TongueRight] = 0.10f;
        tVals[(int)MouthChannel.TongueUp] = 0.45f;
        tVals[(int)MouthChannel.TongueDown] = 0.20f;
        tVals[(int)MouthChannel.TongueRoll] = 0.55f;
        SendMouth(receiver, MouthPacket(seq: 1, mask: sixTongueMask) with { Values = tVals });
        module.Update();
        Near(Weight(UnifiedExpressions.JawOpen), 0.60f);
        Near(Weight(UnifiedExpressions.TongueOut), 0.80f);
        Near(Weight(UnifiedExpressions.TongueLeft), 0.35f);
        Near(Weight(UnifiedExpressions.TongueRight), 0.10f);
        Near(Weight(UnifiedExpressions.TongueUp), 0.45f);
        Near(Weight(UnifiedExpressions.TongueDown), 0.20f);
        Near(Weight(UnifiedExpressions.TongueRoll), 0.55f);
        await Task.Delay(50);
        Check(subMasks.IsEmpty && bridgeEpoch == 1 && bridgeSource.Snapshot.Mouth is not null,
            "Partial 6-tongue MFR1 must keep Bridge face subscription (mask=3) active");

        // Explicit off on JawOpen (bit=1, val=0) zeros JawOpen while keeping Bridge mask=3.
        SendMouth(receiver, MouthPacket(seq: 2, mask: sixTongueMask | Mask(MouthChannel.JawOpen)) with { Values = tVals });
        module.Update();
        Near(Weight(UnifiedExpressions.JawOpen), 0f);
        Near(Weight(UnifiedExpressions.TongueRoll), 0.55f);
        Check(bridgeEpoch == 1, "Adding one off bit still does not cover all 33 native channels");

        // Full native coverage (all 50 channels) yields Bridge face subscription to mask=1.
        var fullVals = new float[50];
        fullVals[(int)MouthChannel.JawOpen] = 0.91f;
        fullVals[(int)MouthChannel.TongueRoll] = 0.66f;
        SendMouth(receiver, MouthPacket(seq: 3, mask: MfrPacket.AllValid) with { Values = fullVals });
        module.Update();
        await Wait(() => bridgeEpoch == 2);
        Check(subMasks.TryDequeue(out int yieldedMask) && yieldedMask == 1, "Full MFR1 must yield Bridge to mask=1");
        Near(Weight(UnifiedExpressions.JawOpen), 0.91f);
        Near(Weight(UnifiedExpressions.TongueRoll), 0.66f);

        // Dropping back to partial 6-tongue MFR1 (e.g., PSL1 dropout) restores Bridge mask=3 and JawOpen=0.60.
        SendMouth(receiver, MouthPacket(seq: 4, mask: sixTongueMask) with { Values = tVals });
        module.Update();
        await Wait(() => bridgeEpoch == 3);
        Check(subMasks.TryDequeue(out int restoredMask) && restoredMask == 3, "Partial MFR1 after full must restore Bridge mask=3");
        await fakeBridge.SendAsync(XV2(3, 3, 200, 0.60f, 0.15f), pcEp!);
        await Wait(() => bridgeSource.Snapshot.Mouth is not null);
        module.Update();
        Near(Weight(UnifiedExpressions.JawOpen), 0.60f);
        Near(Weight(UnifiedExpressions.TongueOut), 0.80f);
        Near(Weight(UnifiedExpressions.TongueRoll), 0.55f);

        // Release clears extended tongue channels and restores Bridge TongueOut.
        SendMouth(receiver, MouthPacket(seq: 4, mask: 0, state: MouthState.Release));
        module.Update();
        Near(Weight(UnifiedExpressions.JawOpen), 0.60f);
        Near(Weight(UnifiedExpressions.TongueOut), 0.15f);
        Near(Weight(UnifiedExpressions.TongueLeft), 0f);
        Near(Weight(UnifiedExpressions.TongueRoll), 0f);
    } finally {
        bgCts.Cancel();
        await bridgeLoop;
        module.Teardown();
    }
}));

int failed = 0; var reports = new List<object>();
foreach (var test in tests) {
    try { await test.Run(); reports.Add(new { name = test.Name, passed = true }); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { failed++; reports.Add(new { name = test.Name, passed = false, error = error.ToString() }); Console.WriteLine("FAIL " + test.Name + ": " + error); }
}
if (args.Length > 0) File.WriteAllText(args[0], JsonSerializer.Serialize(new {
    sdk = typeof(UnifiedTrackingData).Assembly.GetName().Version!.ToString(),
    moduleSdkReference = typeof(UnifiedPicoTrackingModule).Assembly.GetReferencedAssemblies().Single(a => a.Name == "VRCFaceTracking.Core").Version!.ToString(),
    total = tests.Count, passed = tests.Count - failed, failed, tests = reports }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"RESULT {tests.Count - failed}/{tests.Count} passed");
return failed == 0 ? 0 : 1;
