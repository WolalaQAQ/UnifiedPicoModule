using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnifiedPicoModule.Configuration;

public static class ConfigStore
{
    internal static JsonSerializerOptions JsonOptions { get; } = CreateOptions();
    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { WriteIndented = true, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        return options;
    }

    public static ModuleConfig Load(string path, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        if (!File.Exists(path))
        {
            var defaults = ConfigValidator.Validate(new ModuleConfig(), log);
            try { SaveNew(path, defaults); log($"配置不存在，已创建安全默认：{path}"); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { log($"无法创建默认配置：{path}：{ex.Message}"); }
            return defaults;
        }
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            return Parse(document.RootElement, log);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            log($"配置读取失败，使用安全默认且保留原文件：{path}：{ex.Message}");
            return ConfigValidator.Validate(new ModuleConfig(), log);
        }
    }

    // Product validation must not call Load: even an existence precheck races with an
    // editor's atomic save. This entry never creates or rewrites the input path.
    public static ModuleConfig ReadOnly(string path, Action<string> log)
    {
        ArgumentNullException.ThrowIfNull(log);
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return Parse(document.RootElement, log);
    }

    // Validate the JSON shape before deserializing each independent rule. A typo must not
    // turn into a silently enabled default rule; unrelated rules remain usable.
    internal static ModuleConfig Parse(JsonElement root, Action<string> log)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new JsonException("配置根必须为对象。");
        var versions = root.EnumerateObject().Where(p => p.Name == "ConfigVersion").ToArray();
        if (versions.Length != 1 || versions[0].Value.ValueKind != JsonValueKind.Number || !versions[0].Value.TryGetInt32(out var version) || version != 1)
        {
            log("ConfigVersion 缺失、重复或未知；使用版本 1 安全默认，不应用未知语义，不改写原文件。");
            return ConfigValidator.Validate(new ModuleConfig(), log);
        }
        var config = new ModuleConfig();
        Populate(config, root, "", log, false);
        return ConfigValidator.Validate(config, log);
    }

    private static bool Populate(object target, JsonElement json, string prefix, Action<string> log, bool strict)
    {
        if (json.ValueKind != JsonValueKind.Object) { log($"{prefix} 必须为对象。"); return false; }
        var valid = true;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var duplicates = json.EnumerateObject().GroupBy(p => p.Name, StringComparer.Ordinal)
            .Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.Ordinal);
        foreach (var member in json.EnumerateObject())
        {
            var path = prefix.Length == 0 ? member.Name : prefix + "." + member.Name;
            if (!seen.Add(member.Name)) continue;
            if (duplicates.Contains(member.Name)) { log($"{path} 重复，不采用该字段。"); valid = false; continue; }
            var property = target.GetType().GetProperty(member.Name, BindingFlags.Public | BindingFlags.Instance);
            if (property is null || !property.CanWrite) { log($"未知配置键 {path}={member.Value.GetRawText()}，不采用。"); valid = false; continue; }
            if (property.PropertyType == typeof(PostProcessingSettings))
            {
                if (!PopulateRules(config: (PostProcessingSettings)property.GetValue(target)!, member.Value, log)) valid = false;
                continue;
            }
            try
            {
                if (member.Value.ValueKind == JsonValueKind.Null &&
                     (property.PropertyType == typeof(string) || property.PropertyType == typeof(MigrationMetadata) || property.PropertyType == typeof(StreamingHeaderSettings) || Nullable.GetUnderlyingType(property.PropertyType) is not null))
                { property.SetValue(target, null); continue; }
                // Recursively inspect nested objects so diagnostics retain full field paths.
                if (!strict && property.PropertyType.IsClass && property.PropertyType != typeof(string) &&
                    !typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType))
                {
                    var child = Activator.CreateInstance(property.PropertyType)!;
                    if (member.Value.ValueKind == JsonValueKind.Null) throw new JsonException("不能为 null。");
                    Populate(child, member.Value, path, log, false);
                    property.SetValue(target, child);
                }
                else
                {
                    if (strict && HasDuplicateKeys(member.Value, path, log)) { valid = false; continue; }
                    var value = JsonSerializer.Deserialize(member.Value.GetRawText(), property.PropertyType, JsonOptions);
                    if (value is null && property.PropertyType != typeof(string)) throw new JsonException("不能为 null。");
                    property.SetValue(target, value);
                }
            }
            catch (Exception ex) when (ex is JsonException or ArgumentException or TargetInvocationException)
            { log($"{path} 非法：{ex.Message}；不采用该字段。"); valid = false; }
        }
        return valid;
    }

    private static bool HasDuplicateKeys(JsonElement json, string path, Action<string> log)
    {
        var duplicate = false;
        if (json.ValueKind == JsonValueKind.Object)
        {
            foreach (var group in json.EnumerateObject().GroupBy(p => p.Name, StringComparer.Ordinal))
            {
                if (group.Count() > 1) { log($"{path}.{group.Key} 重复；不允许 JSON 后值悄悄覆盖前值。"); duplicate = true; }
                foreach (var entry in group) duplicate |= HasDuplicateKeys(entry.Value, path + "." + entry.Name, log);
            }
        }
        else if (json.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in json.EnumerateArray()) duplicate |= HasDuplicateKeys(item, $"{path}[{index++}]", log);
        }
        return duplicate;
    }

    private static bool PopulateRules(PostProcessingSettings config, JsonElement json, Action<string> log)
    {
        if (json.ValueKind != JsonValueKind.Object)
        {
            foreach (var property in config.GetType().GetProperties())
                ConfigValidator.Disable(property.GetValue(config)!);
            log("PostProcessing 必须为对象；全部规则保持关闭。");
            return false;
        }
        var valid = true;
        foreach (var group in json.EnumerateObject().GroupBy(p => p.Name, StringComparer.Ordinal))
        {
            var property = config.GetType().GetProperty(group.Key);
            var path = "PostProcessing." + group.Key;
            if (property is null) { log($"未知规则 {path}={group.First().Value.GetRawText()}，不采用。"); valid = false; continue; }
            var rule = Activator.CreateInstance(property.PropertyType)!;
            if (group.Count() != 1 || !Populate(rule, group.First().Value, path, log, true))
            { ConfigValidator.Disable(rule); log($"{path} JSON 配置非法；仅禁用此规则。"); valid = false; }
            property.SetValue(config, rule);
        }
        return valid;
    }

    public static void SaveNew(string path, ModuleConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        var messages = new List<string>();
        var safe = ConfigValidator.Validate(config, messages.Add);
        if (messages.Any(m => !m.StartsWith("启用 ", StringComparison.Ordinal)))
            throw new ArgumentException("待保存配置非法，拒绝静默写入修正结果：" + string.Join(" | ", messages));
        var bytes = JsonSerializer.SerializeToUtf8Bytes(safe, JsonOptions);
        // CreateNew is atomic with respect to competing writers, unlike File.Exists + WriteAllText.
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        file.Write(bytes);
        file.Flush(true);
    }
}
