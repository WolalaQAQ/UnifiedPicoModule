using System.Diagnostics;
using UnifiedPicoModule.Protocol;

namespace UnifiedPicoModule.Runtime;

public sealed record TongueView(bool Override, float Left, float Right, float Out, string Status,
    ulong SessionId, ulong SourceSeq, double RemainingMs);

public sealed record ReceiveResult(bool Accepted, bool NewSource, string Reason, TjrPacket? Packet, TongueView View);

// No SDK writes or socket ownership here. All time arguments are PC monotonic milliseconds.
public sealed class TongueReceiver : IDisposable
{
    public const double FreshMs = 350;
    private readonly object gate = new();
    private readonly Func<double> clock;
    private readonly double uncertainHoldMs;
    private readonly HashSet<ulong> retired = [];
    private ulong session, sequence, lastSensor;
    private bool hasSequence, hasSensor, closed, disposed, overlay;
    private float left, right, tongueOut;
    private double deadline, uncertainSince = double.NaN;
    private double sourceOffset = double.PositiveInfinity;
    private string status = "idle";
    public static double NowMs => Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency);

    public TongueReceiver(Func<double>? clock = null, double uncertainHoldMs = 100)
    {
        this.clock = clock ?? (() => NowMs);
        if (!double.IsFinite(uncertainHoldMs) || uncertainHoldMs < 0 || uncertainHoldMs > FreshMs)
            throw new ArgumentOutOfRangeException(nameof(uncertainHoldMs));
        this.uncertainHoldMs = uncertainHoldMs;
    }

    public ReceiveResult Receive(ReadOnlySpan<byte> bytes)
    {
        lock (gate)
        {
            double now = clock();
            Expire(now);
            if (disposed) return Result(false, false, "disposed", null, now);
            if (!TjrPacket.TryParse(bytes, out var decoded, out var error))
                return Result(false, false, "reject-" + error, null, now);
            var p = decoded!;
            if (retired.Contains(p.SessionId)) return Result(false, false, "retired-session", p, now);
            bool terminal = p.State is TongueState.Invalid or TongueState.Stop;
            if (p.SessionId != session)
            {
                if (session != 0) retired.Add(session);
                Release("session-change");
                session = p.SessionId; hasSequence = hasSensor = closed = false;
                sequence = lastSensor = 0; sourceOffset = double.PositiveInfinity;
            }
            if (closed) return Result(false, false, "closed-session", p, now);
            if (hasSequence && (p.SourceSeq < sequence || p.SourceSeq == sequence && !terminal))
                return Result(false, false, p.SourceSeq < sequence ? "old-seq" : "duplicate-seq", p, now);

            bool advance = !hasSequence || p.SourceSeq > sequence;
            sequence = p.SourceSeq; hasSequence = true;
            if (terminal)
            {
                Release(p.State == TongueState.Stop ? "stop" : "invalid");
                if (p.State == TongueState.Stop) { closed = true; retired.Add(session); }
                // Same-seq termination is not a new image and never advances source time.
                return Result(true, false, status, p, now);
            }

            // Even stale results keep their seq tombstone; repeated pixels cannot renew output.
            if (hasSensor && p.MouthSensorNs <= lastSensor)
            {
                Release("old-sensor");
                return Result(false, false, "old-sensor", p, now);
            }
            lastSensor = p.MouthSensorNs; hasSensor = true;
            // Anchor the source clock once; later arrivals can tighten it, never hide backlog.
            double sensorMs = p.MouthSensorNs / 1_000_000.0;
            sourceOffset = Math.Min(sourceOffset, now - p.SourceAgeMs - sensorMs);
            double sourceDeadline = Math.Min(now + FreshMs - p.SourceAgeMs, sourceOffset + sensorMs + FreshMs);
            if (sourceDeadline <= now)
            {
                Release("stale-source");
                return Result(true, advance, "stale-source", p, now);
            }

            switch (p.State)
            {
                case TongueState.Present:
                    overlay = true; left = p.Left; right = p.Right; tongueOut = p.Out;
                    deadline = sourceDeadline; uncertainSince = double.NaN; status = "present";
                    break;
                case TongueState.Absent:
                    overlay = true; left = right = tongueOut = 0;
                    deadline = sourceDeadline; uncertainSince = double.NaN; status = "absent";
                    break;
                case TongueState.Uncertain:
                    if (double.IsNaN(uncertainSince)) uncertainSince = now;
                    if (overlay && status is "present" or "uncertain")
                    {
                        deadline = Math.Min(deadline, Math.Min(sourceDeadline, uncertainSince + uncertainHoldMs));
                        status = "uncertain"; Expire(now);
                    }
                    else Release("uncertain-empty", preserveUncertainty: true);
                    break;
            }
            return Result(true, advance, status, p, now);
        }
    }

    public TongueView Snapshot()
    {
        lock (gate) { double now = clock(); Expire(now); return View(now); }
    }

    public void Stop()
    {
        lock (gate)
        {
            Release("stopped"); closed = true;
            if (session != 0) retired.Add(session);
        }
    }

    public void Dispose()
    {
        lock (gate) { Stop(); disposed = true; }
    }

    private void Expire(double now)
    {
        if (overlay && now >= deadline) Release("expired", preserveUncertainty: true);
    }

    private void Release(string reason, bool preserveUncertainty = false)
    {
        overlay = false; left = right = tongueOut = 0; deadline = 0; status = reason;
        if (!preserveUncertainty) uncertainSince = double.NaN;
    }

    private TongueView View(double now) => new(overlay, left, right, tongueOut, status, session, sequence,
        overlay ? Math.Max(0, deadline - now) : 0);
    private ReceiveResult Result(bool accepted, bool newSource, string reason, TjrPacket? packet, double now) =>
        new(accepted, newSource, reason, packet, View(now));
}
