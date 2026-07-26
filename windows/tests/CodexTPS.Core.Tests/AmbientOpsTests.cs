using System.Text.Json;
using CodexTPS.Core;

namespace CodexTPS.Core.Tests;

public sealed class AmbientOpsTests
{
    [Fact]
    public void ParsesDiscoveryContractAndSelectsPreferredFallback()
    {
        var preferred = AmbientOpsDiscoveryContract.CreateService(
            "Preferred",
            "preferred.local.",
            8791,
            new Dictionary<string, string>
            {
                ["id"] = "preferred",
                ["name"] = "Preferred Ops",
                ["path"] = "/display/pet",
                ["protocol"] = "1",
            });
        var other = AmbientOpsDiscoveryContract.CreateService(
            "Other",
            "other.local.",
            8791,
            new Dictionary<string, string>
            {
                ["id"] = "other",
                ["protocol"] = "1",
            });
        var selector = new AmbientOpsServiceSelector("preferred");

        Assert.Equal(preferred, selector.Select([other!, preferred!]));
        selector.RecordPushFailure(preferred!);
        Assert.Equal(other, selector.Select([preferred!, other!]));
        Assert.Equal("/display/pet", preferred!.DisplayPath);
        Assert.Null(AmbientOpsDiscoveryContract.CreateService(
            "Future",
            "future.local",
            8791,
            new Dictionary<string, string> { ["protocol"] = "2" }));
    }

