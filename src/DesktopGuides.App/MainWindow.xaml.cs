using DesktopGuides.App.Probes;
using DesktopGuides.Core.Fixtures;
using Microsoft.UI.Xaml;
using Microsoft.Web.WebView2.Core;

namespace DesktopGuides.App;

public sealed partial class MainWindow : Window
{
    private FixtureCatalog? fixtures;
    private IReaderProbe? probe;
    private string? savedLocation;

    public MainWindow()
    {
        InitializeComponent();
        Title = "Desktop Guides P0";
        EncodingPicker.SelectedIndex = 0;
        try
        {
            fixtures = FixtureCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures"));
            FixturePicker.ItemsSource = fixtures.Entries
                .Where(entry => entry.Format is "txt" or "html" or "pdf")
                .ToList();
            FixturePicker.SelectedIndex = 0;
            try
            {
                _ = CoreWebView2Environment.GetAvailableBrowserVersionString();
                StatusText.Text = $"{FixturePicker.Items.Count} guide fixtures verified.";
            }
            catch (Exception)
            {
                StatusText.Text = $"{FixturePicker.Items.Count} guide fixtures verified. " +
                    "HTML requires Microsoft Edge WebView2 Runtime; install or repair it to read HTML guides. " +
                    "TXT and PDF remain available.";
            }
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Fixtures unavailable: {exception.Message}";
        }
    }

    private async void OpenClicked(object sender, RoutedEventArgs e)
    {
        if (fixtures is null || FixturePicker.SelectedItem is not FixtureEntry entry)
        {
            StatusText.Text = "Select a valid fixture.";
            return;
        }

        if (probe is not null)
        {
            ReaderHost.Content = null;
            await probe.DisposeAsync();
            probe = null;
        }

        IReaderProbe? nextProbe = entry.Format switch
        {
            "txt" => new TextProbe(),
            "html" => new HtmlProbe(fixtures.Entries.Select(item => fixtures.Resolve(item.Id))),
            "pdf" when Environment.GetEnvironmentVariable(
                "DESKTOP_GUIDES_PDF_CANDIDATE") == "native" => new PdfTextCandidateProbe(),
            "pdf" when Environment.GetEnvironmentVariable(
                "DESKTOP_GUIDES_PDF_CANDIDATE") == "web" => new PdfWebCandidateProbe(),
            "pdf" => new PdfProbe(),
            _ => null
        };
        if (nextProbe is null)
        {
            StatusText.Text = $"{entry.Format.ToUpperInvariant()} probe is being implemented.";
            return;
        }

        StatusText.Text = $"Opening {entry.Id}…";
        try
        {
            ReaderHost.Content = nextProbe.View;
            int? codePage = EncodingPicker.SelectedIndex switch
            {
                1 => 437,
                2 => 1252,
                _ => null
            };
            await nextProbe.OpenAsync(fixtures.Resolve(entry.Id), codePage);
            probe = nextProbe;
            StatusText.Text = $"{entry.Id}: {probe.Diagnostics}";
        }
        catch (Exception exception)
        {
            ReaderHost.Content = null;
            await nextProbe.DisposeAsync();
            StatusText.Text = $"Could not open {entry.Id}: {exception.Message}";
        }
    }

    private async void CaptureClicked(object sender, RoutedEventArgs e)
    {
        if (probe is null)
        {
            return;
        }
        try
        {
            savedLocation = await probe.CaptureAsync();
            StatusText.Text = $"Position captured. {probe.Diagnostics}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Capture failed: {exception.Message}";
        }
    }

    private async void RestoreClicked(object sender, RoutedEventArgs e)
    {
        if (probe is null || savedLocation is null)
        {
            StatusText.Text = "Capture a position first.";
            return;
        }
        try
        {
            await probe.RestoreAsync(savedLocation);
            StatusText.Text = $"Position restored. {probe.Diagnostics}";
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Restore failed: {exception.Message}";
        }
    }

    private async void SmallerClicked(object sender, RoutedEventArgs e) => await ChangeScale(1 / 1.1);

    private async void LargerClicked(object sender, RoutedEventArgs e) => await ChangeScale(1.1);

    private async Task ChangeScale(double factor)
    {
        if (probe is not null)
        {
            try
            {
                await probe.ChangeScaleAsync(factor);
                StatusText.Text = probe.Diagnostics;
            }
            catch (Exception exception)
            {
                StatusText.Text = $"Scale change failed: {exception.Message}";
            }
        }
    }
}
