using DesktopGuides.Core.Reading;
using Microsoft.UI.Xaml;

namespace DesktopGuides.App.Reading;

public interface IReaderAdapter : IReaderSession
{
    FrameworkElement View { get; }
}
