using UnifiedPicoModule.Core;
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
    private readonly HashSet<U> ownedExtensions = [];
    private bool mouthMode;

    internal void RememberMode(TongueView? view)
    {
        if (view is not null) mouthMode = view.MouthMode;
    }

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

    public void Apply(TongueView? view, UnifiedTrackingData data, bool ownsFace, float picoTongueOut,
        bool legacyExpressionCeiling = false)
    {
        if (!ownsFace) { ownedExtensions.Clear(); return; }
        RememberMode(view);
        var next = new HashSet<U>();
        if (mouthMode)
        {
            if (view is { Override: true, Mouth: { } packet })
            {
                // Overlay only channels whose valid bit is set; unset native channels keep the
                // Bridge/pipeline values written just before Apply. Enum order visits aggregate
                // native channels (0..32) before split/extended channels (33..49) so split values win.
                foreach (var channel in Enum.GetValues<MouthChannel>())
                {
                    if (!packet.Has(channel)) continue;
                    foreach (var sdk in Bindings[(int)channel])
                    {
                        data.Shapes[(int)sdk].Weight = packet[channel];
                        if (!Native.Contains(sdk)) next.Add(sdk);
                    }
                }
            }
        }
        else
        {
            bool extended = view?.Extended == true || ownedExtensions.Contains(U.TongueUp);
            TongueSdkAdapter.ApplyView(view, data, true, picoTongueOut, legacyExpressionCeiling, extended);
            next.UnionWith([U.TongueLeft, U.TongueRight]);
            if (extended) next.UnionWith([U.TongueUp, U.TongueDown, U.TongueRoll]);
        }
        foreach (var sdk in ownedExtensions)
            if (!next.Contains(sdk)) data.Shapes[(int)sdk].Weight = 0;
        ownedExtensions.Clear(); ownedExtensions.UnionWith(next);
    }
}
