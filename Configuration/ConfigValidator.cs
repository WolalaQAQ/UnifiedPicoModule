using System.Collections;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using UnifiedPicoModule.Core;

namespace UnifiedPicoModule.Configuration;

public static class ConfigValidator
{
    public static ModuleConfig Validate(ModuleConfig config, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(log);
        if (config.ConfigVersion != 1)
        { log($"ConfigVersion={config.ConfigVersion} 未知；使用安全默认。"); config = new ModuleConfig(); }
        if (!Enum.IsDefined(config.Source)) { log($"Source={config.Source} 未知；使用 Auto。"); config.Source = SourceSelection.Auto; }
        config.Bridge ??= new(); config.Streaming ??= new(); config.Stale ??= new(); config.EyeGain ??= new(); config.PostProcessing ??= new();
        config.Tongue ??= new();
        config.Tongue.Port = Integer(config.Tongue.Port, 1, 65535, 45682, "Tongue.Port", log);
        config.Bridge.IP = Address(config.Bridge.IP, null, "Bridge.IP", log);
        config.Streaming.IP = Address(config.Streaming.IP, null, "Streaming.IP", log);
        config.Bridge.ListenAddress = Address(config.Bridge.ListenAddress, "0.0.0.0", "Bridge.ListenAddress", log)!;
        config.Streaming.ListenAddress = Address(config.Streaming.ListenAddress, "0.0.0.0", "Streaming.ListenAddress", log)!;
        if (config.Bridge.Protocol is not null and not (InputProtocol.BridgeSplit or InputProtocol.DaemonLegacy536))
        { log($"Bridge.Protocol={config.Bridge.Protocol} 不适用于 Bridge；使用自动识别。"); config.Bridge.Protocol = null; }
        if (config.Streaming.Protocol is not null and not (InputProtocol.StreamingHeader16 or InputProtocol.StreamingBodyOnly))
        { log($"Streaming.Protocol={config.Streaming.Protocol} 不适用于串流；使用自动识别。"); config.Streaming.Protocol = null; }
        config.Bridge.ReconnectMs = Integer(config.Bridge.ReconnectMs, 100, 60000, 2000, "Bridge.ReconnectMs", log);
        config.Bridge.ControlTimeoutMs = Integer(config.Bridge.ControlTimeoutMs, 1000, 120000, 30000, "Bridge.ControlTimeoutMs", log);
        config.Streaming.SessionTimeoutMs = Integer(config.Streaming.SessionTimeoutMs, 100, 60000, 2000, "Streaming.SessionTimeoutMs", log);
        if (config.Streaming.HeaderProfile is { } profile)
        {
            var headerValid = true;
            foreach (var property in typeof(StreamingHeaderSettings).GetProperties())
            {
                var number = (int?)property.GetValue(profile);
                var maximum = property.Name == "Version" ? 65535 : 255;
                if (number is null || number < 0 || number > maximum)
                {
                    log($"Streaming.HeaderProfile.{property.Name}={number?.ToString() ?? "null"} 必须显式提供 0–{maximum} 整数；不猜魔数/版本/分包值。");
                    headerValid = false;
                }
            }
            if (!headerValid) { config.Streaming.HeaderProfile = null; log("Streaming.HeaderProfile 非法；回退为未校验的宽松接收（与 SAFT 一致），BodyOnly 不受影响。"); }
        }
        config.AutoSelectionWindowMs = Integer(config.AutoSelectionWindowMs, 100, 60000, 1500, "AutoSelectionWindowMs", log);
        config.Stale.EyeMs = Integer(config.Stale.EyeMs, 1, 5000, 250, "Stale.EyeMs", log);
        config.Stale.FaceMs = Integer(config.Stale.FaceMs, 1, 5000, 250, "Stale.FaceMs", log);
        config.Stale.NeutralPupilMillimeters = Number(config.Stale.NeutralPupilMillimeters, 1, 10, 3.5f, "Stale.NeutralPupilMillimeters", log);
        foreach (var property in typeof(EyeGainSettings).GetProperties())
            property.SetValue(config.EyeGain, Number((float)property.GetValue(config.EyeGain)!, 0, 10, 1, "EyeGain." + property.Name, log));
        foreach (var property in typeof(PostProcessingSettings).GetProperties())
        {
            var rule = property.GetValue(config.PostProcessing);
            if (rule is null) { rule = Activator.CreateInstance(property.PropertyType)!; property.SetValue(config.PostProcessing, rule); log($"PostProcessing.{property.Name} 不能为 null；保持关闭。"); Disable(rule); }
            var path = "PostProcessing." + property.Name;
            if (!ValidateRule(rule, path, log)) { Disable(rule); log($"{path} 非法；仅禁用此规则，中性映射保留。"); }
            if ((bool?)rule.GetType().GetProperty("Enabled")?.GetValue(rule) == true)
                log($"启用 {path}：" + string.Join(", ", rule.GetType().GetProperties().Where(p => p.Name != "Enabled").Select(p => p.Name + "=" + Format(p.GetValue(rule)))));
        }
        return config;
    }

