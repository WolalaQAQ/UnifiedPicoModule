using UnifiedPicoModule.Core;
using C = UnifiedPicoModule.Core.ExpressionChannel;
using P = UnifiedPicoModule.Core.PicoShape;

namespace UnifiedPicoModule.Mapping;

public static class NeutralFaceMapper
{
    // Declarative correspondence between sensor meanings and output channel meanings.
    // Multiple outputs from one slot are explicit; no compensation or implicit gain.
    private static readonly (P Input, C[] Outputs)[] Bindings =
    [
        (P.EyeSquintL, [C.EyeSquintLeft]), (P.EyeSquintR, [C.EyeSquintRight]),
        (P.EyeWideL, [C.EyeWideLeft]), (P.EyeWideR, [C.EyeWideRight]),
        (P.BrowDownL, [C.BrowPinchLeft, C.BrowLowererLeft]),
        (P.BrowDownR, [C.BrowPinchRight, C.BrowLowererRight]),
        (P.BrowOuterUpL, [C.BrowOuterUpLeft]), (P.BrowOuterUpR, [C.BrowOuterUpRight]),
        (P.CheekSquintL, [C.CheekSquintLeft]), (P.CheekSquintR, [C.CheekSquintRight]),
        (P.CheekPuff, [C.CheekPuffLeft, C.CheekPuffRight]),
        (P.JawOpen, [C.JawOpen]), (P.JawLeft, [C.JawLeft]), (P.JawRight, [C.JawRight]),
        (P.JawForward, [C.JawForward]), (P.MouthClose, [C.MouthClosed]),
        (P.MouthRollUpper, [C.LipSuckUpperLeft, C.LipSuckUpperRight]),
        (P.MouthRollLower, [C.LipSuckLowerLeft, C.LipSuckLowerRight]),
        (P.MouthFunnel, [C.LipFunnelUpperLeft, C.LipFunnelUpperRight, C.LipFunnelLowerLeft, C.LipFunnelLowerRight]),
        (P.MouthPucker, [C.LipPuckerUpperLeft, C.LipPuckerUpperRight, C.LipPuckerLowerLeft, C.LipPuckerLowerRight]),
        (P.MouthUpperUpL, [C.MouthUpperUpLeft, C.MouthUpperDeepenLeft]),
        (P.MouthUpperUpR, [C.MouthUpperUpRight, C.MouthUpperDeepenRight]),
        (P.NoseSneerL, [C.NoseSneerLeft]), (P.NoseSneerR, [C.NoseSneerRight]),
        (P.MouthLowerDownL, [C.MouthLowerDownLeft]), (P.MouthLowerDownR, [C.MouthLowerDownRight]),
        (P.MouthLeft, [C.MouthUpperLeft, C.MouthLowerLeft]),
        (P.MouthRight, [C.MouthUpperRight, C.MouthLowerRight]),
        (P.MouthSmileL, [C.MouthCornerPullLeft, C.MouthCornerSlantLeft]),
        (P.MouthSmileR, [C.MouthCornerPullRight, C.MouthCornerSlantRight]),
        (P.MouthFrownL, [C.MouthFrownLeft]), (P.MouthFrownR, [C.MouthFrownRight]),
        (P.MouthStretchL, [C.MouthStretchLeft]), (P.MouthStretchR, [C.MouthStretchRight]),
        (P.MouthDimpleL, [C.MouthDimpleLeft]), (P.MouthDimpleR, [C.MouthDimpleRight]),
        (P.MouthShrugUpper, [C.MouthRaiserUpper]), (P.MouthShrugLower, [C.MouthRaiserLower]),
        (P.MouthPressL, [C.MouthPressLeft]), (P.MouthPressR, [C.MouthPressRight]),
        (P.TongueOut, [C.TongueOut])
    ];

    internal static IReadOnlyList<C> OutputsFor(P input) => Bindings.First(binding => binding.Input == input).Outputs;

    public static ExpressionFrame Map(RawFace raw)
    {
        var result = new ExpressionFrame();
        foreach (var (input, outputs) in Bindings)
        {
            if (!raw.Has(input)) continue;
            foreach (var output in outputs) result[output] = Math.Clamp(raw[input], 0, 1);
        }
        if (raw.HasInnerBrow(true)) result[C.BrowInnerUpLeft] = Math.Clamp(raw.InnerBrow(true), 0, 1);
        if (raw.HasInnerBrow(false)) result[C.BrowInnerUpRight] = Math.Clamp(raw.InnerBrow(false), 0, 1);
        return result;
    }

    public static bool HasConsumableExpressions(RawFace raw, bool eyeEnabled, bool faceEnabled) =>
        eyeEnabled && (raw.HasInnerBrow(true) || raw.HasInnerBrow(false)) ||
        Bindings.Any(binding => raw.Has(binding.Input) &&
            binding.Outputs.Any(channel => ExpressionFrame.IsEye(channel) ? eyeEnabled : faceEnabled));
}
