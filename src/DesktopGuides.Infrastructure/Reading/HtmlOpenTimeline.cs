using System.Text;
using System.Text.Json;

namespace DesktopGuides.Infrastructure.Reading;

/// <summary>
/// Test gates only (#102): the stages the latest HTML open reached and when,
/// so a test that sees "Loading guide…" for too long can tell which await
/// never returned.
/// </summary>
public sealed class HtmlOpenTimeline(Func<long> clockMs)
{
    private readonly List<(string Stage, long Ms, string? Detail)> stages = [];
    // Every finished open in the process, so a test can time the opens that
    // finished during one scenario.
    private readonly List<long> ready = [];
    private long began;
    private int opens;

    public void Begin()
    {
        opens++;
        began = clockMs();
        stages.Clear();
    }

    public void Mark(string stage, string? detail = null)
    {
        long ms = clockMs() - began;
        stages.Add((stage, ms, detail));
        if (stage == "ready") ready.Add(ms);
    }

    public string ToJson()
    {
        using MemoryStream buffer = new();
        using (Utf8JsonWriter json = new(buffer))
        {
            json.WriteStartObject();
            json.WriteNumber("opens", opens);
            json.WriteStartArray("stages");
            foreach ((string stage, long ms, string? detail) in stages)
            {
                json.WriteStartObject();
                json.WriteString("stage", stage);
                json.WriteNumber("ms", ms);
                if (detail is not null) json.WriteString("detail", detail);
                json.WriteEndObject();
            }
            json.WriteEndArray();
            json.WriteStartArray("ready");
            foreach (long ms in ready) json.WriteNumberValue(ms);
            json.WriteEndArray();
            json.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
