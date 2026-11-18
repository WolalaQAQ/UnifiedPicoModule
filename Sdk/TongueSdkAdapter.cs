using UnifiedPicoModule.Runtime;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;

namespace UnifiedPicoModule.Sdk;

public static class TongueSdkAdapter
{
    // This PICO module's ownsFace is the host's authority. No PC-app source override.
    // Take fallback Out from this frame's pipeline, never the previous SDK snapshot.
    public static void Apply(TongueReceiver? receiver, UnifiedTrackingData data, bool ownsFace, float picoTongueOut,
        bool legacyExpressionCeiling = false, bool clearExtended = false)
        => ApplyView(receiver?.Snapshot(), data, ownsFace, picoTongueOut, legacyExpressionCeiling, clearExtended);

    internal static void ApplyView(TongueView? view, UnifiedTrackingData data, bool ownsFace, float picoTongueOut,
        bool legacyExpressionCeiling, bool clearExtended)
    {
        if (!ownsFace || view?.MouthMode == true) return;
        float ceiling = legacyExpressionCeiling ? .99f : 1;
        data.Shapes[(int)UnifiedExpressions.TongueOut].Weight = Math.Min(view?.Override == true ? view.Out : picoTongueOut, ceiling);
        data.Shapes[(int)UnifiedExpressions.TongueLeft].Weight = Math.Min(view?.Override == true ? view.Left : 0, ceiling);
        data.Shapes[(int)UnifiedExpressions.TongueRight].Weight = Math.Min(view?.Override == true ? view.Right : 0, ceiling);
        // TJR1-only instances keep these unowned. Once TJR2 has written them,
        // expiry, model switches and stop must clear them rather than leave a latch.
        if (clearExtended || view?.Extended == true)
        {
            data.Shapes[(int)UnifiedExpressions.TongueUp].Weight = Math.Min(view?.Override == true ? view.Up : 0, ceiling);
            data.Shapes[(int)UnifiedExpressions.TongueDown].Weight = Math.Min(view?.Override == true ? view.Down : 0, ceiling);
            data.Shapes[(int)UnifiedExpressions.TongueRoll].Weight = Math.Min(view?.Override == true ? view.Roll : 0, ceiling);
        }
    }
}
