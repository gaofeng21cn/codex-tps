using System.Net;
using System.Security.Cryptography;
using System.Text;
using CodexTPS.WindowsApp;

namespace CodexTPS.Windows.Tests;

public sealed class UpdateModelsTests
{
    [Theory]
    [InlineData("0.2.20", 0, 2, 20)]
    [InlineData("v12.34.56", 12, 34, 56)]
    public void SemanticVersionParsesAppAndTagVersions(
        string value,
        int major,
        int minor,
        int patch)
    {
        Assert.True(SemanticVersion.TryParse(value, out var version));
        Assert.Equal(new SemanticVersion(major, minor, patch), version);
    }

    [Theory]
    [InlineData("0.2")]
    [InlineData("release-0.2.20")]
    [InlineData("0.2.20.1")]
    [InlineData("0.2.beta")]
    public void SemanticVersionRejectsMalformedVersions(string value)
    {
        Assert.False(SemanticVersion.TryParse(value, out _));
    }

    [Fact]
    public void SemanticVersionComparesEachComponent()
    {
        Assert.True(new SemanticVersion(0, 2, 20) > new SemanticVersion(0, 2, 19));
        Assert.True(new SemanticVersion(1, 0, 0) > new SemanticVersion(0, 99, 99));
    }

    [Fact]
    public void ParsesExpectedWindowsAssetsFromLatestRelease()
    {
        var release = GitHubReleaseParser.Parse(ReleaseJson("v0.2.20"));

        Assert.Equal("v0.2.20", release.TagName);
        Assert.Equal(new SemanticVersion(0, 2, 20), release.Version);
        Assert.EndsWith(
            "/v0.2.20/Codex-TPS-Windows-win-x64-Setup.exe",
            release.InstallerUri.AbsoluteUri,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsReleaseAssetOutsideCanonicalRepository()
    {
        var json = ReleaseJson("v0.2.20").Replace(
            "https://github.com/gaofeng21cn/codex-tps/",
            "https://github.com/example/codex-tps/",
            StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => GitHubReleaseParser.Parse(json));
    }

    [Fact]
    public async Task ParsesAndVerifiesExactInstallerChecksum()
    {
        var directory = Directory.CreateTempSubdirectory("codex-tps-update-test-");
        try
        {
            var installer = Path.Combine(
                directory.FullName,
                "Codex-TPS-Windows-win-x64-Setup.exe");
            await File.WriteAllTextAsync(installer, "verified update bytes", Encoding.UTF8);
            var digest = Convert.ToHexString(
                    SHA256.HashData(await File.ReadAllBytesAsync(installer)))
                .ToLowerInvariant();
            var expected = UpdatePackageVerifier.ParseExpectedSha256(
                $"{digest}  {Path.GetFileName(installer)}\n",
                Path.GetFileName(installer));

            await UpdatePackageVerifier.VerifyAsync(installer, expected);

            Assert.Equal(digest, expected);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static string ReleaseJson(string tag) =>
        $$"""
        {
          "tag_name": "{{tag}}",
          "assets": [
            {
              "name": "Codex-TPS-Windows-win-x64-Setup.exe",
              "browser_download_url": "https://github.com/gaofeng21cn/codex-tps/releases/download/{{tag}}/Codex-TPS-Windows-win-x64-Setup.exe"
            },
            {
              "name": "Codex-TPS-Windows-win-x64-Setup.exe.sha256",
              "browser_download_url": "https://github.com/gaofeng21cn/codex-tps/releases/download/{{tag}}/Codex-TPS-Windows-win-x64-Setup.exe.sha256"
            }
          ]
        }
        """;
}
