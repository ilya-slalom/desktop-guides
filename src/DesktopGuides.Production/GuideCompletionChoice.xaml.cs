using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed record GuideCompletionRequest(Guid GuideId, string Title, bool Complete);

// Shows a guide's committed completion and raises a request when the user
// changes it. It holds no storage logic.
public sealed partial class GuideCompletionChoice : UserControl
{
    private const int InProgressIndex = 0;
    private const int CompleteIndex = 1;

    private bool showing;
    private bool busy;
    private bool shownComplete;
    private bool pendingComplete;
    private string title = string.Empty;

    public GuideCompletionChoice()
    {
        InitializeComponent();
        Choice.SetOptions(
        [
            new BoundedChoiceOption(GuideCompletionPresentation.InProgressLabel, "CompletionInProgress"),
            new BoundedChoiceOption(GuideCompletionPresentation.CompleteLabel, "CompletionComplete"),
        ]);
        Choice.ChoiceChanged += ChoiceChanged;
    }

    // Raised only by a user change of selection.
    public event EventHandler<GuideCompletionRequest>? CompletionRequested;

    public Guid? GuideId { get; private set; }

    // Shows the committed state without raising CompletionRequested.
    public void Show(Guid guideId, string guideTitle, bool complete)
    {
        GuideId = guideId;
        title = guideTitle;
        shownComplete = complete;
        AutomationProperties.SetName(Choice, GuideCompletionPresentation.ChoiceName(guideTitle));
        Select(complete);
        Visibility = Visibility.Visible;
    }

    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        GuideId = null;
    }

    // Puts the selection back to the last committed state.
    public void Revert() => Select(shownComplete);

    // Busy keeps focus where it is: pointer input is ignored, and a keyboard
    // change snaps back to the pending choice.
    public void SetBusy(bool value)
    {
        busy = value;
        pendingComplete = Choice.SelectedIndex == CompleteIndex;
        Choice.IsHitTestVisible = !value;
    }

    public void FocusSelection()
    {
        int index = shownComplete ? CompleteIndex : InProgressIndex;
        if (Choice.ContainerAt(index)?.Focus(FocusState.Programmatic) != true)
        {
            // The panel was just shown; its containers exist after layout.
            DispatcherQueue.TryEnqueue(
                () => Choice.ContainerAt(index)?.Focus(FocusState.Programmatic));
        }
    }

    private void Select(bool complete)
    {
        showing = true;
        try
        {
            Choice.SelectedIndex = complete ? CompleteIndex : InProgressIndex;
        }
        finally
        {
            showing = false;
        }
    }

    private void ChoiceChanged(object? sender, EventArgs args)
    {
        if (showing)
        {
            return;
        }
        bool complete = Choice.SelectedIndex == CompleteIndex;
        if (busy)
        {
            if (complete != pendingComplete)
            {
                DispatcherQueue.TryEnqueue(() => Select(pendingComplete));
            }
            return;
        }
        if (complete == shownComplete || GuideId is not Guid guideId)
        {
            return;
        }
        CompletionRequested?.Invoke(this, new GuideCompletionRequest(guideId, title, complete));
    }
}
