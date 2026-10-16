using DesktopGuides.Core.Library;

namespace DesktopGuides.Core.Text;

// T19.2: a search match's source range as display ranges, one per block it
// touches, for the reflow view's highlight.
public static class ReflowHighlights
{
    public static IReadOnlyList<(int Block, int Start, int Length)> For(ReflowPlan plan, int start, int length)
    {
        ArgumentNullException.ThrowIfNull(plan);
        List<(int Block, int Start, int Length)> ranges = [];
        if (length <= 0 || plan.Blocks.Count == 0) return ranges;
        int end = start + length;
        for (int index = plan.BlockAt(start); index < plan.Blocks.Count && plan.Blocks[index].Start < end; index++)
        {
            ReflowBlock block = plan.Blocks[index];
            int from = block.DisplayIndexOf(Math.Max(start, block.Start));
            int to = block.DisplayIndexOf(Math.Min(end, block.End));
            if (to > from) ranges.Add((index, from, to - from));
        }
        return ranges;
    }
}

public static class TextViewMessages
{
    public const string NoProse = "This guide has no prose to reflow.";

    public static string For(TextViewMode mode) => mode == TextViewMode.Reflow ? "Reflow view." : "Original view.";
}
