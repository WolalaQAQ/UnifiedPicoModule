using System.Diagnostics;
using UnifiedPicoModule.Protocol;

namespace UnifiedPicoModule.Runtime;

public sealed record TongueView(bool Override, float Left, float Right, float Out, string Status,
    ulong SessionId, ulong SourceSeq, double RemainingMs,
    bool Extended = false, float Up = 0, float Down = 0, float Roll = 0,
    MfrPacket? Mouth = null, bool MouthMode = false);

public sealed record ReceiveResult(bool Accepted, bool NewSource, string Reason, TjrPacket? Packet, TongueView View,
    MfrPacket? MouthPacket = null);

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
    private float up, down, roll;
    private bool extendedOwned;
    private bool mouthMode;
    private MfrPacket? mouth;
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
            TjrPacket? p = null; MfrPacket? m = null; string error;
            bool isMouth = bytes.Length == MfrPacket.Size || bytes.Length >= 4 && bytes[..4].SequenceEqual("MFR1"u8);
            if (isMouth ? !MfrPacket.TryParse(bytes, out m, out error) : !TjrPacket.TryParse(bytes, out p, out error))
                return Result(false, false, "reject-" + error, null, now);
            ReceiveResult Finish(bool accepted, bool newSource, string reason) =>
                new(accepted, newSource, reason, p, View(now), m);
            // Protocol promotion is one-way for this listener lifetime. An old legacy producer
            // with a previously unseen session cannot evict a whole-mouth model after release.
            if (!isMouth && mouthMode) return Finish(false, false, "legacy-after-mouth");
            ulong id = m?.SessionId ?? p!.SessionId, seq = m?.SourceSeq ?? p!.SourceSeq;
            ulong sensor = m?.MouthSensorNs ?? p!.MouthSensorNs;
            float age = m?.SourceAgeMs ?? p!.SourceAgeMs;
            if (retired.Contains(id)) return Finish(false, false, "retired-session");
            bool terminal = isMouth ? m!.State == MouthState.Release : p!.State is TongueState.Invalid or TongueState.Stop;
            if (id != session)
            {
                if (session != 0) retired.Add(session);
                Release("session-change");
                session = id; hasSequence = hasSensor = closed = false;
                sequence = lastSensor = 0; sourceOffset = double.PositiveInfinity;
            }
            if (closed) return Finish(false, false, "closed-session");
            if (hasSequence && (seq < sequence || seq == sequence && !terminal))
                return Finish(false, false, seq < sequence ? "old-seq" : "duplicate-seq");

            bool advance = !hasSequence || seq > sequence;
            sequence = seq; hasSequence = true;
            mouthMode |= isMouth;
            if (terminal)
            {
                Release(isMouth ? "mouth-release" : p!.State == TongueState.Stop ? "stop" : "invalid");
                if (p?.State == TongueState.Stop) { closed = true; retired.Add(session); }
                // Same-seq termination is not a new image and never advances source time.
                return Finish(true, false, status);
            }

            // Even stale results keep their seq tombstone; repeated pixels cannot renew output.
            if (hasSensor && sensor <= lastSensor)
            {
                Release("old-sensor");
                return Finish(false, false, "old-sensor");
            }
            lastSensor = sensor; hasSensor = true;
            // Anchor the source clock once; later arrivals can tighten it, never hide backlog.
            double sensorMs = sensor / 1_000_000.0;
            sourceOffset = Math.Min(sourceOffset, now - age - sensorMs);
            double sourceDeadline = Math.Min(now + FreshMs - age, sourceOffset + sensorMs + FreshMs);
            if (sourceDeadline <= now)
            {
                Release("stale-source");
                return Finish(true, advance, "stale-source");
            }

            if (isMouth)
            {
                mouth = m; overlay = true; left = right = tongueOut = up = down = roll = 0;
                deadline = sourceDeadline; uncertainSince = double.NaN; status = "mouth-fresh";
                return Finish(true, advance, status);
            }

            switch (p!.State)
            {
                case TongueState.Present:
                    overlay = true; left = p.Left; right = p.Right; tongueOut = p.Out;
                    up = p.Up; down = p.Down; roll = p.Roll; extendedOwned |= p.Extended;
                    if (p.Extended && p.Out == 0) left = right = up = down = roll = 0;
                    deadline = sourceDeadline; uncertainSince = double.NaN; status = "present";
                    break;
                case TongueState.Absent:
                    overlay = true; left = right = tongueOut = 0;
                    up = down = roll = 0; extendedOwned |= p.Extended;
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
            return Finish(true, advance, status);
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
        overlay = false; mouth = null; left = right = tongueOut = up = down = roll = 0; deadline = 0; status = reason;
        if (!preserveUncertainty) uncertainSince = double.NaN;
    }

    private TongueView View(double now) => new(overlay, left, right, tongueOut, status, session, sequence,
        overlay ? Math.Max(0, deadline - now) : 0, extendedOwned, up, down, roll, mouth, mouthMode);
    private ReceiveResult Result(bool accepted, bool newSource, string reason, TjrPacket? packet, double now) =>
        new(accepted, newSource, reason, packet, View(now));
}
