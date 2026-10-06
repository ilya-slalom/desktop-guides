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
    // Segmented's handler moves the focus after this one. Selecting here
    // would move focus to the new item first and make Segmented skip one,
    // so the focused item is selected once the key has been handled.
    private void SelectWithArrows(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key is not (VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down))
        {
            return;
        }
        DispatcherQueue.TryEnqueue(() =>
        {
            if (XamlRoot is not null &&
                FocusManager.GetFocusedElement(XamlRoot) is BoundedChoiceItem focused &&
                IndexFromContainer(focused) is int index and >= 0)
            {
                SelectedIndex = index;
            }
        });
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
