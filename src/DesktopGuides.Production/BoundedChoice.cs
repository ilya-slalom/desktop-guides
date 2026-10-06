using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed record BoundedChoiceOption(string Label, string AutomationId);

// A Toolkit Segmented bound to data, so each item is a generated container.
// T13.1 found that items declared in XAML exposed no SelectionItem pattern.
public partial class BoundedChoice : Segmented
{
    private int lastSelectedIndex = -1;

    public BoundedChoice()
    {
        SelectionChanged += KeepOneSelected;
    }

    public void SetOptions(IReadOnlyList<BoundedChoiceOption> options) =>
        ItemsSource = options;

    public Control? ContainerAt(int index) => ContainerFromIndex(index) as Control;

    protected override void PrepareContainerForItemOverride(DependencyObject element, object item)
    {
        base.PrepareContainerForItemOverride(element, item);
        if (element is ContentControl container && item is BoundedChoiceOption option)
        {
            container.Content = option.Label;
            AutomationProperties.SetName(container, option.Label);
            AutomationProperties.SetAutomationId(container, option.AutomationId);
        }
    }

    // Ctrl+Space can clear a ListViewBase selection; a choice always has one.
    private void KeepOneSelected(object sender, SelectionChangedEventArgs args)
    {
        if (SelectedIndex >= 0)
        {
            lastSelectedIndex = SelectedIndex;
            return;
        }
        if (lastSelectedIndex >= 0)
        {
            int restore = lastSelectedIndex;
            DispatcherQueue.TryEnqueue(() => SelectedIndex = restore);
        }
    }
}
