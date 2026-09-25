using Microsoft.UI.Xaml;

namespace DesktopGuides.App.Probes;

public interface IReaderProbe : IAsyncDisposable
{
    FrameworkElement View { get; }
    string Diagnostics { get; }
    Task OpenAsync(string path, int? codePage);
    Task<string> CaptureAsync();
    Task RestoreAsync(string locationJson);
    Task ChangeScaleAsync(double factor);
}
