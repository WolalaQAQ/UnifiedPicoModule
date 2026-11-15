using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using UnifiedPicoModule.Configuration;
using UnifiedPicoModule.Core;
using UnifiedPicoModule.Runtime;
using UnifiedPicoModule.Sdk;
using VRCFaceTracking;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Sandboxing.IPC;

namespace CompatibilityTests;

internal static partial class Program
{
    private static readonly List<object> Results = [];
    private static int failures;
    private static readonly BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static int Main(string[] args)
    {
        // The runner launches a separate process with private AppData and working directory.
        // Never call the GUI, discovery, or real tracking-source Start methods here.
        var modulePath = typeof(UnifiedPicoTrackingModule).Assembly.Location;
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(modulePath)!, "UnifiedPicoModule.json"),
            JsonSerializer.Serialize(new ModuleConfig { DisableEyeTracking = true, DisableFaceTracking = true }));
        Console.WriteLine($"Module: {modulePath}");
        Console.WriteLine($"Host Core: {typeof(UnifiedTracking).Assembly.FullName}");
        Console.WriteLine($"Module Core reference: {typeof(UnifiedPicoTrackingModule).Assembly.GetReferencedAssemblies().Single(a => a.Name == "VRCFaceTracking.Core")}");
        Run("lifecycle-and-config-disabled-ownership", Lifecycle);
        Run("pipeline-update-and-channel-ownership", Ownership);
        Run("pupil-sdk-unavailable-and-legacy-estimate", PupilProvenance);
        Run("source-selection-and-fallback", Selection);
        Run("grouped-source-identity-and-staleness", Grouped);
        Run("sandbox-ipc-roundtrip-and-mixed-brow-boundary", Ipc);
        InputTests();
        LegacyTests();
#if NATIVE_HOST
        NativeTests();
#endif
        var knownUpstream = new List<string> { "BrowOuterUpLeft belongs to both eye and mouth IPC copy ranges; a non-sentinel mouth value can overwrite it." };
#if NATIVE_HOST
        knownUpstream.Add("Binary sender starts subscribed before capability events, and capability events can resubscribe after Stop.");
