using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Online;
using DesktopGuides.Infrastructure.Providers;
using DesktopGuides.Infrastructure.Storage;

namespace DesktopGuides.Infrastructure;

// T17.4: source-generated JSON, so a trimmed build keeps every member it
// writes and reads. Callers that used web defaults pass those options in.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(GameMetadataSnapshot))]
[JsonSerializable(typeof(FileOperationManifestJson))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(FixtureProviderHandler.FixtureManifest))]
[JsonSerializable(typeof(List<FixtureProviderHandler.UnmatchedRequest>))]
[JsonSerializable(typeof(CaptureFile))]
[JsonSerializable(typeof(OnlineFixtureManifest))]
internal sealed partial class InfrastructureJsonContext : JsonSerializerContext;
