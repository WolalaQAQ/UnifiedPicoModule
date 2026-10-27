using Microsoft.Extensions.Logging;
using System.Runtime.InteropServices;
using UnifiedPicoModule.Configuration;
using UnifiedPicoModule.Core;
using UnifiedPicoModule.Runtime;
using UnifiedPicoModule.Transport;
using VRCFaceTracking;
using VRCFaceTracking.Core.Library;
using VRCFaceTracking.Core.Params.Expressions;

namespace UnifiedPicoModule.Sdk;

public sealed class UnifiedPicoTrackingModule : ExtTrackingModule
{
    private readonly object gate = new();
    private static readonly (ExpressionChannel Channel, UnifiedExpressions Sdk)[] Channels =
        Enum.GetValues<ExpressionChannel>().Select(c => (c, Enum.Parse<UnifiedExpressions>(c.ToString(), false))).ToArray();
    private ModuleConfig config = new();
    private TrackingPipeline? pipeline;
    private SourceCoordinator? coordinator;
    private DiagnosticReporter? diagnostics;
    private bool ownsEye, ownsFace, stopped = true, wasActive;
    private bool timerPolicyChanged, timerPolicyAttempted;
    private PowerThrottlingState previousTimerPolicy;
    private double lastError;
    private TongueReceiver? tongue;
    private TjrUdpReceiver? tongueTransport;

