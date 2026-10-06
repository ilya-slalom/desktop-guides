using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
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

    protected override DependencyObject GetContainerForItemOverride() => new BoundedChoiceItem();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is BoundedChoiceItem;

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

// Exposes SelectionItem on each generated container (the T14.2 gate fallback).
public partial class BoundedChoiceItem : SegmentedItem
{
    protected override AutomationPeer OnCreateAutomationPeer() => new BoundedChoiceItemPeer(this);
}

internal sealed partial class BoundedChoiceItemPeer(BoundedChoiceItem owner)
    : ListViewItemAutomationPeer(owner), ISelectionItemProvider
{
    public bool IsSelected => owner.IsSelected;

    public IRawElementProviderSimple? SelectionContainer =>
        ItemsControl.ItemsControlFromItemContainer(owner) is { } choice &&
        FrameworkElementAutomationPeer.FromElement(choice) is { } peer
            ? ProviderFromPeer(peer)
            : null;

    public void Select() => owner.IsSelected = true;

    public void AddToSelection() => owner.IsSelected = true;

    // A choice always has one selected item.
    public void RemoveFromSelection() { }

    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.SelectionItem ? this : base.GetPatternCore(patternInterface);
}
