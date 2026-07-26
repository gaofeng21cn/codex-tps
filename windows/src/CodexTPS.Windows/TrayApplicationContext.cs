using CodexTPS.Core;
using System.Diagnostics;

namespace CodexTPS.WindowsApp;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly AppSettingsStore settingsStore = new();
    private readonly AmbientOpsCoordinator ambientOps = new();
    private readonly CancellationTokenSource cancellation = new();
    private readonly Icon applicationIcon;
    private readonly NotifyIcon trayIcon;
    private readonly System.Windows.Forms.Timer refreshTimer;
    private AppSettings settings;
    private SessionScanner scanner;
    private DashboardForm dashboard;
    private UsageSnapshot lastSnapshot = UsageSnapshot.Empty(
        DateTimeOffset.Now,
        CollectionStatus.SessionsDirectoryMissing);
    private bool refreshing;

    public TrayApplicationContext(bool showDashboard)
    {
        settings = settingsStore.Load();
        try
        {
            settings.StartWithWindows = StartupRegistration.IsEnabled();
        }
        catch
        {
            settings.StartWithWindows = false;
        }
        scanner = CreateScanner(settings);
        dashboard = CreateDashboard(scanner.SessionsRoot);

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开", null, (_, _) => dashboard.ShowFromTray());
        menu.Items.Add("刷新", null, async (_, _) => await RefreshAsync(forcePush: true));
        menu.Items.Add("设置", null, (_, _) => ShowSettings());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitThread());
        applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? (Icon)SystemIcons.Application.Clone();
        trayIcon = new NotifyIcon
        {
            Icon = applicationIcon,
            Text = "Codex TPS",
            Visible = true,
            ContextMenuStrip = menu,
        };
        trayIcon.MouseClick += (_, eventArgs) =>
        {
            if (eventArgs.Button == MouseButtons.Left)
            {
                dashboard.ShowFromTray();
            }
        };
        trayIcon.DoubleClick += (_, _) => dashboard.ShowFromTray();

        refreshTimer = new System.Windows.Forms.Timer
        {
            Interval = settings.RefreshSeconds * 1_000,
        };
        refreshTimer.Tick += async (_, _) => await RefreshAsync(forcePush: false);
        refreshTimer.Start();
        dashboard.SetRefreshCadence(settings.RefreshSeconds);
        dashboard.SetStartupEnabled(settings.StartWithWindows);
        if (showDashboard)
        {
            dashboard.ShowFromTray();
        }
        _ = RefreshAsync(forcePush: true);
    }

    protected override void ExitThreadCore()
    {
        refreshTimer.Stop();
        cancellation.Cancel();
        trayIcon.Visible = false;
        trayIcon.Dispose();
        applicationIcon.Dispose();
        dashboard.CloseForExit();
        dashboard.Dispose();
        cancellation.Dispose();
        base.ExitThreadCore();
    }

    private DashboardForm CreateDashboard(string sessionsRoot)
    {
        var form = new DashboardForm(sessionsRoot);
        form.SettingsRequested += (_, _) => ShowSettings();
        form.RefreshRequested += async (_, _) => await RefreshAsync(forcePush: true);
        form.SessionsFolderRequested += (_, _) => OpenSessionsDirectory();
        form.ExitRequested += (_, _) => ExitThread();
        form.RefreshCadenceChanged += SetRefreshCadence;
        form.StartupChanged += SetStartupEnabled;
        return form;
    }

    private async Task RefreshAsync(bool forcePush)
    {
        if (refreshing)
        {
            return;
        }
        refreshing = true;
        try
        {
            lastSnapshot = await Task.Run(() => scanner.Refresh(), cancellation.Token);
            await ambientOps.PushIfDueAsync(
                lastSnapshot,
                settings,
                forcePush,
                cancellation.Token);
            dashboard.UpdateSnapshot(lastSnapshot, ambientOps.Connection);
            trayIcon.Text = lastSnapshot.Status == CollectionStatus.Ready
                ? $"Codex TPS · {Compact(lastSnapshot.OneMinute.TokensPerSecond)} t/s"
                : "Codex TPS · sessions unavailable";
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return;
        }
        catch (Exception error)
        {
            dashboard.UpdateSnapshot(
                lastSnapshot,
                new AmbientOpsConnectionStatus(
                    AmbientOpsConnectionKind.Failed,
                    $"错误 · {error.Message}"));
        }
        finally
        {
            refreshing = false;
        }
    }

    private void ShowSettings()
    {
        using var form = new SettingsForm(settings, ambientOps.Connection);
        if (form.ShowDialog(dashboard) != DialogResult.OK || form.ResultSettings is not { } next)
        {
            return;
        }
        try
        {
            var previousStartup = StartupRegistration.IsEnabled();
            try
            {
                StartupRegistration.SetEnabled(next.StartWithWindows);
                settingsStore.Save(next);
            }
            catch
            {
                StartupRegistration.SetEnabled(previousStartup);
                throw;
            }
            settings = next;
            scanner = CreateScanner(settings);
            dashboard.UpdateSessionsRoot(scanner.SessionsRoot);
            refreshTimer.Interval = settings.RefreshSeconds * 1_000;
            dashboard.SetRefreshCadence(settings.RefreshSeconds);
            dashboard.SetStartupEnabled(settings.StartWithWindows);
            _ = RefreshAsync(forcePush: true);
        }
        catch (Exception error)
        {
            MessageBox.Show(
                dashboard,
                error.Message,
                "Settings could not be saved",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private static SessionScanner CreateScanner(AppSettings settings)
    {
        try
        {
            return new SessionScanner(
                string.IsNullOrWhiteSpace(settings.CodexHome) ? null : settings.CodexHome);
        }
        catch (Exception error) when (
            error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            settings.CodexHome = string.Empty;
            return new SessionScanner();
        }
    }

    private static string Compact(double value) => Math.Abs(value) switch
    {
        >= 1_000_000 => $"{value / 1_000_000:0.0}M",
        >= 1_000 => $"{value / 1_000:0.0}K",
        _ => $"{value:0.0}",
    };

    private void SetRefreshCadence(int seconds)
    {
        if (seconds is not (5 or 15 or 30 or 60))
        {
            return;
        }
        var previous = settings.RefreshSeconds;
        try
        {
            settings.RefreshSeconds = seconds;
            settingsStore.Save(settings);
            refreshTimer.Interval = seconds * 1_000;
        }
        catch (Exception error)
        {
            settings.RefreshSeconds = previous;
            dashboard.SetRefreshCadence(previous);
            ShowError("刷新间隔无法保存", error);
        }
    }

    private void SetStartupEnabled(bool enabled)
    {
        var previous = settings.StartWithWindows;
        try
        {
            StartupRegistration.SetEnabled(enabled);
            settings.StartWithWindows = enabled;
            settingsStore.Save(settings);
            dashboard.SetStartupEnabled(enabled);
        }
        catch (Exception error)
        {
            StartupRegistration.SetEnabled(previous);
            settings.StartWithWindows = previous;
            dashboard.SetStartupEnabled(previous);
            ShowError("登录启动无法更新", error);
        }
    }

    private void OpenSessionsDirectory()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = scanner.SessionsRoot,
                UseShellExecute = true,
            });
        }
        catch (Exception error)
        {
            ShowError("会话目录无法打开", error);
        }
    }

    private void ShowError(string title, Exception error) => MessageBox.Show(
        dashboard,
        error.Message,
        title,
        MessageBoxButtons.OK,
        MessageBoxIcon.Error);
}
