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
}
