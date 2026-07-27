namespace CodexTPS.Windows.Tests;

public sealed class InstallerLaunchContractTests
{
    [Fact]
    public void StandardInstallerLaunchesPostInstallInBackground()
    {
        var definition = ReadContract("CodexTPS.iss");

        Assert.Contains(
            """Filename: "{app}\CodexTPS.exe"; Parameters: "--background"; Description: "{cm:LaunchProgram,Codex TPS}"; Flags: nowait postinstall skipifsilent""",
            definition);
    }

    [Fact]
    public void PortableInstallerLaunchesInBackground()
    {
        var script = ReadContract("install.ps1");

        Assert.Contains(
            """
            Start-Process `
                        (Join-Path $InstallDirectory "CodexTPS.exe") `
                        -ArgumentList "--background"
            """,
            script);
    }

    private static string ReadContract(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "LaunchContracts", name))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
}
