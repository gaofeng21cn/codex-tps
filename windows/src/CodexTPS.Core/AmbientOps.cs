using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace CodexTPS.Core;

public sealed record AmbientOpsService(
    string InstanceId,
    string Name,
    Uri Endpoint,
    string DisplayPath);

public static partial class AmbientOpsDiscoveryContract
{
    public const string ServiceType = "_ambient-ops._tcp.local.";
    public const string ProtocolVersion = "1";
    public const string DefaultDisplayPath = "/display/overview";

    public static AmbientOpsService? CreateService(
        string serviceName,
        string? host,
        int port,
        IReadOnlyDictionary<string, string> txt)
    {
        if (string.IsNullOrWhiteSpace(host) || port is <= 0 or > 65_535 ||
            !txt.TryGetValue("protocol", out var protocol) || protocol != ProtocolVersion)
        {
            return null;
        }
        var normalizedHost = host.Trim().TrimEnd('.');
        if (!Uri.TryCreate($"http://{FormatHost(normalizedHost)}:{port}", UriKind.Absolute, out var endpoint))
        {
            return null;
        }

        var instanceId = NormalizeInstanceId(txt.GetValueOrDefault("id")) ??
            NormalizeInstanceId(serviceName);
        if (instanceId is null)
        {
            return null;
        }
        var name = txt.GetValueOrDefault("name");
        if (string.IsNullOrWhiteSpace(name))
        {
            name = serviceName;
        }
        return new AmbientOpsService(
            instanceId,
            name.Trim()[..Math.Min(name.Trim().Length, 80)],
            endpoint,
            NormalizePath(txt.GetValueOrDefault("path")));
    }

    public static string NormalizePath(string? value)
    {
        var path = value?.Trim();
        return path is { Length: > 0 and <= 160 } && path.StartsWith('/')
            ? path
            : DefaultDisplayPath;
    }

    public static string? NormalizeInstanceId(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is not null && InstanceIdPattern().IsMatch(normalized)
            ? normalized
            : null;
    }

    private static string FormatHost(string host) => host.Contains(':') ? $"[{host}]" : host;

    [GeneratedRegex("^[a-z0-9][a-z0-9._-]{0,79}$", RegexOptions.CultureInvariant)]
    private static partial Regex InstanceIdPattern();
}

public sealed class AmbientOpsServiceSelector
{
    private readonly string? preferredInstanceId;
    private readonly HashSet<Uri> failedEndpoints = [];

    public AmbientOpsServiceSelector(string? preferredInstanceId)
    {
        this.preferredInstanceId =
            AmbientOpsDiscoveryContract.NormalizeInstanceId(preferredInstanceId);
    }

    public AmbientOpsService? Select(IEnumerable<AmbientOpsService> services) =>
        services
            .Where(service => !failedEndpoints.Contains(service.Endpoint))
            .OrderBy(service => service.InstanceId == preferredInstanceId ? 0 : 1)
            .ThenBy(service => service.InstanceId, StringComparer.Ordinal)
            .ThenBy(service => service.Endpoint.AbsoluteUri, StringComparer.Ordinal)
            .FirstOrDefault();

    public void RecordPushFailure(AmbientOpsService service) =>
        failedEndpoints.Add(service.Endpoint);

    public void ResetFailures() => failedEndpoints.Clear();
}

public sealed partial record AmbientOpsMachineIdentity
{
    public AmbientOpsMachineIdentity(string machineId, string machineName, string platform)
    {
        if (!MachineIdPattern().IsMatch(machineId))
        {
            throw new ArgumentException(
                "Machine ID must contain 1-80 letters, numbers, dots, underscores, or hyphens.",
                nameof(machineId));
        }
        MachineId = machineId;
        MachineName = machineName[..Math.Min(machineName.Length, 80)];
        Platform = platform[..Math.Min(platform.Length, 32)];
    }

    [JsonIgnore]
    public string MachineId { get; }

    public string MachineName { get; }
    public string Platform { get; }

    [GeneratedRegex("^[A-Za-z0-9._-]{1,80}$", RegexOptions.CultureInvariant)]
    private static partial Regex MachineIdPattern();
}

public sealed record AmbientOpsWindowSnapshot(
    double Tps,
    long InputTokens,
    long OutputTokens,
    long CachedInputTokens,
    long ReasoningOutputTokens,
    int Requests)
{
    public static AmbientOpsWindowSnapshot FromMetrics(WindowMetrics metrics) =>
        new(
            metrics.TokensPerSecond,
            (long)Math.Round(metrics.InputTokensPerSecond * metrics.WindowSeconds),
            (long)Math.Round(metrics.OutputTokensPerSecond * metrics.WindowSeconds),
            (long)Math.Round(metrics.CachedInputTokensPerSecond * metrics.WindowSeconds),
            (long)Math.Round(metrics.ReasoningTokensPerSecond * metrics.WindowSeconds),
            metrics.RequestCount);
}

