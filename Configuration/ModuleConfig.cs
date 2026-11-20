using UnifiedPicoModule.Core;

namespace UnifiedPicoModule.Configuration;

public sealed class ModuleConfig
{
    public int ConfigVersion { get; set; } = 1;
    public SourceSelection Source { get; set; } = SourceSelection.Auto;
    public bool DisableEyeTracking { get; set; }
    public bool DisableFaceTracking { get; set; }
    public BridgeSettings Bridge { get; set; } = new();
    public StreamingSettings Streaming { get; set; } = new();
    public TongueSettings Tongue { get; set; } = new();
    public EyeGainSettings EyeGain { get; set; } = new();
    public StaleSettings Stale { get; set; } = new();
    public bool AllowFallback { get; set; }
    public int AutoSelectionWindowMs { get; set; } = 1500;
    public bool LegacyExpressionCeiling { get; set; }
    public PostProcessingSettings PostProcessing { get; set; } = new();
    public MigrationMetadata? Migration { get; set; }
}
public sealed class TongueSettings
{
    public bool Enabled { get; set; } = true;
    public int Port { get; set; } = 45682;
}
public sealed class BridgeSettings
{
    public string? IP { get; set; }
    public string ListenAddress { get; set; } = "0.0.0.0";
    public InputProtocol? Protocol { get; set; }
    public int ReconnectMs { get; set; } = 2000;
    public int ControlTimeoutMs { get; set; } = 30000;
}
public sealed class StreamingSettings
{
    public string ListenAddress { get; set; } = "0.0.0.0";
    public bool LocalOnly { get; set; } = true;
    public string? IP { get; set; }
    public InputProtocol? Protocol { get; set; }
    public int SessionTimeoutMs { get; set; } = 2000;
    public StreamingHeaderSettings? HeaderProfile { get; set; }
}
// No invented wire magic/version defaults. All six fields must be supplied explicitly
// from known sender documentation/capture before Header16 decoding is allowed.
public sealed class StreamingHeaderSettings
{
    public int? StartCode1 { get; set; }
    public int? StartCode2 { get; set; }
    public int? SubType { get; set; }
    public int? MultiPack { get; set; }
    public int? CurrentIndex { get; set; }
    public int? Version { get; set; }
}
public sealed class EyeGainSettings
{
    public float LeftX { get; set; } = 1;
    public float LeftY { get; set; } = 1;
    public float RightX { get; set; } = 1;
    public float RightY { get; set; } = 1;
    public float LeftOpenness { get; set; } = 1;
    public float RightOpenness { get; set; } = 1;
}
public sealed class StaleSettings
{
    public bool NeutralizeOnTimeout { get; set; } = false;
    public int EyeMs { get; set; } = 250;
    public int FaceMs { get; set; } = 250;
    public float NeutralPupilMillimeters { get; set; } = 3.5f;
}
public sealed class MigrationMetadata
{
    public string SourceFile { get; set; } = "";
    public string SourceVersion { get; set; } = "";
    public string ImportedAtUtc { get; set; } = "";
    public List<string> Differences { get; set; } = [];
}
