using UnifiedPicoModule.Core;
using UnifiedPicoModule.Configuration;
using UnifiedPicoModule.Mapping;
using UnifiedPicoModule.Protocol;
using UnifiedPicoModule.Runtime;
using VRCFaceTracking.Core.Params.Data;
using U = VRCFaceTracking.Core.Params.Expressions.UnifiedExpressions;

namespace UnifiedPicoModule.Sdk;

// Called only by the module's Write boundary, after the current PICO frame is written.
public sealed class MouthSdkAdapter
{
    private static readonly HashSet<U> Native = Enum.GetNames<ExpressionChannel>().Select(name => Enum.Parse<U>(name)).ToHashSet();
    private static readonly U[][] Bindings = CreateBindings();
    private static readonly U[] MouthTargets = Bindings.SelectMany(targets => targets).Distinct().ToArray();
    private static readonly PicoShape[] NativeInputs = Enum.GetValues<MouthChannel>().Take(33)
        .Select(channel => Enum.Parse<PicoShape>(channel.ToString())).ToArray();
    private readonly HashSet<U> ownedExtensions = [];
    private bool legacyMode;

    internal void RememberMode(TongueView? view)
    {
        if (view is not null) legacyMode = !view.MouthMode && view.SessionId != 0;
    }

    private static bool NativeAvailable(RawFace? raw, MouthChannel channel) => (int)channel < NativeInputs.Length &&
        raw is not null && raw.Has(NativeInputs[(int)channel]) && raw[NativeInputs[(int)channel]] is >= 0 and <= 1;

    private static U[][] CreateBindings()
    {
        var result = new U[MfrPacket.ChannelCount][];
        foreach (var channel in Enum.GetValues<MouthChannel>())
        {
            string name = channel.ToString();
            result[(int)channel] = (int)channel < 33
                ? NeutralFaceMapper.OutputsFor(Enum.Parse<PicoShape>(name)).Select(c => Enum.Parse<U>(c.ToString())).ToArray()
                : channel switch
                {
                    MouthChannel.LipFunnelUpper => [U.LipFunnelUpperLeft, U.LipFunnelUpperRight],
                    MouthChannel.LipFunnelLower => [U.LipFunnelLowerLeft, U.LipFunnelLowerRight],
                    _ => [Enum.Parse<U>(name)]
                };
        }
        return result;
    }

    public void Apply(TongueView? view, UnifiedTrackingData data, bool ownsFace, TrackingOutput output,
        MouthMode mode, bool legacyExpressionCeiling = false)
    {
        if (!ownsFace) { ownedExtensions.Clear(); return; }
        RememberMode(view);
        var next = new HashSet<U>();
        if (!legacyMode || mode == MouthMode.ModelOnly)
        {
            // Clear the owned schema every write, including unknown/expired model fields.
            // Native support is a fresh, present scalar, never an output value or a proxy
            // fan-out (e.g. aggregate Puff does not provide independent left/right Puff).
            foreach (var sdk in MouthTargets) data.Shapes[(int)sdk].Weight = 0;
            if (mode == MouthMode.Hybrid && output.FreshMouth is { } raw)
                foreach (var channel in Enum.GetValues<MouthChannel>().Take(33))
                    if (NativeAvailable(raw, channel))
                        foreach (var sdk in Bindings[(int)channel])
                            data.Shapes[(int)sdk].Weight = output.Expressions[Enum.Parse<ExpressionChannel>(sdk.ToString())];
            if (view is { Override: true, Mouth: { } packet })
            {
                // Standard fan-out first; independently valid fine fields override only their targets.
                // These are continuous control values, not legacy TJR probabilities or directions.
                foreach (var channel in Enum.GetValues<MouthChannel>())
                {
                    if (!packet.Has(channel)) continue;
                    if (mode == MouthMode.Hybrid && (int)channel < 33 &&
                        NativeAvailable(output.FreshMouth, channel)) continue;
                    foreach (var sdk in Bindings[(int)channel])
                    {
                        data.Shapes[(int)sdk].Weight = packet[channel];
                        if (!Native.Contains(sdk)) next.Add(sdk);
                    }
                }
            }
            next.UnionWith(MouthTargets.Where(sdk => !Native.Contains(sdk)));
        }
        else
        {
            bool extended = view?.Extended == true || ownedExtensions.Contains(U.TongueUp);
            TongueSdkAdapter.ApplyView(view, data, true, output.Expressions[ExpressionChannel.TongueOut], legacyExpressionCeiling, extended);
            next.UnionWith([U.TongueLeft, U.TongueRight]);
            if (extended) next.UnionWith([U.TongueUp, U.TongueDown, U.TongueRoll]);
        }
        foreach (var sdk in ownedExtensions)
            if (!next.Contains(sdk)) data.Shapes[(int)sdk].Weight = 0;
        ownedExtensions.Clear(); ownedExtensions.UnionWith(next);
    }
}
