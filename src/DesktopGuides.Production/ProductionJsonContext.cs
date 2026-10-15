using System.Text.Json.Serialization;

namespace DesktopGuides.Production;

// T17.4: source-generated JSON, so a trimmed build keeps every member it
// writes and reads. The names match the anonymous payloads they replace:
// WebView2 DevTools parameters and the test gate's diagnostics files.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(double))]
[JsonSerializable(typeof(DevToolsEvaluateParams))]
[JsonSerializable(typeof(HtmlPositionForTest))]
[JsonSerializable(typeof(ProgressCountsForTest))]
internal sealed partial class ProductionJsonContext : JsonSerializerContext;

internal sealed record DevToolsEvaluateParams(string Expression, bool ReturnByValue, bool UserGesture);

internal sealed record HtmlPositionForTest(
    string? Locator, string? Kind, string? Step, string? Reason,
    string? AppearanceKind, string? AppearanceStep, double? AppearanceScale, int AppearanceRestores,
    bool FindHighlighted);

internal sealed record ProgressCountsForTest(
    int Saves, int SkippedUnchanged, int Failures, int Opens, int OpenFailures,
    ProgressFailureForTest? LastFailure, ProgressFailureContext? FailureContext);

// "hresult", not camelCase's "hResult": the smoke reads the old name.
internal sealed record ProgressFailureForTest(
    string Stage, string Type, [property: JsonPropertyName("hresult")] int HResult,
    string Trigger, long CaptureMs, long WriteMs);

internal sealed record ProgressFailureContext(
    long? GateWaitMs, long? QueueWaitMs, long? WorkMs, int PoolThreads, long PoolPending);
