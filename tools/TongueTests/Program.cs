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
async Task Wait(Func<bool> predicate) {
    var elapsed = Stopwatch.StartNew();
    while (!predicate()) { if (elapsed.ElapsedMilliseconds > 2000) throw new TimeoutException("loopback test"); await Task.Delay(2); }
}

// A separate synthetic loopback process for checking another producer's actual wire encoder.
if (args.FirstOrDefault() == "--probe") {
    var module = Module(!args.Contains("--no-face")); Sentinel();
    Set(module, "pipeline", new TrackingPipeline(new ModuleConfig()));
    using var receiver = new TongueReceiver(); Set(module, "tongue", receiver);
    var packets = new ConcurrentQueue<ReceiveResult>();
    int port;
    await using (var udp = new TjrUdpReceiver(receiver, 0, packets.Enqueue)) {
        port = udp.Port; Console.WriteLine(JsonSerializer.Serialize(new { ready = true, port }));
        var elapsed = Stopwatch.StartNew(); bool finished = false;
        while (!finished && elapsed.Elapsed.TotalSeconds < 10) {
            Call(module, "Write", Output(.17f));
            while (packets.TryDequeue(out var packet)) {
                Call(module, "Write", Output(.17f));
                Console.WriteLine(JsonSerializer.Serialize(new { accepted = packet.Accepted, reason = packet.Reason,
                    state = receiver.Snapshot().Status, @out = Weight(UnifiedExpressions.TongueOut),
                    left = Weight(UnifiedExpressions.TongueLeft), right = Weight(UnifiedExpressions.TongueRight),
                    up = Weight(UnifiedExpressions.TongueUp), down = Weight(UnifiedExpressions.TongueDown) }));
                finished |= packet.Accepted && packet.Packet?.State == TongueState.Stop;
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

Test("actual_sdk_545_and_tongue_fields", () => {
    Check(typeof(UnifiedTrackingData).Assembly.GetName().Version == new Version(5, 4, 5, 0));
    foreach (string name in new[] { "TongueLeft", "TongueRight", "TongueOut", "TongueUp", "TongueDown" })
        Check(Enum.IsDefined(typeof(UnifiedExpressions), name));
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
Test("config_roundtrip_and_port_validation", () => {
    string path = Path.Combine(Path.GetTempPath(), "pico-tongue-config-" + Guid.NewGuid() + ".json");
    try {
        File.WriteAllText(path, "{\"ConfigVersion\":1,\"Tongue\":{\"Enabled\":false,\"Port\":45692}}");
        var read = ConfigStore.ReadOnly(path, _ => { }); Check(!read.Tongue.Enabled && read.Tongue.Port == 45692);
        File.WriteAllText(path, "{\"ConfigVersion\":1}"); read = ConfigStore.ReadOnly(path, _ => { });
        Check(read.Tongue.Enabled && read.Tongue.Port == 45682);
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

int failed = 0; var reports = new List<object>();
foreach (var test in tests) {
    try { await test.Run(); reports.Add(new { name = test.Name, passed = true }); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { failed++; reports.Add(new { name = test.Name, passed = false, error = error.ToString() }); Console.WriteLine("FAIL " + test.Name + ": " + error); }
}
if (args.Length > 0) File.WriteAllText(args[0], JsonSerializer.Serialize(new { total = tests.Count, passed = tests.Count - failed, failed, tests = reports }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"RESULT {tests.Count - failed}/{tests.Count} passed");
return failed == 0 ? 0 : 1;
