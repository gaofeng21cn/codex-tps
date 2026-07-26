using CodexTPS.Core;

namespace CodexTPS.WindowsApp;

internal sealed class DashboardForm : Form
{
    private static readonly Color Background = Color.FromArgb(12, 15, 18);
    private static readonly Color Primary = Color.FromArgb(238, 243, 247);
    private static readonly Color Secondary = Color.FromArgb(154, 165, 174);
    private readonly Label totalValue = ValueLabel(42);
    private readonly Label inputValue = ValueLabel(18);
    private readonly Label outputValue = ValueLabel(18);
    private readonly Label sessionValue = ValueLabel(18);
    private readonly Label ambientValue = new()
    {
        AutoSize = true,
        ForeColor = Secondary,
        Font = new Font("Segoe UI", 10),
        Text = "Ambient Ops · Not connected",
    };
    private readonly Label sourceValue = new()
    {
        AutoSize = true,
        ForeColor = Secondary,
        Font = new Font("Segoe UI", 9),
    };
    private bool allowClose;

    public DashboardForm(string sessionsRoot)
    {
        Text = "Codex TPS";
        BackColor = Background;
        ForeColor = Primary;
        ClientSize = new Size(640, 390);
        MinimumSize = new Size(560, 360);
        StartPosition = FormStartPosition.CenterScreen;
        ShowInTaskbar = true;
        sourceValue.Text = sessionsRoot;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 22, 28, 20),
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Background,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        Controls.Add(root);

        var hero = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Background,
        };
        hero.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = Secondary,
            Font = new Font("Segoe UI Semibold", 10),
            Text = "TOTAL TPS",
        });
        hero.Controls.Add(totalValue);
        root.Controls.Add(hero, 0, 0);

        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Background,
        };
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
        metrics.Controls.Add(Metric("INPUT", inputValue), 0, 0);
        metrics.Controls.Add(Metric("OUTPUT", outputValue), 1, 0);
        metrics.Controls.Add(Metric("SESSIONS", sessionValue), 2, 0);
        root.Controls.Add(metrics, 0, 1);
        root.Controls.Add(ambientValue, 0, 2);
        root.Controls.Add(sourceValue, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            BackColor = Background,
        };
        var settingsButton = new Button
        {
            Text = "Settings",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Primary,
            BackColor = Color.FromArgb(31, 37, 42),
        };
        settingsButton.FlatAppearance.BorderColor = Color.FromArgb(62, 72, 80);
        settingsButton.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        var refreshButton = new Button
        {
            Text = "Refresh",
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Primary,
            BackColor = Color.FromArgb(31, 37, 42),
        };
        refreshButton.FlatAppearance.BorderColor = Color.FromArgb(62, 72, 80);
        refreshButton.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        actions.Controls.Add(settingsButton);
        actions.Controls.Add(refreshButton);
        root.Controls.Add(actions, 0, 4);

        FormClosing += (_, eventArgs) =>
        {
            if (!allowClose && eventArgs.CloseReason == CloseReason.UserClosing)
            {
                eventArgs.Cancel = true;
                Hide();
            }
        };
    }

    public event EventHandler? SettingsRequested;
    public event EventHandler? RefreshRequested;

    public void UpdateSnapshot(UsageSnapshot snapshot, string ambientStatus)
    {
        var metrics = snapshot.OneMinute;
        totalValue.Text = snapshot.Status == CollectionStatus.Ready
            ? FormatRate(metrics.TokensPerSecond)
            : "--";
        inputValue.Text =
            $"{FormatRate(metrics.InputTokensPerSecond)}\nCACHE {FormatRate(metrics.CachedInputTokensPerSecond)}";
        outputValue.Text =
            $"{FormatRate(metrics.OutputTokensPerSecond)}\nREASON {FormatRate(metrics.ReasoningTokensPerSecond)}";
        sessionValue.Text = snapshot.ActiveSessions.ToString();
        ambientValue.Text = $"Ambient Ops · {ambientStatus}";
    }

    public void UpdateSessionsRoot(string sessionsRoot) => sourceValue.Text = sessionsRoot;

    public void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    public void CloseForExit()
    {
        allowClose = true;
        Close();
    }

    private static Control Metric(string title, Label value)
    {
        var panel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Background,
            Padding = new Padding(0, 12, 10, 0),
        };
        panel.Controls.Add(new Label
        {
            AutoSize = true,
            ForeColor = Secondary,
            Font = new Font("Segoe UI Semibold", 9),
            Text = title,
        });
        panel.Controls.Add(value);
        return panel;
    }

    private static Label ValueLabel(float size) => new()
    {
        AutoSize = true,
        ForeColor = Primary,
        Font = new Font("Segoe UI Semibold", size),
        Margin = new Padding(0, 4, 0, 0),
    };

    private static string FormatRate(double value) => Math.Abs(value) switch
    {
        >= 1_000_000 => $"{value / 1_000_000:0.00}M",
        >= 1_000 => $"{value / 1_000:0.00}K",
        _ => $"{value:0.00}",
    };
}
