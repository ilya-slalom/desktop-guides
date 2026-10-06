using CommunityToolkit.WinUI.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DesktopGuides.Production;

public sealed record BoundedChoiceOption(string Label, string AutomationId);

// A Toolkit Segmented bound to data, so each item is a generated container.
// T13.1 found that items declared in XAML exposed no SelectionItem pattern.
public partial class BoundedChoice : Segmented
{
    private int lastSelectedIndex = -1;
    private bool applyingTemplate;

    public BoundedChoice()
    {
        SelectionChanged += KeepOneSelected;
        // Added before Segmented's own handler, which only moves focus.
        PreviewKeyDown += SelectWithArrows;
    }

    // Raised when the selected option changes, but not when the template
    // reset or a cleared selection briefly moves SelectedIndex.
    public event EventHandler? ChoiceChanged;

    public void SetOptions(IReadOnlyList<BoundedChoiceOption> options) =>
        ItemsSource = options;

    public Control? ContainerAt(int index) => ContainerFromIndex(index) as Control;

    // Segmented's first template pass resets SelectedIndex to the first
    // index it ever held, which can be an older choice than the one shown.
    protected override void OnApplyTemplate()
    {
        int selected = SelectedIndex;
        applyingTemplate = true;
        try
        {
            base.OnApplyTemplate();
            if (selected >= 0)
            {
                SelectedIndex = selected;
            }
        }
        finally
        {
            applyingTemplate = false;
        }
    }

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

    // The arrow keys move the selection with focus, as in a radio group.
    // Focus alone never selects, so tabbing in can't change the choice.
    private void SelectWithArrows(object sender, KeyRoutedEventArgs args)
    {
        bool rightToLeft = FlowDirection == FlowDirection.RightToLeft;
        int step = args.Key switch
        {
            VirtualKey.Left => rightToLeft ? 1 : -1,
            VirtualKey.Right => rightToLeft ? -1 : 1,
            VirtualKey.Up => -1,
            VirtualKey.Down => 1,
            _ => 0
        };
        if (step == 0 || XamlRoot is null ||
            FocusManager.GetFocusedElement(XamlRoot) is not BoundedChoiceItem focused)
        {
            return;
        }
        int index = IndexFromContainer(focused) + step;
        if (index < 0 || index >= Items.Count || ContainerFromIndex(index) is not BoundedChoiceItem next)
        {
            return;
        }
        args.Handled = true;
        SelectedIndex = index;
        next.Focus(FocusState.Keyboard);
    }

    // Ctrl+Space can clear a ListViewBase selection; a choice always has one.
    private void KeepOneSelected(object sender, SelectionChangedEventArgs args)
    {
        if (applyingTemplate)
        {
            return;
        }
        if (SelectedIndex >= 0)
        {
            bool changed = SelectedIndex != lastSelectedIndex;
            lastSelectedIndex = SelectedIndex;
            if (changed)
            {
                ChoiceChanged?.Invoke(this, EventArgs.Empty);
            }
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
