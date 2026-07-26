using CodexTPS.Core;
using System.Net;

namespace CodexTPS.WindowsApp;

internal enum AmbientOpsConnectionKind
{
    Disabled,
    Discovering,
    Ready,
    NeedsToken,
    Pushing,
    Live,
    Failed,
}

internal sealed record AmbientOpsConnectionStatus(
    AmbientOpsConnectionKind Kind,
    string Message,
    Uri? Endpoint = null);

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
    private AmbientOpsPetAssetCatalog? petAssetCatalog;
    private DateTimeOffset? lastPush;
    private string configurationKey = string.Empty;

    public AmbientOpsConnectionStatus Connection { get; private set; } = new(
        AmbientOpsConnectionKind.Discovering,
        "正在连接");

    public async Task PushIfDueAsync(
        UsageSnapshot usage,
        AppSettings settings,
        string codexHome,
        bool force,
        CancellationToken cancellationToken)
    {
        if (!settings.AmbientEnabled)
        {
            SetStatus(AmbientOpsConnectionKind.Disabled, "未启用");
            return;
        }

        ResetIfConfigurationChanged(settings, codexHome);
        Uri endpoint;
        string destination;

        if (!settings.AutoDiscover)
        {
            if (!Uri.TryCreate(settings.ManualUrl, UriKind.Absolute, out var manualEndpoint) ||
                manualEndpoint.Scheme is not ("http" or "https"))
            {
                SetStatus(AmbientOpsConnectionKind.Failed, "请输入有效的 HTTP(S) 地址");
                return;
            }
            endpoint = manualEndpoint;
            destination = endpoint.Host;
        }
        else
        {
            if (discoveredServices.Count == 0)
            {
                SetStatus(AmbientOpsConnectionKind.Discovering, "正在自动发现");
                discoveredServices = await discovery.DiscoverAsync(cancellationToken)
                    .ConfigureAwait(false);
                selector!.ResetFailures();
            }
            selectedService ??= selector!.Select(discoveredServices);
            if (selectedService is null)
            {
                SetStatus(AmbientOpsConnectionKind.Failed, "未发现兼容的 Ambient Ops");
                return;
            }
            endpoint = selectedService.Endpoint;
            destination = selectedService.Name;
        }

        if (string.IsNullOrWhiteSpace(settings.Token))
        {
            SetStatus(
                AmbientOpsConnectionKind.NeedsToken,
                $"已发现 {destination} · 需要推送令牌",
                endpoint);
            return;
        }
        if (!force && lastPush is { } pushedAt && DateTimeOffset.Now - pushedAt < PushInterval)
        {
            return;
        }

        var identity = new AmbientOpsMachineIdentity(
            settings.MachineId,
            settings.MachineName,
            "Windows");
        var petAsset = settings.PetEnabled
            ? petAssetCatalog!.CurrentAsset()
            : null;
        var pet = petAsset is not null
            ? petTracker.Snapshot(petAsset.Definition, usage)
            : null;
        var payload = AmbientOpsAgentSnapshot.FromUsage(
            usage,
            identity,
            lastSuccessfulSnapshot,
            pet);

        try
        {
            await PushAsync(
                endpoint,
                payload,
                identity,
                settings.Token,
                petAsset,
                cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException error) when (
            error.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            SetStatus(
                AmbientOpsConnectionKind.Failed,
                $"推送被拒绝 · HTTP {(int)error.StatusCode.Value}",
                endpoint);
            return;
        }
        catch (Exception error) when (
            settings.AutoDiscover &&
            !cancellationToken.IsCancellationRequested &&
            error is HttpRequestException or TaskCanceledException)
        {
            selector!.RecordPushFailure(selectedService!);
            var fallback = selector.Select(discoveredServices);
            if (fallback is null)
            {
                discoveredServices = [];
                selectedService = null;
                SetStatus(AmbientOpsConnectionKind.Failed, $"推送失败 · {error.Message}");
                return;
            }
            selectedService = fallback;
            try
            {
                await PushAsync(
                    fallback.Endpoint,
                    payload,
                    identity,
                    settings.Token,
                    petAsset,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (Exception fallbackError) when (
                !cancellationToken.IsCancellationRequested &&
                fallbackError is HttpRequestException or TaskCanceledException)
            {
                SetStatus(
                    AmbientOpsConnectionKind.Failed,
                    $"推送失败 · {fallbackError.Message}",
                    fallback.Endpoint);
                return;
            }
            endpoint = fallback.Endpoint;
            destination = fallback.Name;
        }
        catch (Exception error) when (
            !cancellationToken.IsCancellationRequested &&
            error is HttpRequestException or TaskCanceledException)
        {
            SetStatus(
                AmbientOpsConnectionKind.Failed,
                $"推送失败 · {error.Message}",
                endpoint);
            return;
        }
        RecordSuccess(usage, payload, destination, endpoint);
    }

    private async Task PushAsync(
        Uri endpoint,
        AmbientOpsAgentSnapshot payload,
        AmbientOpsMachineIdentity identity,
        string token,
        AmbientOpsPetAsset? petAsset,
        CancellationToken cancellationToken)
    {
        SetStatus(AmbientOpsConnectionKind.Pushing, $"正在推送到 {endpoint.Host}", endpoint);
        await pushClient.PushAsync(
            endpoint, token, identity, payload, petAsset, cancellationToken)
            .ConfigureAwait(false);
    }

    private void RecordSuccess(
        UsageSnapshot usage,
        AmbientOpsAgentSnapshot payload,
        string destination,
        Uri endpoint)
    {
        if (usage.Status == CollectionStatus.Ready)
        {
            lastSuccessfulSnapshot = payload;
        }
        lastPush = DateTimeOffset.Now;
        SetStatus(AmbientOpsConnectionKind.Live, $"{destination} · 已连接", endpoint);
    }

    private void ResetIfConfigurationChanged(AppSettings settings, string codexHome)
    {
        var nextKey = string.Join('|',
            settings.AutoDiscover,
            settings.ManualUrl,
            settings.PreferredInstanceId,
            settings.MachineId,
            codexHome);
        if (nextKey == configurationKey)
        {
            return;
        }
        configurationKey = nextKey;
        selector = new AmbientOpsServiceSelector(settings.PreferredInstanceId);
        discoveredServices = [];
        selectedService = null;
        petAssetCatalog = new AmbientOpsPetAssetCatalog(codexHome);
    }

    private void SetStatus(AmbientOpsConnectionKind kind, string message, Uri? endpoint = null) =>
        Connection = new AmbientOpsConnectionStatus(kind, message, endpoint);
}
