using UnifiedPicoModule.Runtime;
using VRCFaceTracking.Core.Params.Data;
using VRCFaceTracking.Core.Params.Expressions;

namespace UnifiedPicoModule.Sdk;

public static class TongueSdkAdapter
{
    // This PICO module's ownsFace is the host's authority. No PC-app source override.
    // Take fallback Out from this frame's pipeline, never the previous SDK snapshot.
    public static void Apply(TongueReceiver? receiver, UnifiedTrackingData data, bool ownsFace, float picoTongueOut,
        bool legacyExpressionCeiling = false)
    {
        if (!ownsFace) return;
        var view = receiver?.Snapshot();
        float ceiling = legacyExpressionCeiling ? .99f : 1;
        data.Shapes[(int)UnifiedExpressions.TongueOut].Weight = Math.Min(view?.Override == true ? view.Out : picoTongueOut, ceiling);
        data.Shapes[(int)UnifiedExpressions.TongueLeft].Weight = Math.Min(view?.Override == true ? view.Left : 0, ceiling);
        data.Shapes[(int)UnifiedExpressions.TongueRight].Weight = Math.Min(view?.Override == true ? view.Right : 0, ceiling);
        // TJR1 does not support Up/Down; leave both unchanged.
    }
}