    [Fact]
    public void SerializesOnlyAggregatePayloadFields()
    {
        var identity = new AmbientOpsMachineIdentity("windows-pc", "Windows PC", "Windows");
        var payload = AmbientOpsAgentSnapshot.FromUsage(Usage(), identity);
        var json = JsonSerializer.Serialize(payload, AmbientOpsPushClient.SerializerOptions);
        using var document = JsonDocument.Parse(json);
        var keys = document.RootElement.EnumerateObject().Select(item => item.Name).ToHashSet();

        Assert.Equal(
            new HashSet<string>
            {
                "schemaVersion", "machineName", "platform", "generatedAt", "status",
                "oneMinute", "fiveMinutes", "activeSessions",
            },
            keys);
        Assert.DoesNotContain("prompt", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("response", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionId", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sessionsRoot", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BuildsAuthenticatedRequestWithoutConversationContent()
    {
        var identity = new AmbientOpsMachineIdentity("windows-pc", "Windows PC", "Windows");
        var request = new AmbientOpsPushClient().CreateRequest(
            new Uri("https://ops.example.test/base"),
            "test-token",
            identity,
            AmbientOpsAgentSnapshot.FromUsage(Usage(), identity));
        var body = await request.Content!.ReadAsStringAsync();

        Assert.Equal(
            "https://ops.example.test/base/api/v1/agents/windows-pc/snapshot",
            request.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("test-token", request.Headers.Authorization.Parameter);
        Assert.DoesNotContain("prompt", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("response", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EncodesPetStateWithAmbientOpsWireCasing()
    {
        var identity = new AmbientOpsMachineIdentity("windows-pc", "Windows PC", "Windows");
        var usage = Usage();
        var pet = new AmbientOpsPetTracker().Snapshot(AmbientOpsPetDefinition.LedgerOwl, usage);
        var payload = AmbientOpsAgentSnapshot.FromUsage(usage, identity, pet: pet);
        var json = JsonSerializer.Serialize(payload, AmbientOpsPushClient.SerializerOptions);

        Assert.Contains("\"state\":\"running\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("prompt", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UploadsOnlyPetAssetRequestedBySnapshotResponse()
    {
        var temporary = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            PetAssetTests.WritePetForUpload(temporary);
            var asset = Assert.IsType<AmbientOpsPetAsset>(
                new AmbientOpsPetAssetCatalog(temporary).CurrentAsset());
            var handler = new PetUploadHandler(asset.Definition.AssetHash);
            var client = new AmbientOpsPushClient(new HttpClient(handler));
            var identity = new AmbientOpsMachineIdentity("windows-pc", "Windows PC", "Windows");
            var usage = Usage();
            var pet = new AmbientOpsPetTracker().Snapshot(asset.Definition, usage);

            await client.PushAsync(
                new Uri("https://ops.example.test/base"),
                "test-token",
                identity,
                AmbientOpsAgentSnapshot.FromUsage(usage, identity, pet: pet),
                asset);

            Assert.Equal(2, handler.Requests.Count);
            Assert.Equal(HttpMethod.Put, handler.Requests[1].Method);
            Assert.Equal(
                $"https://ops.example.test/base/api/v1/agents/windows-pc/pets/{asset.Definition.AssetHash}",
                handler.Requests[1].Url);
            Assert.Equal("Bearer test-token", handler.Requests[1].Authorization);
            Assert.Equal("image/webp", handler.Requests[1].ContentType);
            Assert.Equal(asset.Data.ToArray(), handler.Requests[1].Body);
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    [Fact]
    public async Task DoesNotUploadForeignMissingHash()
    {
        var handler = new PetUploadHandler(new string('a', 64));
        var client = new AmbientOpsPushClient(new HttpClient(handler));
        var identity = new AmbientOpsMachineIdentity("windows-pc", "Windows PC", "Windows");

        await client.PushAsync(
            new Uri("https://ops.example.test"),
            "test-token",
            identity,
            AmbientOpsAgentSnapshot.FromUsage(Usage(), identity));

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task RetriesSnapshotOnceAfterUploadManifestConflict()
    {
        var temporary = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            PetAssetTests.WritePetForUpload(temporary);
            var asset = Assert.IsType<AmbientOpsPetAsset>(
                new AmbientOpsPetAssetCatalog(temporary).CurrentAsset());
            var handler = new PetUploadHandler(asset.Definition.AssetHash, conflictOnce: true);
            var client = new AmbientOpsPushClient(new HttpClient(handler));
            var identity = new AmbientOpsMachineIdentity("windows-pc", "Windows PC", "Windows");

            await client.PushAsync(
                new Uri("https://ops.example.test"),
                "test-token",
                identity,
                AmbientOpsAgentSnapshot.FromUsage(Usage(), identity),
                asset);

            Assert.Equal(
                [HttpMethod.Post, HttpMethod.Put, HttpMethod.Post, HttpMethod.Put],
                handler.Requests.Select(item => item.Method));
        }
        finally
        {
            Directory.Delete(temporary, recursive: true);
        }
    }

    private static UsageSnapshot Usage()
    {
        var oneMinute = new WindowMetrics(60, 2, 2, 10, 8, 5, 2, 1, 0.625, 600);
        return new UsageSnapshot(
            DateTimeOffset.FromUnixTimeSeconds(1_000),
            oneMinute,
            WindowMetrics.Empty(300),
            WindowMetrics.Empty(1_800),
            WindowMetrics.Empty(3_600),
            3,
            0,
            CollectionStatus.Ready);
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        string Url,
        string? Authorization,
        string? ContentType,
        byte[] Body);

    private sealed class PetUploadHandler(
        string missingHash,
        bool conflictOnce = false) : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = request.Content is null
                ? []
                : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!.AbsoluteUri,
                request.Headers.Authorization?.ToString(),
                request.Content?.Headers.ContentType?.MediaType,
                body));

            return request.Method == HttpMethod.Put
                ? new HttpResponseMessage(
                    conflictOnce && Requests.Count == 2
                        ? System.Net.HttpStatusCode.Conflict
                        : System.Net.HttpStatusCode.Created)
                : new HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
                {
                    Content = new StringContent(
                        $$"""{"accepted":true,"missingPetAssets":["{{missingHash}}"]}"""),
                };
        }
    }
}
