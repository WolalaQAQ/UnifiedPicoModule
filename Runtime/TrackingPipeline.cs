using UnifiedPicoModule.Configuration;
using UnifiedPicoModule.Core;
using UnifiedPicoModule.Mapping;
using UnifiedPicoModule.Processing;

namespace UnifiedPicoModule.Runtime;

public sealed class TrackingPipeline
{
    private readonly ModuleConfig config;
    private readonly EyeResolver eyes;
    private readonly PostProcessor processor;
    private readonly PostProcessor groupedEyeProcessor, mouthProcessor;
    private SourceKind? source;
    private long generation = -1, outputGeneration = -1, faceSequence = -1;
    private ExpressionFrame faceOutput = new();
    private ExpressionFrame independentEyeOutput = new();
    private double leftExpressionArrival, rightExpressionArrival;
    private bool hadFreshFace;
    private double lastProcessedFaceArrival;
    private ExpressionFrame groupedEyeOutput = new(), mouthOutput = new();
    private long leftSequence = -1, rightSequence = -1, mouthSequence = -1;
    private bool hadFreshMouth;
    private double lastProcessedMouthArrival;
    public ConsumptionDiagnostics Diagnostics { get; } = new();

    public TrackingPipeline(ModuleConfig config)
    {
        this.config = config; eyes = new(config); processor = new(config.PostProcessing);
        groupedEyeProcessor = new(config.PostProcessing); mouthProcessor = new(config.PostProcessing);
    }
    public void Reset() => Reset(preserveOutput: false);

    private void Reset(bool preserveOutput)
    {
        // Connection timeouts can discard transport caches without neutralizing held output.
        // Explicit source/session changes and lifecycle stops still clear output state.
        if (!preserveOutput)
        {
            eyes.Reset(); faceOutput = new(); independentEyeOutput = new();
            groupedEyeOutput = new(); mouthOutput = new();
            leftExpressionArrival = rightExpressionArrival = 0;
        }
        processor.Reset(); faceSequence = -1;
        groupedEyeProcessor.Reset(); mouthProcessor.Reset();
        leftSequence = rightSequence = mouthSequence = -1;
        hadFreshMouth = false; lastProcessedMouthArrival = 0;
        Diagnostics.ClearPending();
        source = null; generation = outputGeneration = -1; hadFreshFace = false; lastProcessedFaceArrival = 0;
    }
    public TrackingOutput Evaluate(SourceSnapshot? snapshot, double now)
    {
        if (snapshot is null)
        {
            Reset(); return Neutral;
        }
        if (source != snapshot.Source || generation != snapshot.Generation)
        {
            var preserveOutput = !config.Stale.NeutralizeOnTimeout && source == snapshot.Source &&
                outputGeneration == snapshot.OutputGeneration;
            Reset(preserveOutput); source = snapshot.Source; generation = snapshot.Generation;
            outputGeneration = snapshot.OutputGeneration;
        }
        if (snapshot.GroupedExpressions) return EvaluateGrouped(snapshot, now);
        bool fresh = snapshot.Face is { } face && MonotonicClock.Fresh(face.Arrival, now, config.Stale.FaceMs);
        if (fresh && snapshot.Face is { } raw)
        {
            if (faceSequence != snapshot.FaceSequence)
            {
                double started = MonotonicClock.Now;
                // A host stall may hide the entire stale interval from Evaluate. Detect it
                // from consecutive received samples before allowing any temporal rule to resume.
                if (lastProcessedFaceArrival > 0 && (raw.Arrival < lastProcessedFaceArrival ||
                    (raw.Arrival - lastProcessedFaceArrival) * 1000 > config.Stale.FaceMs)) processor.Reset();
                var filtered = raw.WithValidity(!config.DisableEyeTracking, !config.DisableFaceTracking);
                // Temporal rules use actual monotonic reception time, not a delayed SDK poll.
                var processed = processor.Process(filtered, NeutralFaceMapper.Map(filtered), raw.Arrival);
                if (filtered.IndependentEyes is not null && !config.DisableEyeTracking)
                    KeepIndependentExpressions(filtered, processed);
                else
                {
                    independentEyeOutput = new(); leftExpressionArrival = rightExpressionArrival = 0;
                }
                faceOutput = processed;
                Diagnostics.Processed((config.DisableEyeTracking ? 0 : 3) | (config.DisableFaceTracking ? 0 : 4),
                    snapshot.FaceSequence, faceSequence, raw.Arrival, started, MonotonicClock.Now);
                faceSequence = snapshot.FaceSequence;
                lastProcessedFaceArrival = raw.Arrival;
            }
            hadFreshFace = true;
            faceOutput = processor.Expire(faceOutput, now);
        }
        else
        {
            if (hadFreshFace) processor.Reset();
            hadFreshFace = false;
            if (config.Stale.NeutralizeOnTimeout) faceOutput = new();
        }
        return ResolveOutput(snapshot, faceOutput.Clone(), now);
    }