    [StructLayout(LayoutKind.Sequential)]
    private struct PowerThrottlingState { public uint Version, ControlMask, StateMask; }
    private const int ProcessPowerThrottling = 4;
    private const uint IgnoreTimerResolution = 4;
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessInformation(nint process, int kind,
        ref PowerThrottlingState information, uint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessInformation(nint process, int kind,
        ref PowerThrottlingState information, uint size);

    public override (bool SupportsEye, bool SupportsExpression) Supported => (true, true);
    public override (bool eyeSuccess, bool expressionSuccess) Initialize(bool eyeAvailable, bool expressionAvailable)
    {
        lock (gate)
        {
            StopSources();
            ModuleInformation.Name = "Unified PICO Face Tracking";
            // Keep the stream available for the host's deferred image loading.
            // Embedded PNG works offline and does not depend on loose asset paths.
            if (ModuleInformation.StaticImages is not { Count: > 0 })
            {
                var icon = typeof(UnifiedPicoTrackingModule).Assembly
                    .GetManifestResourceStream("UnifiedPicoModule.Branding.ModuleIcon.png");
                ModuleInformation.StaticImages = icon is null ? [] : [icon];
                if (icon is null) Log("Embedded module icon is missing; tracking remains available.");
            }
            ModuleInformation.Active = true;
            config = ConfigStore.Load(Path.Combine(Path.GetDirectoryName(GetType().Assembly.Location)!, "UnifiedPicoModule.json"), Log);
            Log("Eye sources: gaze from the EyeLook shapes; openness from 1 - EyeBlink; native eye packets supply a measured pupil diameter.");
            Log($"Timeout neutralization: {(config.Stale.NeutralizeOnTimeout ? "enabled" : "disabled; hold last values")}.");
            var enabledRules = typeof(PostProcessingSettings).GetProperties().Where(p =>
                (bool?)p.PropertyType.GetProperty("Enabled")?.GetValue(p.GetValue(config.PostProcessing)) == true).Select(p => p.Name).ToArray();
            Log("Enabled postprocessing: " + (enabledRules.Length == 0 ? "none (neutral mapping)" : string.Join(", ", enabledRules)) + "; fixed ConfigVersion=1 order; no runtime preset.");
            ownsEye = eyeAvailable && !config.DisableEyeTracking;
            ownsFace = expressionAvailable && !config.DisableFaceTracking;
            // The pipeline must not advance time-state for a host-unavailable output group.
            config.DisableEyeTracking = !ownsEye; config.DisableFaceTracking = !ownsFace;
            pipeline = new(config); diagnostics = new(Log); stopped = false;
            Status = ownsEye || ownsFace ? ModuleState.Active : ModuleState.Idle;
            if (ownsEye || ownsFace) StartSources();
            Log("UnifiedPicoModule 0.4.0: X v1/v2; VRCFT SDK 5.4.5 / net10. UDP is unauthenticated: trusted LAN only.");
            return (ownsEye, ownsFace);
        }
    }
    public override void Update()
    {
        lock (gate)
        {
            if (!stopped && pipeline is not null)
            {
                var active = ModuleInformation.Active && Status == ModuleState.Active && (ownsEye || ownsFace);
                if (!active)
                {
                    if (wasActive) { StopSources(); pipeline.Reset(); Write(pipeline.Neutral); }
                    wasActive = false;
                }
                else
                {
                    try
                    {
                        if (coordinator is null) StartSources();
                        var (selected, snapshots, now) = coordinator!.Select();
                        if (selected?.State is ConnectionState.Faulted or ConnectionState.Stopped ||
                            snapshots.All(s => s.State is ConnectionState.Faulted or ConnectionState.Stopped))
                            RestoreTimerPolicy();
                        else HonorTimerResolution();
                        var output = pipeline.Evaluate(selected, now);
                        double writeStarted = MonotonicClock.Now;
                        Write(output);
                        pipeline.Diagnostics.Written(writeStarted, MonotonicClock.Now);
                        diagnostics!.Report(snapshots, selected, output, now, pipeline.Diagnostics);
                        wasActive = true;
                    }
                    catch (Exception exception)
                    {
                        StopSources();
                        pipeline.Reset(); Write(pipeline.Neutral);
                        var now = MonotonicClock.Now;
                        if (now - lastError > 5) { Log($"Update recovered to neutral: {exception.GetType().Name}: {exception.Message}"); lastError = now; }
                    }
                }
            }
        }
        // The SDK invokes Update in an unpaced loop. Never spin or wait indefinitely for UDP.
        Thread.Sleep(2);
    }
    public override void Teardown()
    {
        lock (gate)
        {
            stopped = true; StopSources();
            if (pipeline is not null) { pipeline.Reset(); Write(pipeline.Neutral); }
            pipeline = null; wasActive = false; ownsEye = ownsFace = false;
            ModuleInformation.Active = false; Status = ModuleState.Uninitialized;
        }
    }
    private void StartSources()
    {
        try
        {
            HonorTimerResolution();
            coordinator = new(config, Log);
            StartTongue();
            wasActive = true;
        }
        catch
        {
            StopSources();
            throw;
        }
    }
    private void HonorTimerResolution()
    {
        if (!OperatingSystem.IsWindows() || timerPolicyAttempted || timerPolicyChanged) return;
        timerPolicyAttempted = true;
        // Official VRCFT 5.4.5 already requests a 1 ms timer period in its module process.
        // Honor that request in the background; leave CPU throttling and priorities alone.
        previousTimerPolicy = new() { Version = 1 };
        uint size = (uint)Marshal.SizeOf<PowerThrottlingState>();
        if (GetProcessInformation(-1, ProcessPowerThrottling, ref previousTimerPolicy, size))
        {
            var policy = previousTimerPolicy;
            policy.ControlMask |= IgnoreTimerResolution;
            policy.StateMask &= ~IgnoreTimerResolution;
            timerPolicyChanged = SetProcessInformation(-1, ProcessPowerThrottling, ref policy, size);
        }
        Log($"Sandbox timer policy: honorResolution={timerPolicyChanged} error={(timerPolicyChanged ? 0 : Marshal.GetLastPInvokeError())}.");
    }
    private void StopSources()
    {
        try { StopTongue(); }
        finally
        {
            try { coordinator?.Dispose(); }
            finally { coordinator = null; RestoreTimerPolicy(); }
        }
    }
    private void StartTongue()
    {
        if (!ownsFace || !config.Tongue.Enabled) return;
        var receiver = new TongueReceiver();
        try
        {
            tongueTransport = new(receiver, config.Tongue.Port, log: Log);
            tongue = receiver;
            Log($"Tongue TJR1: listening on 127.0.0.1:{tongueTransport.Port}; PC app output, expiry350ms.");
        }
        catch (System.Net.Sockets.SocketException error)
        {
            receiver.Dispose();
            Log($"Tongue TJR1 unavailable; normal PICO tracking continues: {error.Message}");
        }
    }
    private void StopTongue()
    {
        try { tongueTransport?.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        finally { tongueTransport = null; tongue?.Dispose(); tongue = null; }
    }
    private void RestoreTimerPolicy()
    {
        timerPolicyAttempted = false;
        if (!timerPolicyChanged) return;
        var policy = new PowerThrottlingState { Version = 1 };
        uint size = (uint)Marshal.SizeOf<PowerThrottlingState>();
        bool restored = false;
        if (GetProcessInformation(-1, ProcessPowerThrottling, ref policy, size))
        {
            // Other components may have changed non-timer bits while tracking.
            policy.ControlMask = (policy.ControlMask & ~IgnoreTimerResolution) |
                (previousTimerPolicy.ControlMask & IgnoreTimerResolution);
            policy.StateMask = (policy.StateMask & ~IgnoreTimerResolution) |
                (previousTimerPolicy.StateMask & IgnoreTimerResolution);
            restored = SetProcessInformation(-1, ProcessPowerThrottling, ref policy, size);
        }
        // Keep the original snapshot if either call failed so the next stop can retry it.
        if (restored) timerPolicyChanged = false;
        Log($"Sandbox timer policy restored={restored} error={(restored ? 0 : Marshal.GetLastPInvokeError())}.");
    }
    private void Log(string message) => Logger?.LogInformation("{UnifiedPicoMessage}", message);

    // Sole UnifiedTracking write boundary; never zero SDK channels we do not own.
    private void Write(TrackingOutput output)
    {
        var data = UnifiedTracking.Data;
        foreach (var (channel, sdk) in Channels)
            if (ExpressionFrame.IsEye(channel) ? ownsEye : ownsFace) data.Shapes[(int)sdk].Weight = output.Expressions[channel];
        TongueSdkAdapter.Apply(tongue, data, ownsFace, output.Expressions[ExpressionChannel.TongueOut], config.LegacyExpressionCeiling);
        if (!ownsEye) return;
        data.Eye.Left.Gaze.x = output.Left.GazeX; data.Eye.Left.Gaze.y = output.Left.GazeY;
        data.Eye.Left.Openness = output.Left.Openness; data.Eye.Left.PupilDiameter_MM = output.Left.PupilMillimeters;
        data.Eye.Right.Gaze.x = output.Right.GazeX; data.Eye.Right.Gaze.y = output.Right.GazeY;
        data.Eye.Right.Openness = output.Right.Openness; data.Eye.Right.PupilDiameter_MM = output.Right.PupilMillimeters;
    }
}
