using System.Net;
using CodexTPS.WindowsApp;

namespace CodexTPS.Windows.Tests;

public sealed class WindowsUpdateManagerTests
{
    [Fact]
    public async Task FindsNewerReleaseAndKeepsItAvailable()
    {
        using var fixture = new UpdateManagerFixture(
            HttpStatusCode.OK,
            ReleaseJson("v0.2.20"),
            new SemanticVersion(0, 2, 19));

        await fixture.Manager.CheckForUpdatesAsync();

        Assert.Equal(AppUpdateKind.Available, fixture.Manager.State.Kind);
        Assert.Equal("v0.2.20", fixture.Manager.State.Release?.TagName);
    }

    [Fact]
    public async Task ReportsUpToDateForSameRelease()
    {
        using var fixture = new UpdateManagerFixture(
            HttpStatusCode.OK,
            ReleaseJson("v0.2.20"),
            new SemanticVersion(0, 2, 20));

        await fixture.Manager.CheckForUpdatesAsync();

        Assert.Equal(AppUpdateKind.UpToDate, fixture.Manager.State.Kind);
        Assert.Equal("已是最新版本", fixture.Manager.State.Message);
    }

    [Fact]
    public async Task ManualFailureIsVisibleButAutomaticFailureStaysQuiet()
    {
        using var manualFixture = new UpdateManagerFixture(
            HttpStatusCode.ServiceUnavailable,
            string.Empty,
            new SemanticVersion(0, 2, 19));
        await manualFixture.Manager.CheckForUpdatesAsync(manual: true);
        Assert.Equal(AppUpdateKind.Failed, manualFixture.Manager.State.Kind);

        using var automaticFixture = new UpdateManagerFixture(
            HttpStatusCode.ServiceUnavailable,
            string.Empty,
            new SemanticVersion(0, 2, 19));
        await automaticFixture.Manager.CheckForUpdatesAsync(manual: false);
        Assert.Equal(AppUpdateKind.Idle, automaticFixture.Manager.State.Kind);
    }

    private static string ReleaseJson(string tag) =>
        $$"""
        {
          "tag_name": "{{tag}}",
          "assets": [
            {
              "name": "Codex-TPS-Windows-win-x64-Setup.exe",
              "browser_download_url": "https://github.com/gaofeng21cn/opl-fleet-agent/releases/download/{{tag}}/Codex-TPS-Windows-win-x64-Setup.exe"
            },
            {
              "name": "Codex-TPS-Windows-win-x64-Setup.exe.sha256",
              "browser_download_url": "https://github.com/gaofeng21cn/opl-fleet-agent/releases/download/{{tag}}/Codex-TPS-Windows-win-x64-Setup.exe.sha256"
            }
          ]
        }
        """;

    private sealed class UpdateManagerFixture : IDisposable
    {
        private readonly DirectoryInfo directory = Directory.CreateTempSubdirectory(
            "codex-tps-update-manager-test-");
        private readonly HttpClient client;

        public UpdateManagerFixture(
            HttpStatusCode statusCode,
            string content,
            SemanticVersion currentVersion)
        {
            client = new HttpClient(new StubHandler(statusCode, content));
            var executable = Path.Combine(directory.FullName, "CodexTPS.exe");
            File.WriteAllText(executable, "test executable");
            Manager = new WindowsUpdateManager(
                client,
                currentVersion,
                executable,
                Path.Combine(directory.FullName, "updates"),
                Path.Combine(directory.FullName, "update-result.json"));
        }

        public WindowsUpdateManager Manager { get; }

        public void Dispose()
        {
            Manager.Dispose();
            client.Dispose();
            directory.Delete(recursive: true);
        }
    }

    private sealed class StubHandler(HttpStatusCode statusCode, string content)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content),
                RequestMessage = request,
            });
    }
}
