using CodexTPS.Core;

namespace CodexTPS.WindowsApp;

internal sealed class AmbientOpsCoordinator
{
    private static readonly TimeSpan PushInterval = TimeSpan.FromSeconds(10);
    private readonly AmbientOpsDiscovery discovery = new();
    private readonly AmbientOpsPushClient pushClient = new();
    private readonly AmbientOpsPetTracker petTracker = new();
    private IReadOnlyList<AmbientOpsService> discoveredServices = [];
    private AmbientOpsService? selectedService;
    private AmbientOpsServiceSelector? selector;
    private AmbientOpsAgentSnapshot? lastSuccessfulSnapshot;
    private DateTimeOffset? lastPush;
    private string configurationKey = string.Empty;

    public string Status { get; private set; } = "Not connected";

    public async Task PushIfDueAsync(
        UsageSnapshot usage,
        AppSettings settings,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!settings.AmbientEnabled)
        {
            Status = "Disabled";
            return;
        }
        if (string.IsNullOrWhiteSpace(settings.Token))
        {
            Status = "Push token required";
            return;
        }
        if (!force && lastPush is { } pushedAt && DateTimeOffset.Now - pushedAt < PushInterval)
        {
            return;
        }

        ResetIfConfigurationChanged(settings);
        var identity = new AmbientOpsMachineIdentity(
            settings.MachineId,
            settings.MachineName,
            "Windows");
        var pet = settings.PetEnabled
            ? petTracker.Snapshot(AmbientOpsPetDefinition.LedgerOwl, usage)
            : null;
        var payload = AmbientOpsAgentSnapshot.FromUsage(
            usage,
            identity,
            lastSuccessfulSnapshot,
            pet);

        if (!settings.AutoDiscover)
        {
            if (!Uri.TryCreate(settings.ManualUrl, UriKind.Absolute, out var endpoint) ||
                endpoint.Scheme is not ("http" or "https"))
            {
                Status = "Valid HTTP(S) URL required";
                return;
            }
            await PushAsync(endpoint, payload, identity, settings.Token, cancellationToken)
                .ConfigureAwait(false);
            RecordSuccess(usage, payload, endpoint.Host);
            return;
        }

        if (discoveredServices.Count == 0)
        {
            Status = "Discovering Ambient Ops";
            discoveredServices = await discovery.DiscoverAsync(cancellationToken)
                .ConfigureAwait(false);
            selector!.ResetFailures();
        }
        selectedService ??= selector!.Select(discoveredServices);
        if (selectedService is null)
        {
            Status = "No compatible Ambient Ops service";
            return;
        }

        try
        {
            await PushAsync(
                selectedService.Endpoint,
                payload,
                identity,
                settings.Token,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception error) when (
            !cancellationToken.IsCancellationRequested &&
            error is HttpRequestException or TaskCanceledException)
        {
            selector!.RecordPushFailure(selectedService);
            var fallback = selector.Select(discoveredServices);
            if (fallback is null)
            {
                discoveredServices = [];
                selectedService = null;
                Status = $"Push failed: {error.Message}";
                return;
            }
            selectedService = fallback;
            await PushAsync(
                fallback.Endpoint,
                payload,
                identity,
                settings.Token,
                cancellationToken).ConfigureAwait(false);
        }
        RecordSuccess(usage, payload, selectedService.Name);
    }

    private async Task PushAsync(
        Uri endpoint,
        AmbientOpsAgentSnapshot payload,
        AmbientOpsMachineIdentity identity,
        string token,
        CancellationToken cancellationToken)
    {
        Status = $"Pushing to {endpoint.Host}";
        await pushClient.PushAsync(endpoint, token, identity, payload, cancellationToken)
            .ConfigureAwait(false);
    }

    private void RecordSuccess(
        UsageSnapshot usage,
        AmbientOpsAgentSnapshot payload,
        string destination)
    {
        if (usage.Status == CollectionStatus.Ready)
        {
            lastSuccessfulSnapshot = payload;
        }
        lastPush = DateTimeOffset.Now;
        Status = $"Live · {destination}";
    }

    private void ResetIfConfigurationChanged(AppSettings settings)
    {
        var nextKey = string.Join('|',
            settings.AutoDiscover,
            settings.ManualUrl,
            settings.PreferredInstanceId,
            settings.MachineId);
        if (nextKey == configurationKey)
        {
            return;
        }
        configurationKey = nextKey;
        selector = new AmbientOpsServiceSelector(settings.PreferredInstanceId);
        discoveredServices = [];
        selectedService = null;
    }
}
