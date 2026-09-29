using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

// A layout-neutral host that appears in UI Automation as a group, so its
// AutomationId is reachable. Toolkit's MetadataControl is sealed and creates
// no automation peer, so an AutomationId set on it never reaches the tree.
public sealed partial class AutomationGroup : Grid
{
    protected override AutomationPeer OnCreateAutomationPeer() => new GroupPeer(this);

    private sealed partial class GroupPeer(AutomationGroup owner) : FrameworkElementAutomationPeer(owner)
    {
        protected override AutomationControlType GetAutomationControlTypeCore() =>
            AutomationControlType.Group;

        protected override string GetClassNameCore() => nameof(AutomationGroup);
    }
}
