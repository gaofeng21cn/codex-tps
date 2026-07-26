using CodexTPS.Core;
using System.Globalization;

namespace CodexTPS.WindowsApp;

internal sealed class DashboardForm : RoundedPopupForm
{
    private static readonly Color Background = Color.White;
    private static readonly Color Primary = Color.FromArgb(36, 36, 38);
    private static readonly Color Secondary = Color.FromArgb(128, 128, 132);
    private static readonly Color Tertiary = Color.FromArgb(178, 178, 182);
    private static readonly Color Border = Color.FromArgb(224, 224, 226);
    private static readonly Color Success = Color.FromArgb(52, 199, 89);
    private static readonly Color Warning = Color.FromArgb(255, 149, 0);
    private static readonly Color Failure = Color.FromArgb(255, 59, 48);
    private static readonly Color InputAccent = Color.FromArgb(0, 122, 255);
    private static readonly Color CacheAccent = Color.FromArgb(0, 188, 212);
    private static readonly Color OutputAccent = Color.FromArgb(255, 149, 0);
    private static readonly Color ReasoningAccent = Color.FromArgb(191, 64, 209);

    private readonly Label statusDot = TextLabel(7, Success, text: "●");
    private readonly Label statusValue = TextLabel(9, Secondary);
    private readonly HeroMetricsControl heroMetrics = new();
    private readonly Label inputValue = MetricValueLabel();
    private readonly Label cacheValue = MetricValueLabel();
    private readonly Label outputValue = MetricValueLabel();
    private readonly Label reasoningValue = MetricValueLabel();
    private readonly Label sessionValue = TextLabel(9, Secondary);
    private readonly Label cacheRatioValue = TextLabel(9, Secondary);
    private readonly AmbientStatusControl ambientStatus = new();
    private readonly MetricWindowSelector windowSelector = new();
    private readonly RefreshCadenceButton refreshCadence = new();
    private readonly ToggleSwitch startupToggle = new();
    private readonly ToolTip toolTip = new();
    private readonly Image applicationImage;
    private readonly Control ambientRow;
    private UsageSnapshot lastSnapshot = UsageSnapshot.Empty(
        DateTimeOffset.Now,
        CollectionStatus.SessionsDirectoryMissing);
    private AmbientOpsConnectionStatus lastConnection = new(
        AmbientOpsConnectionKind.Discovering,
        "正在连接");
    private string sessionsRoot = string.Empty;
    private bool allowClose;
    private bool suppressFooterEvents;

