using UnifiedPicoModule.Configuration;
using UnifiedPicoModule.Core;
using UnifiedPicoModule.Protocol;
using UnifiedPicoModule.Mapping;
using UnifiedPicoModule.Transport;

namespace UnifiedPicoModule.Runtime;

public sealed class SourceCoordinator : IDisposable
{
    private readonly ModuleConfig config;
    private readonly Action<string> log;
    private readonly List<ITrackingSource> sources = [];
    private readonly CancellationTokenSource cancellation = new();
    private readonly double started = MonotonicClock.Now;
    private SourceKind? selected;

    public SourceCoordinator(ModuleConfig config, Action<string> log)
    {
        this.config = config; this.log = log;
        if (config.DisableEyeTracking && config.DisableFaceTracking) return;
        if (config.Source != SourceSelection.Streaming) sources.Add(new BridgeUdpSource(config.Bridge, log,
            !config.DisableEyeTracking, !config.DisableFaceTracking));
        if (config.Source != SourceSelection.Bridge)
        {
            StreamingHeaderProfile? profile = null;
            if (config.Streaming.HeaderProfile is { StartCode1: { } first, StartCode2: { } second,
                SubType: { } subtype, MultiPack: { } multi, CurrentIndex: { } index, Version: { } version })
                profile = new((byte)first, (byte)second, (byte)subtype, (byte)multi, (byte)index, (ushort)version);
            if (config.Streaming.Protocol != InputProtocol.StreamingBodyOnly && profile is null)
                log("StreamingHeader16 has no configured profile: accepting >=908-byte frames with trackingType=2 and taking the body after the 16-byte header (matching SAFT). Trailing bytes are ignored (PICO Connect 10.6.6 sends 912). Header magic/subtype/version/fragment fields are not validated in this mode, and 29765 sender compatibility has not been accepted with a documented profile.");
            sources.Add(new StreamingUdpSource(config.Streaming, log, profile));
        }
        foreach (var source in sources) source.Start(cancellation.Token);
    }

    public (SourceSnapshot? Selected, IReadOnlyList<SourceSnapshot> Snapshots, double Now) Select()
    {
        // Capture immutable snapshots before sampling the clock so none has a future arrival.
        // Return this same batch and time for pipeline evaluation and diagnostics.
        var snapshots = Snapshots;
        var now = MonotonicClock.Now;
        return (Select(snapshots, now), snapshots, now);
    }

    private SourceSnapshot? Select(IReadOnlyList<SourceSnapshot> snapshots, double now)
    {
        if (selected is { } kind)
        {
            var current = snapshots.FirstOrDefault(s => s.Source == kind);
            if (!config.AllowFallback || current is not null && Usable(current, now)) return current;
            var next = snapshots.FirstOrDefault(s => s.Source != kind && Usable(s, now));
            if (next is null) return current;
            selected = next.Source; log($"Explicit fallback selected {next.Source}/{next.Protocol}; no cross-source fusion.");
            return next;
        }
        var bridge = snapshots.FirstOrDefault(s => s.Source == SourceKind.Bridge && Usable(s, now));
        var streaming = snapshots.FirstOrDefault(s => s.Source == SourceKind.Streaming && Usable(s, now));
        var candidate = bridge ?? (config.Source == SourceSelection.Streaming ||
            (now - started) * 1000 >= config.AutoSelectionWindowMs ? streaming : null);
        if (candidate is null) return null;
        selected = candidate.Source; log($"Selected sticky source {candidate.Source}/{candidate.Protocol}, generation {candidate.Generation}.");
        return candidate;
    }

    private bool Usable(SourceSnapshot snapshot, double now)
    {
        if (snapshot.State is ConnectionState.Faulted or ConnectionState.Stopped) return false;
        if (!config.DisableEyeTracking && snapshot.Eye is { } eye && MonotonicClock.Fresh(eye.Arrival, now, config.Stale.EyeMs) &&
            EyeResolver.HasConsumableNative(snapshot, config)) return true;
        if (snapshot.GroupedExpressions)
            return UsableGroup(snapshot.Face, now, !config.DisableEyeTracking, false) ||
                UsableGroup(snapshot.RightFace, now, !config.DisableEyeTracking, false) ||
                UsableGroup(snapshot.Mouth, now, false, !config.DisableFaceTracking);
        return snapshot.Face is { } face && MonotonicClock.Fresh(face.Arrival, now, config.Stale.FaceMs) &&
            (NeutralFaceMapper.HasConsumableExpressions(face, !config.DisableEyeTracking, !config.DisableFaceTracking) ||
                !config.DisableEyeTracking && EyeResolver.HasConsumableFace(snapshot, config));
    }
    private bool UsableGroup(RawFace? raw, double now, bool eyeEnabled, bool faceEnabled) =>
        raw is not null && MonotonicClock.Fresh(raw.Arrival, now, config.Stale.FaceMs) &&
        NeutralFaceMapper.HasConsumableExpressions(raw, eyeEnabled, faceEnabled);
    public IReadOnlyList<SourceSnapshot> Snapshots => sources.Select(s => s.Snapshot).ToArray();
    /// <summary>Headset address found by the Bridge source (explicit Bridge.IP or auto-discovery).</summary>
    public System.Net.IPAddress? BridgePeer => sources.OfType<BridgeUdpSource>().FirstOrDefault()?.PeerAddress;
    public void Dispose()
    {
        cancellation.Cancel();
        foreach (var source in sources) source.Dispose();
        cancellation.Dispose();
    }
}
