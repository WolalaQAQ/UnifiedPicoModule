using System.Buffers.Binary;
using System.Reflection;
using System.Text;
using VRCFaceTracking;
using VRCFaceTracking.Core.OSC;
using VRCFaceTracking.Core.OSC.Query;
using VRCFaceTracking.Core.Params.Expressions;
using VRCFaceTracking.Core.Services;

namespace CompatibilityTests;

internal static partial class Program
{
    private static Queue<OscMessage> SendQueue => (Queue<OscMessage>)typeof(ParameterSenderService)
        .GetField("SendQueue", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;

    private static void LegacyTests()
    {
        Run("legacy-avatar-actual-formulas-and-osc-encoding", () =>
        {
            var avatar = new OscQueryAvatarInfo(new OscQueryNode { FullPath = "/avatar", Contents = new() {
                ["parameters"] = new() { Contents = new() {
                    ["jaw"] = new() { FullPath = "/avatar/parameters/v2/JawOpen", OscType = "f" },
                    ["brow"] = new() { FullPath = "/avatar/parameters/v2/BrowInnerUp", OscType = "f" }
                } }
            } });
            var parameters = UnifiedExpressionsParameters.ExpressionParameters;
            try
            {
                foreach (var parameter in parameters)
                {
#if NATIVE_HOST
                    parameter.ResetParam(avatar.Parameters, avatar);
#else
                    parameter.ResetParam(avatar.Parameters);
#endif
                }
                ResetData(); ParameterSenderService.Clear();
                Weight(UnifiedExpressions.JawOpen, 0.21f);
                Weight(UnifiedExpressions.BrowInnerUpLeft, 0.8f); Weight(UnifiedExpressions.BrowInnerUpRight, 0);
                UnifiedTracking.OnUnifiedDataUpdated(UnifiedTracking.Data);
                var jaw = SendQueue.Single(m => m.Address == "/avatar/parameters/v2/JawOpen");
                var brow = SendQueue.Single(m => m.Address == "/avatar/parameters/v2/BrowInnerUp");
                Near((float)jaw.Value, 0.21f, "Official legacy JawOpen formula");
                Near((float)brow.Value, 0.4f, "Official legacy combined brow remains average");
                var bytes = new byte[4096]; int length = jaw.Encode(bytes, default).GetAwaiter().GetResult();
                int addressEnd = Array.IndexOf(bytes, (byte)0); int tagStart = (addressEnd + 4) & ~3;
                Check(Encoding.ASCII.GetString(bytes, 0, addressEnd) == "/avatar/parameters/v2/JawOpen", "Actual legacy OSC address");
                Check(Encoding.ASCII.GetString(bytes, tagStart, 4) == ",f\0\0" && length == tagStart + 8, "One legacy float argument");
                Near(BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(tagStart + 4, 4))), 0.21f, "Actual legacy OSC float encoding");
            }
            finally
            {
                foreach (var parameter in parameters)
                {
#if NATIVE_HOST
                    parameter.ResetParam([], avatar);
#else
                    parameter.ResetParam([]);
#endif
                }
                ParameterSenderService.Clear();
            }
        });
    }
}
