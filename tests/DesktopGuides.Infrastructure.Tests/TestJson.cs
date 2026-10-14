using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace DesktopGuides.Infrastructure.Tests;

// Reflection JSON is off in this project; test code that builds JSON on
// purpose opts back in through this explicit resolver.
internal static class TestJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        TypeInfoResolver = new DefaultJsonTypeInfoResolver()
    };
}
