using DesktopGuides.Core.Library;
using DesktopGuides.Core.Reading;
using DesktopGuides.Core.Text;
using Microsoft.UI.Xaml;

namespace DesktopGuides.Production;

public sealed partial class ShellWindow
{
    // T19.2: a TXT guide's Original or Reflow view, chosen and saved per guide.
    private const string TextViewSaveFailed = "Couldn't save the view for this guide.";
    private bool settingTextView;
    private bool switchingTextView;
    private Guid? textViewGuide;

    private void InitializeTextView()
    {
        ReaderTextView.SetOptions(
        [
            new BoundedChoiceOption("Original", "ReaderTextViewOriginal"),
            new BoundedChoiceOption("Reflow", "ReaderTextViewReflow")
        ]);
        ShowTextViewChoice(TextViewMode.Original);
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
    }

    private async Task ChooseTextViewAsync(TextViewMode mode)
    {
        if (readerSession is not TextReaderSession session || textViewGuide is not Guid guide || switchingTextView)
        {
            return;
        }
        switchingTextView = true;
        try
        {
            bool shown = await session.SetViewAsync(mode, CancellationToken.None);
            if (!ReferenceEquals(session, readerSession)) return;
            if (!shown)
            {
                ShowTextViewChoice(TextViewMode.Original);
                ShowWarningStatus(TextViewMessages.NoProse);
                return;
            }
            try
            {
                await RequireRepository().SaveTextViewAsync(guide, mode);
            }
            catch (Exception)
            {
                ShowErrorStatus(TextViewSaveFailed);
                return;
            }
            AnnounceStatus(TextViewMessages.For(mode));
        }
        finally
        {
            switchingTextView = false;
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
