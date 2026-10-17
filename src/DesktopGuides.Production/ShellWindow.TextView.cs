using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // T19.2: a TXT guide's Original or Reflow view, chosen and saved per guide.
    private const string TextViewSaveFailed = "Couldn't save the view for this guide.";
    private bool settingTextView;
    // The session a switch is running for, and the view last chosen: a
    // choice made during a switch is applied when the switch ends.
    private TextReaderSession? switchingTextView;
    private TextViewMode chosenTextView;
    private Guid? textViewGuide;
    // The narrowest toolbar row known to hold the choice beside the toolbar.
    private double textViewBesideWidth;

    private void InitializeTextView()
    {
        ReaderTextView.SetOptions(
        [
            new BoundedChoiceOption("Original", "ReaderTextViewOriginal"),
            new BoundedChoiceOption("Reflow", "ReaderTextViewReflow")
        ]);
        ShowTextViewChoice(TextViewMode.Original);
        ReaderActions.OverflowChanged += (_, _) => PlaceTextViewChoice();
        ReaderToolbarRow.SizeChanged += (_, _) => PlaceTextViewChoice();
        ReaderTextView.ChoiceChanged += (_, _) =>
        {
            if (settingTextView) return;
            _ = ChooseTextViewAsync(ReaderTextView.SelectedIndex == 1 ? TextViewMode.Reflow : TextViewMode.Original);
        };
    }

    // Sets the choice without acting on it.
    private void ShowTextViewChoice(TextViewMode mode)
    {
        settingTextView = true;
        try
        {
            ReaderTextView.SelectedIndex = mode == TextViewMode.Reflow ? 1 : 0;
        }
        finally
        {
            settingTextView = false;
        }
    }

    // The choice sits beside the toolbar while every command still fits. If
    // a command overflows, the choice takes its own row, left-aligned, until
    // the row is wider than where that happened.
    private void PlaceTextViewChoice()
    {
        bool beside = Grid.GetRow(ReaderTextView) == 0;
        double width = ReaderToolbarRow.ActualWidth;
        if (beside && ReaderTextView.Visibility == Visibility.Visible && ReaderActions.HasOverflow)
        {
            textViewBesideWidth = Math.Max(textViewBesideWidth, width + 1);
            MoveTextViewChoice(below: true);
        }
        else if (!beside && width >= textViewBesideWidth)
        {
            MoveTextViewChoice(below: false);
        }
    }

    private void MoveTextViewChoice(bool below)
    {
        Grid.SetColumnSpan(ReaderActions, below ? 2 : 1);
        Grid.SetRow(ReaderTextView, below ? 1 : 0);
        Grid.SetColumn(ReaderTextView, below ? 0 : 1);
        Grid.SetColumnSpan(ReaderTextView, below ? 2 : 1);
        ReaderTextView.HorizontalAlignment = below ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
        ReaderTextView.Margin = below ? default : new Thickness(12, 0, 0, 0);
    }

    private void HideTextViewChoice()
    {
        ReaderTextView.Visibility = Visibility.Collapsed;
        textViewGuide = null;
    }

    private async Task<TextViewMode> ReadTextViewAsync(Guid guideId)
    {
        try
        {
            return (await RequireRepository().GetReaderPreferencesAsync(guideId))?.TextView ?? TextViewMode.Original;
        }
        catch (Exception)
        {
            // An unreadable preference is Original; reading goes on.
            return TextViewMode.Original;
        }
    }

    // Called once the TXT session exists, before its surface shows. A saved
    // Reflow with no plan opens in Original, quietly.
    private async Task StartTextViewAsync(Guid guideId, TextReaderSession session, CancellationToken token)
    {
        textViewGuide = guideId;
        session.ViewChanged += OnTextViewChanged;
        if (await ReadTextViewAsync(guideId) == TextViewMode.Reflow)
        {
            await session.SetViewAsync(TextViewMode.Reflow, token);
        }
        ShowTextViewChoice(session.Mode);
        ReaderTextView.Visibility = (session.Capabilities & ReaderCapabilities.Reflow) != 0
            ? Visibility.Visible
            : Visibility.Collapsed;
        PlaceTextViewChoice();
    }

    private async Task ChooseTextViewAsync(TextViewMode mode)
    {
        if (readerSession is not TextReaderSession session || textViewGuide is not Guid guide) return;
        chosenTextView = mode;
        if (ReferenceEquals(switchingTextView, session)) return;
        switchingTextView = session;
        try
        {
            while (true)
            {
                mode = chosenTextView;
                bool shown = await session.SetViewAsync(mode, CancellationToken.None);
                if (!ReferenceEquals(session, readerSession)) return;
                if (!shown)
                {
                    ShowTextViewChoice(TextViewMode.Original);
                    ShowWarningStatus(TextViewMessages.NoProse);
                    return;
                }
                if (chosenTextView != mode) continue;
                try
                {
                    await RequireRepository().SaveTextViewAsync(guide, mode);
                }
                catch (Exception)
                {
                    ShowErrorStatus(TextViewSaveFailed);
                    return;
                }
                if (!ReferenceEquals(session, readerSession)) return;
                if (chosenTextView != mode) continue;
                AnnounceStatus(TextViewMessages.For(mode));
                return;
            }
        }
        finally
        {
            if (ReferenceEquals(switchingTextView, session)) switchingTextView = null;
            if (ReferenceEquals(session, readerSession)) ShowTextViewChoice(session.Mode);
        }
    }

    private void OnTextViewChanged(object? sender, EventArgs args)
    {
        if (sender is TextReaderSession session && ReferenceEquals(session, readerSession))
        {
            ReaderSurface.Content = session.Surface;
        }
    }
}