public enum AmbientOpsPetState
{
    Idle,
    Running,
    Waiting,
    Review,
    Failed,
}

public sealed record AmbientOpsPetDefinition(
    string Id,
    string DisplayName,
    int SpriteVersionNumber,
    string AssetHash)
{
    public static readonly AmbientOpsPetDefinition LedgerOwl = new(
        "ledger-owl",
        "Ledger Owl",
        1,
        "783854af87d6ee8639843ca7812917e062345b0095d43f9be5ea2374a41ada6c");
}

public sealed record AmbientOpsPetSnapshot(
    string Id,
    string DisplayName,
    int SpriteVersionNumber,
    string AssetHash,
    AmbientOpsPetState State,
    DateTimeOffset StateSince);

public sealed class AmbientOpsPetTracker
{
    private AmbientOpsPetState? state;
    private DateTimeOffset? stateSince;

    public AmbientOpsPetSnapshot Snapshot(
        AmbientOpsPetDefinition definition,
        UsageSnapshot usage)
    {
        var next = usage.Status != CollectionStatus.Ready
            ? AmbientOpsPetState.Failed
            : usage.ActiveSessions > 0 && usage.OneMinute.RequestCount > 0
                ? AmbientOpsPetState.Running
                : AmbientOpsPetState.Idle;
        if (next != state)
        {
            state = next;
            stateSince = usage.GeneratedAt;
        }
        return new AmbientOpsPetSnapshot(
            definition.Id,
            definition.DisplayName,
            Math.Max(definition.SpriteVersionNumber, 1),
            definition.AssetHash,
            next,
            stateSince ?? usage.GeneratedAt);
    }
}

public sealed record AmbientOpsAgentSnapshot(
    int SchemaVersion,
    string MachineName,
    string Platform,
    DateTimeOffset GeneratedAt,
    string Status,
    string? Error,
    AmbientOpsWindowSnapshot OneMinute,
    AmbientOpsWindowSnapshot FiveMinutes,
    int ActiveSessions,
    AmbientOpsPetSnapshot? Pet)
{
    public static AmbientOpsAgentSnapshot FromUsage(
        UsageSnapshot usage,
        AmbientOpsMachineIdentity identity,
        AmbientOpsAgentSnapshot? fallback = null,
        AmbientOpsPetSnapshot? pet = null)
    {
        var live = usage.Status == CollectionStatus.Ready;
        return new AmbientOpsAgentSnapshot(
            2,
            identity.MachineName,
            identity.Platform,
            usage.GeneratedAt,
            live ? "live" : "error",
            live ? null : ErrorMessage(usage.Status),
            live || fallback is null
                ? AmbientOpsWindowSnapshot.FromMetrics(usage.OneMinute)
                : fallback.OneMinute,
            live || fallback is null
                ? AmbientOpsWindowSnapshot.FromMetrics(usage.FiveMinutes)
                : fallback.FiveMinutes,
            live ? usage.ActiveSessions : fallback?.ActiveSessions ?? 0,
            pet);
    }

    private static string ErrorMessage(CollectionStatus status) => status switch
    {
        CollectionStatus.SessionsDirectoryMissing => "Codex sessions directory is unavailable",
        CollectionStatus.ReadFailed => "Codex usage collection failed",
        _ => string.Empty,
    };
}

public sealed class AmbientOpsPushClient
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly HttpClient httpClient;

    public AmbientOpsPushClient(HttpClient? httpClient = null)
    {
        this.httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
    }

    public HttpRequestMessage CreateRequest(
        Uri endpoint,
        string token,
        AmbientOpsMachineIdentity identity,
        AmbientOpsAgentSnapshot snapshot)
    {
        if (endpoint.Scheme is not ("http" or "https") || string.IsNullOrWhiteSpace(endpoint.Host))
        {
            throw new ArgumentException("Ambient Ops URL must be HTTP or HTTPS.", nameof(endpoint));
        }
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new ArgumentException("Ambient Ops push token is required.", nameof(token));
        }

        var url = new Uri(
            endpoint.AbsoluteUri.TrimEnd('/') +
            $"/api/v1/agents/{Uri.EscapeDataString(identity.MachineId)}/snapshot");
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(snapshot, SerializerOptions),
                Encoding.UTF8,
                "application/json"),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        return request;
    }

    public async Task PushAsync(
        Uri endpoint,
        string token,
        AmbientOpsMachineIdentity identity,
        AmbientOpsAgentSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        using var request = CreateRequest(endpoint, token, identity, snapshot);
        using var response = await httpClient.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        if ((int)response.StatusCode != 202)
        {
            throw new HttpRequestException($"Ambient Ops returned HTTP {(int)response.StatusCode}.");
        }
    }
}