    public DashboardForm(string sessionsRoot)
    {
        this.sessionsRoot = sessionsRoot;
        Text = "Codex TPS";
        AccessibleName = "Codex TPS";
        BackColor = Background;
        ForeColor = Primary;
        Font = new Font("Segoe UI Variable Text", 9f);
        ClientSize = new Size(380, 408);
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        KeyPreview = true;

        using var iconStream = typeof(DashboardForm).Assembly.GetManifestResourceStream(
            "CodexTPS.AppIcon.png") ?? throw new InvalidOperationException("App icon resource is missing.");
        using var sourceImage = Image.FromStream(iconStream);
        applicationImage = new Bitmap(sourceImage);
        var executableIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        if (executableIcon is not null)
        {
            Icon = (Icon)executableIcon.Clone();
            executableIcon.Dispose();
        }

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = Background,
            Margin = Padding.Empty,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 236));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 1));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(root);

        root.Controls.Add(BuildHeader(), 0, 0);
        root.Controls.Add(Separator(), 0, 1);
        root.Controls.Add(BuildThroughput(), 0, 2);
        root.Controls.Add(Separator(), 0, 3);
        ambientRow = BuildAmbientRow();
        root.Controls.Add(ambientRow, 0, 4);
        root.Controls.Add(Separator(), 0, 5);
        root.Controls.Add(BuildFooter(), 0, 6);

        windowSelector.SelectedSecondsChanged += seconds =>
        {
            windowSelector.SelectedSeconds = seconds;
            UpdateSnapshot(lastSnapshot, lastConnection);
        };
        refreshCadence.SecondsChanged += seconds =>
        {
            if (!suppressFooterEvents)
            {
                RefreshCadenceChanged?.Invoke(seconds);
            }
        };
        startupToggle.CheckedChanged += (_, _) =>
        {
            if (!suppressFooterEvents)
            {
                StartupChanged?.Invoke(startupToggle.Checked);
            }
        };

        toolTip.SetToolTip(ambientRow, "Ambient Ops 连接设置");
        FormClosing += OnFormClosing;
        KeyDown += (_, eventArgs) =>
        {
            if (eventArgs.Control && eventArgs.KeyCode == Keys.Oemcomma)
            {
                SettingsRequested?.Invoke(this, EventArgs.Empty);
                eventArgs.Handled = true;
            }
            else if (eventArgs.KeyCode == Keys.Escape)
            {
                HideToTray();
                eventArgs.Handled = true;
            }
        };
        ApplyInitialDpiScale();
    }

    public event EventHandler? SettingsRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? SessionsFolderRequested;
    public event EventHandler? ExitRequested;
    public event Action<int>? RefreshCadenceChanged;
    public event Action<bool>? StartupChanged;

    public void UpdateSnapshot(
        UsageSnapshot snapshot,
        AmbientOpsConnectionStatus connection)
    {
        lastSnapshot = snapshot;
        lastConnection = connection;
        var metrics = SelectedMetrics(snapshot);
        var detailedRate = snapshot.Status == CollectionStatus.Ready
            ? FormatDetailed(metrics.TokensPerSecond)
            : "--";
        heroMetrics.SetValues(
            detailedRate,
            metrics.RequestsPerMinute.ToString("0.0", CultureInfo.CurrentCulture));
        inputValue.Text = FormatCompact(metrics.InputTokensPerSecond);
        cacheValue.Text = FormatCompact(metrics.CachedInputTokensPerSecond);
        outputValue.Text = FormatCompact(metrics.OutputTokensPerSecond);
        reasoningValue.Text = FormatCompact(metrics.ReasoningTokensPerSecond);
        sessionValue.Text = $"{snapshot.ActiveSessions} 个活跃会话";
        cacheRatioValue.Text = $"缓存占比 {metrics.CacheRatio:P0}";

        statusValue.Text = $"{CollectionLabel(snapshot)}  ·  {snapshot.GeneratedAt:HH:mm}";
        statusDot.ForeColor = snapshot.Status switch
        {
            CollectionStatus.Ready when snapshot.MalformedRelevantLines == 0 => Success,
            CollectionStatus.Ready => Warning,
            _ => Failure,
        };
        ambientStatus.SetConnection(connection);
        toolTip.SetToolTip(
            ambientRow,
            connection.Endpoint is null
                ? connection.Message
                : $"{connection.Message}\n{connection.Endpoint.AbsoluteUri}");
    }

    public void UpdateSessionsRoot(string nextSessionsRoot)
    {
        sessionsRoot = nextSessionsRoot;
    }

    public void SetRefreshCadence(int seconds)
    {
        suppressFooterEvents = true;
        try
        {
            refreshCadence.SetSeconds(seconds);
        }
        finally
        {
            suppressFooterEvents = false;
        }
    }

    public void SetStartupEnabled(bool enabled)
    {
        suppressFooterEvents = true;
        try
        {
            startupToggle.Checked = enabled;
        }
        finally
        {
            suppressFooterEvents = false;
        }
    }

    public void ShowFromTray()
    {
        WindowState = FormWindowState.Normal;
        if (!Visible)
        {
            Show();
        }
        var workingArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(
            workingArea.Right - Width - 12,
            workingArea.Bottom - Height - 12);
        BringToFront();
        Activate();
    }

    public void CloseForExit()
    {
        allowClose = true;
        Close();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            applicationImage.Dispose();
            toolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            BackColor = Background,
            Padding = new Padding(16),
            Margin = Padding.Empty,
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 44));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84));

        header.Paint += (_, eventArgs) => eventArgs.Graphics.DrawImage(
            applicationImage,
            new Rectangle(16, 17, 32, 32));

        var titles = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = Background,
            Margin = Padding.Empty,
        };
        titles.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        titles.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var title = TextLabel(
            12.5f,
            Primary,
            "Segoe UI Semibold",
            text: "Codex TPS");
        title.Margin = Padding.Empty;
        titles.Controls.Add(title, 0, 0);
        var status = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Background,
            Margin = Padding.Empty,
        };
        statusDot.Margin = new Padding(0, 2, 6, 0);
        statusValue.Margin = Padding.Empty;
        status.Controls.Add(statusDot);
        status.Controls.Add(statusValue);
        titles.Controls.Add(status, 0, 1);
        header.Controls.Add(titles, 1, 0);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Background,
            Margin = Padding.Empty,
        };
        var refreshButton = HeaderButton("\uE72C", "立即刷新");
        refreshButton.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        var settingsButton = HeaderButton("\uE713", "打开设置");
        settingsButton.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        var folderButton = HeaderButton("\uE8B7", "打开 Codex 会话目录");
        folderButton.Click += (_, _) => SessionsFolderRequested?.Invoke(this, EventArgs.Empty);
        toolTip.SetToolTip(folderButton, sessionsRoot);
        actions.Controls.Add(refreshButton);
        actions.Controls.Add(settingsButton);
        actions.Controls.Add(folderButton);
        header.Controls.Add(actions, 2, 0);

        EnableWindowDrag(header);
        EnableWindowDrag(titles);
        EnableWindowDrag(title);
        EnableWindowDrag(status);
        EnableWindowDrag(statusDot);
        EnableWindowDrag(statusValue);
        return header;
    }

    private Control BuildThroughput()
    {
        var throughput = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = Background,
            Padding = new Padding(16),
            Margin = Padding.Empty,
        };
        throughput.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        throughput.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        throughput.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        throughput.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        throughput.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        throughput.RowStyles.Add(new RowStyle(SizeType.Absolute, 12));
        throughput.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        windowSelector.Dock = DockStyle.Fill;
        windowSelector.Margin = Padding.Empty;
        throughput.Controls.Add(windowSelector, 0, 0);
        throughput.Controls.Add(BuildHero(), 0, 2);
        throughput.Controls.Add(BuildMetrics(), 0, 4);
        throughput.Controls.Add(BuildSummary(), 0, 6);
        return throughput;
    }

    private Control BuildHero()
    {
        heroMetrics.Dock = DockStyle.Fill;
        heroMetrics.Margin = Padding.Empty;
        return heroMetrics;
    }

    private Control BuildMetrics()
    {
        var metrics = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            BackColor = Background,
            Margin = Padding.Empty,
        };
        for (var index = 0; index < 4; index++)
        {
            metrics.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        }
        metrics.Controls.Add(MetricColumn("输入", inputValue, InputAccent), 0, 0);
        metrics.Controls.Add(MetricColumn("缓存", cacheValue, CacheAccent), 1, 0);
        metrics.Controls.Add(MetricColumn("输出", outputValue, OutputAccent), 2, 0);
        metrics.Controls.Add(MetricColumn("推理", reasoningValue, ReasoningAccent), 3, 0);
        return metrics;
    }

    private Control BuildSummary()
    {
        var summary = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            BackColor = Background,
            Margin = Padding.Empty,
        };
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
        summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        var sessions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Background,
            Margin = Padding.Empty,
        };
        var sessionsIcon = TextLabel(10, Secondary, "Segoe MDL2 Assets", text: "\uE81E");
        sessionsIcon.Margin = new Padding(0, 0, 7, 0);
        sessionValue.Margin = Padding.Empty;
        sessions.Controls.Add(sessionsIcon);
        sessions.Controls.Add(sessionValue);
        summary.Controls.Add(sessions, 0, 0);
        cacheRatioValue.Anchor = AnchorStyles.Right | AnchorStyles.Top;
        cacheRatioValue.Margin = Padding.Empty;
        summary.Controls.Add(cacheRatioValue, 1, 0);
        return summary;
    }

    private Control BuildAmbientRow()
    {
        ambientStatus.Dock = DockStyle.Fill;
        ambientStatus.Margin = Padding.Empty;
        ambientStatus.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        return ambientStatus;
    }

    private Control BuildFooter()
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            BackColor = Background,
            Padding = new Padding(16, 7, 12, 7),
            Margin = Padding.Empty,
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 32));

        var cadence = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Background,
            Margin = Padding.Empty,
        };
        var cadenceLabel = TextLabel(9.5f, Primary, text: "自动刷新");
        cadenceLabel.Margin = new Padding(0, 6, 9, 0);
        refreshCadence.Margin = Padding.Empty;
        cadence.Controls.Add(cadenceLabel);
        cadence.Controls.Add(refreshCadence);
        footer.Controls.Add(cadence, 0, 0);

        var startup = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Background,
            Margin = Padding.Empty,
        };
        var startupLabel = TextLabel(9.5f, Primary, text: "登录时启动");
        startupLabel.Margin = new Padding(0, 6, 8, 0);
        startupToggle.Margin = new Padding(0, 3, 0, 0);
        startup.Controls.Add(startupLabel);
        startup.Controls.Add(startupToggle);
        footer.Controls.Add(startup, 1, 0);

        var exitButton = HeaderButton("\uE7E8", "退出 Codex TPS");
        exitButton.Anchor = AnchorStyles.Right;
        exitButton.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        footer.Controls.Add(exitButton, 2, 0);
        return footer;
    }

    private static Button HeaderButton(string glyph, string accessibleName)
    {
        var button = new MacIconButton
        {
            Text = glyph,
            AccessibleName = accessibleName,
            Size = new Size(28, 28),
            FlatStyle = FlatStyle.Flat,
            Font = new Font("Segoe MDL2 Assets", 12),
            ForeColor = Secondary,
            BackColor = Background,
            Cursor = Cursors.Hand,
            Margin = Padding.Empty,
            TabStop = true,
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseOverBackColor = Color.FromArgb(242, 242, 244);
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(232, 232, 234);
        return button;
    }

    private static Control MetricColumn(string title, Label value, Color accent)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            BackColor = Background,
            Padding = new Padding(0, 0, 4, 0),
            Margin = Padding.Empty,
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 18));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 21));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Background,
            Margin = Padding.Empty,
        };
        var dot = TextLabel(6, accent, text: "●");
        dot.Margin = new Padding(0, 2, 5, 0);
        var label = TextLabel(8.5f, Secondary, text: title);
        label.Margin = Padding.Empty;
        heading.Controls.Add(dot);
        heading.Controls.Add(label);
        panel.Controls.Add(heading, 0, 0);
        value.Margin = Padding.Empty;
        panel.Controls.Add(value, 0, 1);
        var unit = TextLabel(8, Tertiary, text: "token/s");
        unit.Margin = new Padding(0, 1, 0, 0);
        panel.Controls.Add(unit, 0, 2);
        return panel;
    }

    private static Label TextLabel(
        float size,
        Color color,
        string family = "Microsoft YaHei UI",
        FontStyle style = FontStyle.Regular,
        string text = "") => new()
        {
            Text = text,
            AutoSize = true,
            ForeColor = color,
            BackColor = Color.Transparent,
            Font = new Font(family, size, style),
            Margin = Padding.Empty,
        };

    private static Label MetricValueLabel() =>
        TextLabel(11, Primary, "Segoe UI Semibold");

    private static Panel Separator() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = Border,
        Margin = Padding.Empty,
    };

    private WindowMetrics SelectedMetrics(UsageSnapshot snapshot) =>
        windowSelector.SelectedSeconds switch
        {
            300 => snapshot.FiveMinutes,
            1_800 => snapshot.ThirtyMinutes,
            3_600 => snapshot.OneHour,
            _ => snapshot.OneMinute,
        };

    private void OnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (!allowClose && eventArgs.CloseReason == CloseReason.UserClosing)
        {
            eventArgs.Cancel = true;
            HideToTray();
        }
    }

    private void HideToTray()
    {
        Hide();
        WindowState = FormWindowState.Normal;
    }

    private static string CollectionLabel(UsageSnapshot snapshot) => snapshot.Status switch
    {
        CollectionStatus.Ready when snapshot.MalformedRelevantLines == 0 => "就绪",
        CollectionStatus.Ready => "部分记录无法解析",
        CollectionStatus.SessionsDirectoryMissing => "未找到会话目录",
        _ => "读取失败",
    };

    private static string FormatDetailed(double value) => Math.Abs(value) >= 1_000
        ? value.ToString("#,##0", CultureInfo.CurrentCulture)
        : value.ToString("0.0", CultureInfo.CurrentCulture);

    private static string FormatCompact(double value) => Math.Abs(value) switch
    {
        >= 1_000_000 => $"{value / 1_000_000:0.0}M",
        >= 1_000 => $"{value / 1_000:0.0}k",
        >= 10 => $"{value:0}",
        _ => $"{value:0.0}",
    };
}
