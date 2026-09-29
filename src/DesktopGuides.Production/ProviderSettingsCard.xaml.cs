using DesktopGuides.Core.Providers;
using DesktopGuides.Infrastructure.Providers;
using DesktopGuides.Production.Providers;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DesktopGuides.Production;

public sealed partial class ProviderSettingsCard : UserControl
{
    private ProviderServices? services;
    private ProviderCredentials saved = ProviderCredentials.None;
    private CancellationTokenSource? pending;

    public ProviderSettingsCard()
    {
        InitializeComponent();
        SetBusy(true);
    }

    internal event EventHandler? CredentialsChanged;

    internal async Task InitializeAsync(ProviderServices providerServices)
    {
        services = providerServices;
        saved = await services.Credentials.LoadAsync(CancellationToken.None);
        ShowSaved();
        SetBusy(false);
    }

    internal void Cancel() => pending?.Cancel();

    private void SaveClicked(object sender, RoutedEventArgs args) => _ = RunAsync(async token =>
    {
        ProviderCredentials next = Pending();
        await services!.Credentials.SaveAsync(next, token);
        saved = next;
        ShowSaved();
        Show(InfoBarSeverity.Success, "Provider credentials saved.");
        CredentialsChanged?.Invoke(this, EventArgs.Empty);
    });

    private void RemoveClicked(object sender, RoutedEventArgs args) => _ = RunAsync(async token =>
    {
        await services!.Credentials.ClearAsync(token);
        saved = ProviderCredentials.None;
        ShowSaved();
        Show(InfoBarSeverity.Informational, "Provider credentials removed.");
        CredentialsChanged?.Invoke(this, EventArgs.Empty);
    });

    private void TestClicked(object sender, RoutedEventArgs args) => _ = RunAsync(async token =>
    {
        ProviderCredentials test = Pending();
        if (test.Igdb is null && test.SteamGridDbKey is null)
        {
            Show(InfoBarSeverity.Informational, "Enter credentials to test them.");
            return;
        }
        List<string> results = [];
        bool failed = false;
        if (test.Igdb is { } igdb)
        {
            try
            {
                await services!.CreateIgdb(_ => Task.FromResult<IgdbCredentials?>(igdb)).TestConnectionAsync(token);
                results.Add("IGDB connected.");
            }
            catch (ProviderException error)
            {
                failed = true;
                results.Add(ProviderMessages.ForIgdb(error.Kind));
            }
        }
        if (test.SteamGridDbKey is { } key)
        {
            try
            {
                await services!.CreateSteamGridDb(_ => Task.FromResult<string?>(key)).TestConnectionAsync(token);
                results.Add("SteamGridDB connected.");
            }
            catch (ProviderException error)
            {
                failed = true;
                results.Add(ProviderMessages.ForSteamGridDb(error.Kind));
            }
        }
        Show(failed ? InfoBarSeverity.Error : InfoBarSeverity.Success, string.Join(" ", results));
    });

    // Ruling T10-b: a blank field keeps the saved value.
    private ProviderCredentials Pending() => ProviderCredentialBlob.Normalize(
        Entered(IgdbClientIdInput) ?? saved.Igdb?.ClientId,
        Entered(IgdbClientSecretInput) ?? saved.Igdb?.ClientSecret,
        Entered(SteamGridDbKeyInput) ?? saved.SteamGridDbKey);

    private static string? Entered(PasswordBox box) =>
        string.IsNullOrWhiteSpace(box.Password) ? null : box.Password;

    private async Task RunAsync(Func<CancellationToken, Task> action)
    {
        if (services is null || pending is not null) return;
        pending = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            await action(pending.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ArgumentException error)
        {
            // Normalize names the field and never includes its value.
            Show(InfoBarSeverity.Error, error.Message);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Show(InfoBarSeverity.Error, "Couldn't update the saved provider credentials.");
        }
        finally
        {
            pending.Dispose();
            pending = null;
            SetBusy(false);
        }
    }

    private void ShowSaved()
    {
        IgdbClientIdInput.Password = IgdbClientSecretInput.Password = SteamGridDbKeyInput.Password = string.Empty;
        IgdbClientIdInput.PlaceholderText = saved.Igdb is null ? "Not set" : "Saved";
        IgdbClientSecretInput.PlaceholderText = saved.Igdb is null ? "Not set" : "Saved";
        SteamGridDbKeyInput.PlaceholderText = saved.SteamGridDbKey is null ? "Not set" : "Saved";
        string description = saved.Igdb is null
            ? "Add your IGDB credentials to search for games."
            : "IGDB credentials saved.";
        ProviderSettingsExpander.Description = description;
        AutomationProperties.SetName(ProviderSettingsExpander, $"Game data providers. {description}");
    }

    private void Show(InfoBarSeverity severity, string message)
    {
        ProviderSettingsStatus.Severity = severity;
        ProviderSettingsStatus.Message = message;
        AutomationProperties.SetName(ProviderSettingsStatus, message);
        ProviderSettingsStatus.IsOpen = true;
    }

    private void SetBusy(bool busy)
    {
        ProviderBusy.IsActive = busy;
        TestConnectionButton.IsEnabled = SaveButton.IsEnabled = !busy && services is not null;
        RemoveButton.IsEnabled = !busy && services is not null && saved != ProviderCredentials.None;
    }

    internal void Expand() => ProviderSettingsExpander.IsExpanded = true;
}