    internal static void Disable(object rule) => rule.GetType().GetProperty("Enabled")?.SetValue(rule, false);

    private static bool ValidateRule(object rule, string path, Action<string> log)
    {
        var valid = true;
        void Error(string field, string reason) { log($"{path}.{field}：{reason}"); valid = false; }
        foreach (var property in rule.GetType().GetProperties())
        {
            var value = property.GetValue(rule);
            var name = property.Name;
            if (name == "Enabled" || value is bool) continue;
            if (value is null) { Error(name, "不能为 null。"); continue; }
            if (name == "Gains")
            {
                if (value is not IDictionary gains) { Error(name, "必须为通道倍率对象。"); continue; }
                var keys = new HashSet<ExpressionChannel>();
                var normalized = new Dictionary<string, float>(StringComparer.Ordinal);
                foreach (DictionaryEntry entry in gains)
                {
                    if (entry.Key is not string key || !TryChannel(key, out var channel)) { Error(name + "." + entry.Key, "未知或非表情通道。"); continue; }
                    if (!keys.Add(channel)) Error(name + "." + entry.Key, "通道重复（大小写不敏感）。");
                    var gain = Convert.ToDouble(entry.Value, CultureInfo.InvariantCulture);
                    if (!double.IsFinite(gain) || gain < 0 || gain > 10) Error(name + "." + entry.Key, "倍率必须有限且在 0–10。");
                    else normalized[channel.ToString()] = (float)gain;
                }
                // Runtime uses ordinal channel names; accept old case-insensitive keys only
                // after canonicalizing them. The original JSON is never rewritten.
                if (valid) property.SetValue(rule, normalized);
                continue;
            }
            if (name == "Groups")
            {
                if (value is not IEnumerable groups || value is string) { Error(name, "必须为数组。"); continue; }
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var group in groups)
                {
                    var text = group?.ToString() ?? "";
                    if (text is not ("Smile" or "Frown" or "Stretch" or "UpperUp")) Error(name, $"未知分组 {text}。");
                    if (!seen.Add(text)) Error(name, $"分组重复 {text}。");
                }
                continue;
            }
            if (name == "Method") { if (value.ToString() is not ("Max" or "Average")) Error(name, $"未知方法 {value}。"); continue; }
            if (value is Enum enumeration) { if (!Enum.IsDefined(enumeration.GetType(), enumeration)) Error(name, "未知枚举。"); continue; }
            if (value is not (float or double or int)) { Error(name, "没有可识别的校验语义。"); continue; }
            var number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            var (min, max) = name switch
            {
                "Gain" or "DefaultGain" => (0d, 10d),
                "Slope" => (0.1d, 40d), "Gamma" => (0.1d, 5d),
                "HoldMs" or "MouthSmoothingMs" => (0d, 500d),
                _ => (0d, 1d)
            };
            if (!double.IsFinite(number) || number < min || number > max || (name == "Range" && number <= 0))
                Error(name, $"值 {number} 非法；范围 {min}–{max}" + (name == "Range" ? " 且必须 >0。" : "。"));
        }
        var range = rule.GetType().GetProperty("Range");
        var threshold = rule.GetType().GetProperty("Threshold");
        if (range is not null && threshold is not null && Convert.ToDouble(range.GetValue(rule)) + Convert.ToDouble(threshold.GetValue(rule)) > 1.0000001)
            Error("Range", "Threshold + Range 必须 ≤1。");
        return valid;
    }

    internal static bool TryChannel(string name, out ExpressionChannel channel) =>
        Enum.TryParse(name, true, out channel) && Enum.GetNames<ExpressionChannel>().Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase));

    private static int Integer(int value, int min, int max, int fallback, string path, Action<string> log)
    { if (value >= min && value <= max) return value; log($"{path}={value} 超出 {min}–{max}；使用 {fallback}。"); return fallback; }
    private static float Number(float value, float min, float max, float fallback, string path, Action<string> log)
    { if (float.IsFinite(value) && value >= min && value <= max) return value; log($"{path}={value} 非有限或超出 {min}–{max}；使用 {fallback}。"); return fallback; }
    private static string? Address(string? value, string? fallback, string path, Action<string> log)
    {
        if (value is null && fallback is null) return null;
        if (IPAddress.TryParse(value, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork && !IsMulticast(ip) &&
            (fallback is not null || !ip.Equals(IPAddress.Any))) return ip.ToString();
        log($"{path}={value ?? "null"} 不是有效单播 IPv4 地址；使用 {fallback ?? "自动发现"}。"); return fallback;
    }
    private static bool IsMulticast(IPAddress ip) => ip.GetAddressBytes()[0] >= 224 || ip.Equals(IPAddress.Broadcast);
    private static string Format(object? value) => value is IDictionary dictionary
        ? "{" + string.Join(", ", dictionary.Keys.Cast<object>().Select(key => key + ":" + dictionary[key])) + "}"
        : value is IEnumerable list and not string ? "[" + string.Join(", ", list.Cast<object>()) + "]" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
}
