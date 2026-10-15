using DesktopGuides.Core.Reading;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.System;

namespace DesktopGuides.Production;

// T18.1: the reader's find bar. It reports intent; the shell searches.
public sealed partial class FindBar : UserControl
{
    public FindBar() => InitializeComponent();

    public event EventHandler<FindDirection>? SearchRequested;
    public event EventHandler? BackRequested;
    public event EventHandler? CloseRequested;

    public string Query => QueryBox.Text;

    public void FocusQuery()
    {
        QueryBox.Focus(FocusState.Keyboard);
        QueryBox.SelectAll();
    }

    public void Reset()
    {
        QueryBox.Text = string.Empty;
        Count.Text = string.Empty;
        Note.Text = string.Empty;
        Note.Visibility = Visibility.Collapsed;
    }

    // A step that waits (a PDF scan still reaching its match) says so, so
    // the last query's count never stands in for the new one's.
    public void ShowSearching()
    {
        Count.Text = "Searching\u2026";
        Note.Text = string.Empty;
        Note.Visibility = Visibility.Collapsed;
    }

    public void ShowResult(FindResult result, string query)
    {
        Count.Text = result.Outcome switch
        {
            FindOutcome.Found => $"{result.Index} of {result.Total}{(result.TotalIsPartial ? "+" : "")}",
            FindOutcome.Failed => "Couldn't search this guide",
            _ => "No matches"
        };
        string? note = FindResultText.Note(result);
        Note.Text = note ?? string.Empty;
        Note.Visibility = note is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void QueryKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (args.Key == VirtualKey.Enter)
        {
            bool shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            SearchRequested?.Invoke(this, shift ? FindDirection.Previous : FindDirection.Next);
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            args.Handled = true;
        }
    }

    private void PreviousClicked(object sender, RoutedEventArgs args) =>
        SearchRequested?.Invoke(this, FindDirection.Previous);

    private void NextClicked(object sender, RoutedEventArgs args) =>
        SearchRequested?.Invoke(this, FindDirection.Next);

    private void BackClicked(object sender, RoutedEventArgs args) =>
        BackRequested?.Invoke(this, EventArgs.Empty);

    private void CloseClicked(object sender, RoutedEventArgs args) =>
        CloseRequested?.Invoke(this, EventArgs.Empty);
}
