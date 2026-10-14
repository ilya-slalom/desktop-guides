using System.Text.Json.Serialization;
using DesktopGuides.Core.Fixtures;
using DesktopGuides.Core.Html;
using DesktopGuides.Core.Pdf;

namespace DesktopGuides.Core;

// T17.4: source-generated JSON, so a trimmed build keeps every member it
// writes and reads. Names stay camelCase, as the anonymous payloads were.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(FixtureCatalog.FixtureManifest))]
[JsonSerializable(typeof(HtmlFindArgs))]
[JsonSerializable(typeof(HtmlSessionDiagnosticsJson))]
[JsonSerializable(typeof(PdfSessionDiagnostics))]
internal sealed partial class CoreJsonContext : JsonSerializerContext;
