using DesktopGuides.Core.Library;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class GameEditorDialog : ContentDialog
{
    private readonly Func<GameDetails, Task> save;
    private bool saving;

    public GameEditorDialog(Game? game, Func<GameDetails, Task> save)
    {
        InitializeComponent();
        this.save = save;
        Title = game is null ? "Add game" : "Edit game";
        PrimaryButtonText = game is null ? "Add game" : "Save changes";
        if (game is not null)
        {
            GameTitleInput.Text = game.Title;
            GamePlatformInput.Text = game.Platform ?? string.Empty;
            GameNotesInput.Text = game.Notes ?? string.Empty;
        }
        Opened += (_, _) => GameTitleInput.Focus(FocusState.Programmatic);
        UpdateValidation();
    }

    private void InputChanged(object sender, TextChangedEventArgs args)
    {
        if (SaveError is null)
        {
            return;
        }
        SaveError.IsOpen = false;
        UpdateValidation();
    }

    private void UpdateValidation()
    {
        if (GameTitleInput is null || GamePlatformInput is null ||
            GameNotesInput is null || TitleFeedback is null ||
            PlatformFeedback is null || NotesFeedback is null)
        {
            return;
        }
        string title = GameTitleInput.Text.Trim();
        string platform = GamePlatformInput.Text.Trim();
        string notes = GameNotesInput.Text.Trim();
        TitleFeedback.Text = title.Length == 0
            ? "Enter a title to continue."
            : $"{title.Length} / {GameDetails.TitleLimit} characters";
        PlatformFeedback.Text =
            $"{platform.Length} / {GameDetails.PlatformLimit} characters";
        NotesFeedback.Text =
            $"{notes.Length} / {GameDetails.NotesLimit} characters";
        IsPrimaryButtonEnabled =
            !saving &&
            title.Length is > 0 and <= GameDetails.TitleLimit &&
            platform.Length <= GameDetails.PlatformLimit &&
            notes.Length <= GameDetails.NotesLimit;
    }

    private async void SaveClicked(
        ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        var deferral = args.GetDeferral();
        saving = true;
        UpdateValidation();
        try
        {
            GameDetails details = GameDetails.Create(
                GameTitleInput.Text, GamePlatformInput.Text, GameNotesInput.Text);
            await save(details);
        }
        catch (Exception error)
        {
            args.Cancel = true;
            SaveError.Message = $"Could not save this game: {error.Message}";
            SaveError.IsOpen = true;
        }
        finally
        {
            saving = false;
            UpdateValidation();
            deferral.Complete();
        }
    }
}
