using System.Diagnostics;
using System.Text.Json;
using DesktopGuides.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DesktopGuides.App.Probes;

public sealed class TextProbe : IReaderProbe
{
    private readonly ListView list;
    private TextGuideDocument? document;
    private List<TextLine> lines = [];
    private long openMilliseconds;
    private string restoreKind = "none";

    public TextProbe()
    {
        list = new ListView
        {
            SelectionMode = ListViewSelectionMode.None,
            IsItemClickEnabled = false,
            ItemTemplate = (DataTemplate)Application.Current.Resources["TextLineTemplate"],
            ItemsPanel = (ItemsPanelTemplate)Application.Current.Resources["TextItemsPanel"],
            FontSize = 16
        };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Auto);
        ScrollViewer.SetHorizontalScrollMode(list, ScrollMode.Enabled);
    }

    public FrameworkElement View => list;

    public string Diagnostics
    {
        get
        {
            int firstVisible = FindItemsPanel(list)?.FirstVisibleIndex ?? -1;
            return $"{lines.Count} lines; first visible {firstVisible + 1}; " +
                $"{CountVisualItems(list)} realized visuals; prepare {openMilliseconds} ms; " +
                $"restore {restoreKind}.";
        }
    }

    public async Task OpenAsync(string path, int? codePage)
    {
        Stopwatch timer = Stopwatch.StartNew();
        document = await Task.Run(() => TextGuideDocument.Decode(File.ReadAllBytes(path), codePage));
        lines = new List<TextLine>(document.LineStarts.Count);
        for (int index = 0; index < document.LineStarts.Count; index++)
        {
            int start = document.LineStarts[index];
            int end = index + 1 < document.LineStarts.Count
                ? document.LineStarts[index + 1] - 1
                : document.Text.Length;
            lines.Add(new TextLine(document.Text[start..end]));
        }
        list.ItemsSource = lines;
        timer.Stop();
        openMilliseconds = timer.ElapsedMilliseconds;
    }

    public Task<string> CaptureAsync()
    {
        TextGuideDocument activeDocument = document ??
            throw new InvalidOperationException("Open a text guide first.");
        int firstVisible = Math.Clamp(FindItemsPanel(list)?.FirstVisibleIndex ?? 0, 0, lines.Count - 1);
        TextLocation location = activeDocument.Capture(activeDocument.LineStarts[firstVisible]);
        return Task.FromResult(JsonSerializer.Serialize(location));
    }

    public Task RestoreAsync(string locationJson)
    {
        TextGuideDocument activeDocument = document ??
            throw new InvalidOperationException("Open a text guide first.");
        TextLocation location = JsonSerializer.Deserialize<TextLocation>(locationJson) ??
            throw new InvalidDataException("Invalid text location.");
        TextRestoreResult result = activeDocument.Restore(location);
        int index = activeDocument.LineAtOffset(result.CharacterOffset);
        list.ScrollIntoView(lines[index], ScrollIntoViewAlignment.Leading);
        restoreKind = result.Kind.ToString();
        return Task.CompletedTask;
    }

    public Task ChangeScaleAsync(double factor)
    {
        list.FontSize = Math.Clamp(list.FontSize * factor, 10, 32);
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        list.ItemsSource = null;
        document = null;
        lines = [];
        return ValueTask.CompletedTask;
    }

    private static ItemsStackPanel? FindItemsPanel(DependencyObject root)
    {
        if (root is ItemsStackPanel panel)
        {
            return panel;
        }
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            ItemsStackPanel? found = FindItemsPanel(VisualTreeHelper.GetChild(root, index));
            if (found is not null)
            {
                return found;
            }
        }
        return null;
    }

    private static int CountVisualItems(DependencyObject root)
    {
        int count = root is ListViewItem ? 1 : 0;
        for (int index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            count += CountVisualItems(VisualTreeHelper.GetChild(root, index));
        }
        return count;
    }

    private sealed record TextLine(string Text);
}