    private TrackingOutput EvaluateGrouped(SourceSnapshot snapshot, double now)
    {
        ProcessEye(snapshot.Face, snapshot.FaceSequence, ref leftSequence, true, now);
        ProcessEye(snapshot.RightFace, snapshot.RightFaceSequence, ref rightSequence, false, now);
        bool mouthFresh = snapshot.Mouth is { } mouth && MonotonicClock.Fresh(mouth.Arrival, now, config.Stale.FaceMs);
        if (mouthFresh && snapshot.Mouth is { } raw && !config.DisableFaceTracking)
        {
            if (mouthSequence != snapshot.MouthSequence)
            {
                double started = MonotonicClock.Now;
                if (lastProcessedMouthArrival > 0 && (raw.Arrival < lastProcessedMouthArrival ||
                    (raw.Arrival - lastProcessedMouthArrival) * 1000 > config.Stale.FaceMs)) mouthProcessor.Reset();
                var filtered = raw.WithValidity(false, true);
                mouthOutput = mouthProcessor.Process(filtered, NeutralFaceMapper.Map(filtered), raw.Arrival);
                Diagnostics.Processed(4, snapshot.MouthSequence, mouthSequence, raw.Arrival, started, MonotonicClock.Now);
                mouthSequence = snapshot.MouthSequence; lastProcessedMouthArrival = raw.Arrival;
            }
            hadFreshMouth = true;
            mouthOutput = mouthProcessor.Expire(mouthOutput, now);
        }
        else
        {
            if (hadFreshMouth) mouthProcessor.Reset();
            hadFreshMouth = false;
            if (config.Stale.NeutralizeOnTimeout) mouthOutput = new();
        }
        var output = groupedEyeOutput.Clone();
        foreach (var channel in Enum.GetValues<ExpressionChannel>())
            if (!ExpressionFrame.IsEye(channel)) output[channel] = mouthOutput[channel];
        return ResolveOutput(snapshot, output, now);
    }

    private void ProcessEye(RawFace? raw, long sequence, ref long previous, bool isLeft, double now)
    {
        if (config.DisableEyeTracking || raw is null || sequence == previous ||
            !MonotonicClock.Fresh(raw.Arrival, now, config.Stale.FaceMs)) return;
        double started = MonotonicClock.Now;
        var filtered = raw.WithValidity(true, false);
        var output = groupedEyeProcessor.Process(filtered, NeutralFaceMapper.Map(filtered), raw.Arrival);
        bool available = raw.EyeAvailable(isLeft);
        if (available)
        {
            if (isLeft) leftExpressionArrival = raw.Arrival; else rightExpressionArrival = raw.Arrival;
        }
        foreach (var channel in Enum.GetValues<ExpressionChannel>())
        {
            if (!ExpressionFrame.IsEye(channel) || ExpressionFrame.IsLeftEye(channel) != isLeft) continue;
            if (available) independentEyeOutput[channel] = output[channel];
            groupedEyeOutput[channel] = independentEyeOutput[channel];
        }
        Diagnostics.Processed(isLeft ? 1 : 2, sequence, previous, raw.Arrival, started, MonotonicClock.Now);
        previous = sequence;
    }

    private TrackingOutput ResolveOutput(SourceSnapshot snapshot, ExpressionFrame output, double now)
    {
        var (left, right) = eyes.Resolve(snapshot, now);
        if ((snapshot.GroupedExpressions || snapshot.Face?.IndependentEyes is not null) && config.Stale.NeutralizeOnTimeout)
            foreach (var channel in Enum.GetValues<ExpressionChannel>())
                if (ExpressionFrame.IsEye(channel) && !MonotonicClock.Fresh(
                    ExpressionFrame.IsLeftEye(channel) ? leftExpressionArrival : rightExpressionArrival, now, config.Stale.FaceMs))
                    output[channel] = 0;
        output.Clamp(config.LegacyExpressionCeiling ? 0.99f : 1);
        return new(output, left, right);
    }
    public void ClearMouth()
    {
        foreach (var channel in Enum.GetValues<ExpressionChannel>())
            if (!ExpressionFrame.IsEye(channel)) { faceOutput[channel] = 0; mouthOutput[channel] = 0; }
        mouthProcessor.Reset();
        mouthSequence = -1; hadFreshMouth = false; lastProcessedMouthArrival = 0;
    }
    private void KeepIndependentExpressions(RawFace raw, ExpressionFrame output)
    {
        bool leftAvailable = raw.EyeAvailable(true), rightAvailable = raw.EyeAvailable(false);
        if (leftAvailable) leftExpressionArrival = raw.Arrival;
        if (rightAvailable) rightExpressionArrival = raw.Arrival;
        foreach (var channel in Enum.GetValues<ExpressionChannel>())
        {
            if (!ExpressionFrame.IsEye(channel)) continue;
            bool available = ExpressionFrame.IsLeftEye(channel) ? leftAvailable : rightAvailable;
            if (available) independentEyeOutput[channel] = output[channel];
            else output[channel] = independentEyeOutput[channel];
        }
    }
    public TrackingOutput Neutral => new(new(), new(PupilMillimeters: config.Stale.NeutralPupilMillimeters),
        new(PupilMillimeters: config.Stale.NeutralPupilMillimeters));
}
