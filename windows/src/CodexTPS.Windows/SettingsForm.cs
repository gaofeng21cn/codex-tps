using CodexTPS.Core;

namespace CodexTPS.WindowsApp;

internal sealed class SettingsForm : Form
{
    private readonly TextBox codexHome = new();
    private readonly CheckBox ambientEnabled = new() { Text = "Send aggregate metrics" };
    private readonly CheckBox autoDiscover = new() { Text = "Discover on local network" };
    private readonly TextBox manualUrl = new();
    private readonly TextBox token = new() { UseSystemPasswordChar = true };
    private readonly TextBox preferredInstance = new();
    private readonly TextBox machineId = new();
    private readonly TextBox machineName = new();
    private readonly CheckBox petEnabled = new() { Text = "Report Ledger Owl" };
    private readonly CheckBox startWithWindows = new() { Text = "Start with Windows" };

    public SettingsForm(AppSettings settings)
    {
        Text = "Codex TPS Settings";
        ClientSize = new Size(610, 620);
        MinimumSize = new Size(560, 590);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;

        codexHome.Text = settings.CodexHome;
        ambientEnabled.Checked = settings.AmbientEnabled;
        autoDiscover.Checked = settings.AutoDiscover;
        manualUrl.Text = settings.ManualUrl;
        token.Text = settings.Token;
        preferredInstance.Text = settings.PreferredInstanceId;
        machineId.Text = settings.MachineId;
        machineName.Text = settings.MachineName;
        petEnabled.Checked = settings.PetEnabled;
        startWithWindows.Checked = settings.StartWithWindows;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(20),
            ColumnCount = 2,
            RowCount = 12,
            AutoScroll = true,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        Controls.Add(layout);

        var browse = new Button { Text = "Browse", AutoSize = true };
        browse.Click += (_, _) => BrowseCodexHome();
        var homePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        homePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        homePanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        homePanel.Controls.Add(codexHome, 0, 0);
        homePanel.Controls.Add(browse, 1, 0);
        codexHome.Dock = DockStyle.Fill;
        AddRow(layout, 0, "Codex home", homePanel);
        AddRow(layout, 1, "Ambient Ops", ambientEnabled);
        AddRow(layout, 2, "Discovery", autoDiscover);
        AddRow(layout, 3, "Manual URL", manualUrl);
        AddRow(layout, 4, "Push token", token);
        AddRow(layout, 5, "Preferred instance", preferredInstance);
        AddRow(layout, 6, "Machine ID", machineId);
        AddRow(layout, 7, "Machine name", machineName);
        AddRow(layout, 8, "Pet", petEnabled);
        AddRow(layout, 9, "Startup", startWithWindows);

        var note = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            MaximumSize = new Size(520, 0),
            Text = "The token is encrypted with Windows DPAPI for the current user. " +
                "Only aggregate counters and optional pet state are sent.",
        };
        layout.SetColumnSpan(note, 2);
        layout.Controls.Add(note, 0, 10);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
        };
        var save = new Button { Text = "Save", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        save.Click += (_, _) => SaveSettings();
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        layout.SetColumnSpan(buttons, 2);
        layout.Controls.Add(buttons, 0, 11);
        AcceptButton = save;
        CancelButton = cancel;
    }

    public AppSettings? ResultSettings { get; private set; }

    private void SaveSettings()
    {
        try
        {
            _ = new AmbientOpsMachineIdentity(machineId.Text.Trim(), machineName.Text.Trim(), "Windows");
            if (ambientEnabled.Checked && !autoDiscover.Checked &&
                (!Uri.TryCreate(manualUrl.Text.Trim(), UriKind.Absolute, out var endpoint) ||
                 endpoint.Scheme is not ("http" or "https")))
            {
                throw new InvalidOperationException("Enter a valid Ambient Ops HTTP(S) URL.");
            }
            ResultSettings = new AppSettings
            {
                CodexHome = codexHome.Text.Trim(),
                AmbientEnabled = ambientEnabled.Checked,
                AutoDiscover = autoDiscover.Checked,
                ManualUrl = manualUrl.Text.Trim(),
                Token = token.Text,
                PreferredInstanceId = preferredInstance.Text.Trim(),
                MachineId = machineId.Text.Trim(),
                MachineName = machineName.Text.Trim(),
                PetEnabled = petEnabled.Checked,
                StartWithWindows = startWithWindows.Checked,
            };
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            MessageBox.Show(this, error.Message, "Invalid settings", MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void BrowseCodexHome()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Select the Codex home folder containing sessions",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false,
        };
        if (Directory.Exists(codexHome.Text))
        {
            dialog.SelectedPath = codexHome.Text;
        }
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            codexHome.Text = dialog.SelectedPath;
        }
    }

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.Controls.Add(new Label
        {
            Text = label,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
        }, 0, row);
        control.Dock = DockStyle.Fill;
        layout.Controls.Add(control, 1, row);
    }
}