#endif
        var report = new { core = typeof(UnifiedTracking).Assembly.GetName().Version!.ToString(),
            coreProduct = System.Diagnostics.FileVersionInfo.GetVersionInfo(typeof(UnifiedTracking).Assembly.Location).ProductVersion,
            module = typeof(UnifiedPicoTrackingModule).Assembly.GetName().Version!.ToString(), failures,
            knownUpstream,
            results = Results };
        File.WriteAllText(args[0], JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        return failures == 0 ? 0 : 1;
    }

    private static void Run(string name, Action test)
    {
        try { test(); Results.Add(new { name, status = "pass" }); Console.WriteLine($"PASS {name}"); }
        catch (Exception e) { failures++; Results.Add(new { name, status = "fail", error = e.ToString() }); Console.WriteLine($"FAIL {name}: {e}"); }
    }
    internal static void Check(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
    internal static void Near(float actual, float expected, string message)
    { Check(Math.Abs(actual - expected) < 0.0001f, $"{message}: expected {expected}, actual {actual}"); }
    private static void Set(object instance, string field, object? value) => instance.GetType().GetField(field, Hidden)!.SetValue(instance, value);
    private static T Get<T>(object instance, string field) => (T)instance.GetType().GetField(field, Hidden)!.GetValue(instance)!;
    internal static UnifiedPicoTrackingModule NewModule() => new() { Logger = NullLogger.Instance, ModuleInformation = new() };

    private static void Lifecycle()
    {
        var module = NewModule();
        Check(module is ExtTrackingModule && module.Supported == (true, true), "Official SDK base type resolution");
        var path = Path.Combine(Path.GetDirectoryName(typeof(UnifiedPicoTrackingModule).Assembly.Location)!, "UnifiedPicoModule.json");
        try
        {
            File.Delete(path);
            Check(module.Initialize(false, false) == (false, false), "No host allocations create no sources");
            Check(File.Exists(path), "Missing configuration creates defaults in the isolated module directory");
        }
        finally
        {
            module.Teardown();
            File.WriteAllText(path, JsonSerializer.Serialize(new ModuleConfig { DisableEyeTracking = true, DisableFaceTracking = true }));
        }
        foreach (var requested in new[] { (false, false), (true, false), (false, true), (true, true) })
        {
            Check(module.Initialize(requested.Item1, requested.Item2) == (false, false), "Configuration disables both outputs");
            Check(module.Status == ModuleState.Idle, "Disabled module must be idle");
            module.Update();
            module.Teardown(); module.Teardown(); module.Update();
            Check(module.Status == ModuleState.Uninitialized && !module.ModuleInformation.Active, "Teardown is idempotent");
        }
        Check(module.ModuleInformation.StaticImages.Count == 1, "Embedded icon remains readable");
        Check(module.ModuleInformation.StaticImages[0].ReadByte() == 137, "Embedded PNG header");
    }

    // Inject immutable snapshots after normal disabled initialization. This exercises the real
    // Update/Write boundary without binding production ports, discovery, or changing host settings.
    internal static UnifiedPicoTrackingModule ActiveModule(ModuleConfig config, bool eye, bool mouth, SnapshotSource source)
    {
        // Never allocate a real transport, even if an earlier test left default config.
        var module = NewModule(); module.Initialize(false, false);
        config.DisableEyeTracking = !eye; config.DisableFaceTracking = !mouth;
        var coordinator = new SourceCoordinator(new() { DisableEyeTracking = true, DisableFaceTracking = true }, _ => { });
        Get<List<ITrackingSource>>(coordinator, "sources").Add(source);
        Set(coordinator, "config", config);
        Set(module, "config", config); Set(module, "pipeline", new TrackingPipeline(config));
        Set(module, "coordinator", coordinator); Set(module, "ownsEye", eye); Set(module, "ownsFace", mouth);
        module.Status = ModuleState.Active;
        return module;
    }

    internal sealed class SnapshotSource(SourceSnapshot snapshot) : ITrackingSource
    {
        public SourceSnapshot Snapshot { get; set; } = snapshot;
        public bool Disposed { get; private set; }
        public void Start(CancellationToken token) => throw new InvalidOperationException("Network sources are forbidden in compatibility tests.");
        public void Dispose() => Disposed = true;
    }
    internal static RawFace Face(double time, ulong stamp = 1, IndependentEyeExpression? independent = null,
        params (PicoShape Shape, float Value)[] values)
    {
        var shapes = new float[52]; var present = Enumerable.Repeat(true, 52).ToArray();
        foreach (var (shape, value) in values) shapes[(int)shape] = value;
        return new(shapes, present, true, true, stamp, time, independentEyes: independent);
    }
    internal static SourceSnapshot Snapshot(RawFace face) => SourceSnapshot.Empty(SourceKind.Bridge) with {
        Generation = 1, OutputGeneration = 1, Protocol = InputProtocol.BridgeSplit, State = ConnectionState.Connected,
        Face = face, FaceSequence = 1 };
    internal static void ResetData()
    {
        UnifiedTracking.Data = new UnifiedTrackingData();
        // IPC rejects uninitialized calibration extrema (0, 999), as the official
        // sandbox normally populates these before returning tracking data.
        UnifiedTracking.Data.Eye._maxDilation = 10;
        UnifiedTracking.Data.Eye._minDilation = 1;
    }
    internal static float Weight(UnifiedExpressions expression) => UnifiedTracking.Data.Shapes[(int)expression].Weight;
    internal static void Weight(UnifiedExpressions expression, float value) => UnifiedTracking.Data.Shapes[(int)expression].Weight = value;

    private static void PupilProvenance()
    {
        ResetData(); var time = MonotonicClock.Now;
        var eye = new NativeEye(new(0, 0, 1), false, 1, false, 7.5f, true);
        var config = new ModuleConfig(); config.Stale.NeutralPupilMillimeters = 4;
        var source = new SnapshotSource(Snapshot(Face(time)) with {
            Eye = new(eye, eye with { PupilValid = false }, new(0, 0, 1), false, 1, time), EyeSequence = 1 });
        var module = ActiveModule(config, true, true, source);
        try
        {
            module.Update(); Near(UnifiedTracking.Data.Eye.Left.PupilDiameter_MM, 7.5f, "Preserve real pupil millimeters");
#if NATIVE_HOST
            Near(UnifiedTracking.Data.Eye.Right.PupilDiameter_MM, 0, "VRCFT 6 unavailable pupil sentinel");
#else
            Near(UnifiedTracking.Data.Eye.Right.PupilDiameter_MM, 4, "VRCFT 5 preserves the configured legacy estimate");
#endif
        }
        finally { module.Teardown(); }
    }

    private static void Ownership()
    {
        foreach (var (eye, mouth) in new[] { (true, false), (false, true), (true, true) })
        {
            ResetData(); Weight(UnifiedExpressions.JawOpen, 0.91f); Weight(UnifiedExpressions.BrowOuterUpLeft, 0.87f);
            UnifiedTracking.Data.Eye.Left.Gaze.x = 0.9f;
            var source = new SnapshotSource(Snapshot(Face(MonotonicClock.Now, values: [
                (PicoShape.BrowOuterUpL, 0.3f), (PicoShape.JawOpen, 0.6f), (PicoShape.EyeLookInL, 0.4f)])));
            var module = ActiveModule(new(), eye, mouth, source);
            try
            {
                module.Update();
                Near(Weight(UnifiedExpressions.JawOpen), mouth ? 0.6f : 0.91f, "Mouth ownership");
                Near(Weight(UnifiedExpressions.BrowOuterUpLeft), eye ? 0.3f : 0.87f, "Left outer brow ownership");
                Near(UnifiedTracking.Data.Eye.Left.Gaze.x, eye ? 0.4f : 0.9f, "Gaze ownership");
                module.ModuleInformation.Active = false; module.Update();
                Check(source.Disposed, "Pause disposes owned source");
                Near(Weight(UnifiedExpressions.JawOpen), mouth ? 0 : 0.91f, "Pause preserves foreign mouth");
                Near(Weight(UnifiedExpressions.BrowOuterUpLeft), eye ? 0 : 0.87f, "Pause preserves foreign eye");
            }
            finally { module.Teardown(); }
        }
        // Deliberate source read failure exercises Update's actual recovery and cleanup path.
        var broken = new SnapshotSource(Snapshot(Face(MonotonicClock.Now)));
        var failing = ActiveModule(new(), true, true, broken);
        Set(failing, "diagnostics", null);
        try { failing.Update(); Check(broken.Disposed, "Update exception disposes source"); Near(Weight(UnifiedExpressions.JawOpen), 0, "Error neutralization"); }
        finally { failing.Teardown(); }
    }

    private static void Selection()
    {
        var config = new ModuleConfig { Source = SourceSelection.Auto };
        using var coordinator = new SourceCoordinator(new() { DisableEyeTracking = true, DisableFaceTracking = true }, _ => { });
        Set(coordinator, "config", config);
        var select = typeof(SourceCoordinator).GetMethods(Hidden).Single(m => m.Name == "Select" && m.GetParameters().Length == 2);
        var bridge = Snapshot(Face(100)); var stream = bridge with { Source = SourceKind.Streaming };
        SourceSnapshot? Pick(params SourceSnapshot[] snapshots) => (SourceSnapshot?)select.Invoke(coordinator, [snapshots, 100d]);
        Check(Pick(stream, bridge)?.Source == SourceKind.Bridge, "Auto prefers Bridge");
        var missing = bridge with { State = ConnectionState.MissingFrames, Face = null };
        Check(Pick(missing, stream)?.Source == SourceKind.Bridge, "Source remains sticky by default");
        config.AllowFallback = true;
        Check(Pick(missing, stream)?.Source == SourceKind.Streaming, "Explicit whole-source fallback");
    }

    private static void Grouped()
    {
        foreach (var neutralize in new[] { false, true })
        {
            var config = new ModuleConfig(); config.Stale.NeutralizeOnTimeout = neutralize;
            var pipeline = new TrackingPipeline(config);
            var raw = Face(100, independent: new(0.7f, 1, 1, 2, 7, 10, 20), values: [
                (PicoShape.BrowInnerUp, 0.2f), (PicoShape.EyeLookInL, 0.3f), (PicoShape.EyeLookOutR, 0.6f), (PicoShape.JawOpen, 0.4f)]);
            var snapshot = Snapshot(raw) with { GroupedExpressions = true, Face = raw.EyeObservation(true),
                RightFace = raw.EyeObservation(false), Mouth = raw.MouthObservation(), RightFaceSequence = 1, MouthSequence = 1 };
            var first = pipeline.Evaluate(snapshot, 100);
            Near(first.Left.GazeX, 0.3f, "Independent left gaze"); Near(first.Right.GazeX, 0.6f, "Independent right gaze");
            Near(first.Expressions[ExpressionChannel.BrowInnerUpLeft], 0.2f, "Independent left inner brow");
            Near(first.Expressions[ExpressionChannel.BrowInnerUpRight], 0.7f, "Independent right inner brow");
            var next = Face(100.1, 2, new(0.1f, 1, 1, 2, 1, 10, 20), (PicoShape.EyeLookInL, 0.8f), (PicoShape.JawOpen, 0.9f));
            snapshot = snapshot with { Face = next.EyeObservation(true), FaceSequence = 2 };
            var second = pipeline.Evaluate(snapshot, 100.1);
            Near(second.Left.GazeX, 0.8f, "New left source consumed"); Near(second.Right.GazeX, 0.6f, "Right source not advanced");
            Near(second.Expressions[ExpressionChannel.JawOpen], 0.4f, "Eye frame does not reprocess mouth");
            var expired = pipeline.Evaluate(snapshot, 101);
            Near(expired.Expressions[ExpressionChannel.JawOpen], neutralize ? 0 : 0.4f, "Configured mouth expiry");
            Near(expired.Right.GazeX, neutralize ? 0 : 0.6f, "Configured eye expiry");
        }
    }

    internal static ReplyUpdatePacket Capture()
    {
        var packet = new ReplyUpdatePacket(); var bytes = packet.GetBytes();
        var decoded = new ReplyUpdatePacket(); decoded.Decode(bytes); return decoded;
    }
    private static void Ipc()
    {
        ResetData(); Weight(UnifiedExpressions.BrowOuterUpLeft, 0.8f); Weight(UnifiedExpressions.BrowOuterUpRight, 0.6f);
        UnifiedTracking.Data.Eye.Left.Gaze.x = -0.4f; var eye = Capture();
        ResetData(); Weight(UnifiedExpressions.JawOpen, 0.7f); var mouth = Capture();
        ResetData(); eye.UpdateGlobalEyeState(); mouth.UpdateGlobalExpressionState();
        Near(Weight(UnifiedExpressions.JawOpen), 0.7f, "IPC independent mouth");
        Near(UnifiedTracking.Data.Eye.Left.Gaze.x, -0.4f, "IPC independent gaze");
        Near(Weight(UnifiedExpressions.BrowOuterUpRight), 0.6f, "IPC independent right outer brow");
        // Pinned official 5 and 6 include BrowOuterUpLeft in BOTH ranges. Track the upstream
        // defect explicitly instead of changing Core or writing a foreign output channel.
        Near(Weight(UnifiedExpressions.BrowOuterUpLeft), 0, "Known upstream left outer brow overwrite");
        Console.WriteLine("KNOWN-UPSTREAM: mouth IPC overwrites BrowOuterUpLeft; eye/mouth reply order affects this channel.");
        eye.UpdateGlobalEyeState(); Near(Weight(UnifiedExpressions.BrowOuterUpLeft), 0.8f, "Reverse reply order restores left brow");
    }
}
