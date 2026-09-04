using System.Diagnostics;
using System.Drawing.Text;
using System.Globalization;
using System.Runtime.InteropServices;

namespace LocalTransfer;

internal sealed class MainForm : Form
{
    private static readonly string UiFontFamily = ResolveFontFamily("Segoe UI Variable Text", "Segoe UI");
    private static readonly string DisplayFontFamily = ResolveFontFamily("Segoe UI Variable Display", "Segoe UI");
    private static readonly Color BackgroundColor = Color.FromArgb(248, 250, 252);
    private static readonly Color CardColor = Color.White;
    private static readonly Color TextColor = Color.FromArgb(15, 23, 42);
    private static readonly Color MutedColor = Color.FromArgb(71, 85, 105);
    private static readonly Color PrimaryColor = Color.FromArgb(37, 99, 235);
    private static readonly Color SuccessColor = Color.FromArgb(22, 101, 52);
    private static readonly Color SuccessBackground = Color.FromArgb(240, 253, 244);
    private static readonly Color BorderColor = Color.FromArgb(226, 232, 240);
    private static readonly Color DarkBackgroundColor = Color.FromArgb(11, 18, 32);
    private static readonly Color DarkCardColor = Color.FromArgb(17, 27, 46);
    private static readonly Color DarkTextColor = Color.FromArgb(248, 250, 252);
    private static readonly Color DarkMutedColor = Color.FromArgb(203, 213, 225);
    private static readonly Color DarkBorderColor = Color.FromArgb(51, 65, 85);
    private static readonly Color DarkPrimaryColor = Color.FromArgb(96, 165, 250);

    private readonly AppSettings _settings;
    private readonly Dictionary<Control, string> _localizedControls = [];
    private string _uploadFolder;
    private bool _darkMode;
    private string _languageCode;
    private readonly PictureBox _qrPicture = new();
    private readonly Label _statusLabel = new();
    private readonly Label _urlLabel = new();
    private readonly Label _deviceLabel = new();
    private readonly Label _emptyHistoryLabel = new();
    private readonly ListView _historyList = new();
    private readonly Label _folderLabel = new();
    private readonly Label _outgoingLabel = new();
    private readonly Button _clearOutgoingButton;
    private readonly ThemeToggleButton _themeButton = new();
    private readonly FlatComboBox _languageSelector = new();
    private readonly Button _copyButton;
    private readonly Button _refreshButton;
    private LocalTransferServer? _server;
    private StatusKind _statusKind = StatusKind.Neutral;
    private string _statusKey = "status.starting";
    private object[] _statusArguments = [];
    private string _deviceStateKey = "device.none";
    private string? _connectedAddress;
    private IReadOnlyList<OutgoingFile> _outgoingFiles = [];
    private bool _changingLanguage;

    public MainForm()
    {
        _settings = AppSettings.Load();
        _uploadFolder = _settings.ResolveUploadFolder();
        _darkMode = _settings.DarkMode;
        _languageCode = Localizer.ResolveLanguage(_settings.Language);

        Text = "local-transfer";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 680);
        Size = new Size(1080, 760);
        BackColor = BackgroundColor;
        ForeColor = TextColor;
        Font = UiFont(10F);
        AutoScaleMode = AutoScaleMode.Dpi;
        KeyPreview = true;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }

        _copyButton = CreateButton("Copy connection", primary: true);
        _copyButton.Click += (_, _) => CopyConnectionLink();
        _copyButton.Enabled = false;

        _refreshButton = CreateButton("Create new QR");
        _refreshButton.Click += (_, _) => RefreshConnection();
        _refreshButton.Enabled = false;

        _clearOutgoingButton = CreateButton("Clear list");
        _clearOutgoingButton.Enabled = false;
        _clearOutgoingButton.Click += (_, _) => ClearOutgoingFiles();

        Controls.Add(BuildLayout());
        ConfigureLanguageSelector();
        ApplyLanguage(refreshConnection: false);
        ApplyTheme(_darkMode);
        HandleCreated += (_, _) => UpdateTitleBarTheme();
        Shown += async (_, _) => await StartServerAsync();
        FormClosing += OnFormClosing;
    }

    private Control BuildLayout()
    {
        var page = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 20, 24, 22),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = BackgroundColor
        };
        page.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        page.Controls.Add(BuildHeader(), 0, 0);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        content.Controls.Add(BuildConnectionCard(), 0, 0);
        content.Controls.Add(BuildRightColumn(), 1, 0);
        page.Controls.Add(content, 0, 1);
        return page;
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var titleStack = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        var mark = new BrandMark { Location = new Point(0, 5), Size = new Size(44, 44) };
        var title = new Label
        {
            Text = "local-transfer",
            AutoSize = true,
            Font = DisplayFont(21F, FontStyle.Bold),
            ForeColor = TextColor,
            Location = new Point(58, 0)
        };
        var subtitle = new Label
        {
            Text = "Direct, offline transfers between iPhone and computer",
            AutoSize = true,
            ForeColor = MutedColor,
            Location = new Point(60, 40)
        };
        titleStack.Controls.Add(mark);
        titleStack.Controls.Add(title);
        titleStack.Controls.Add(subtitle);

        _statusLabel.Text = "Starting";
        _statusLabel.AutoSize = true;
        _statusLabel.Padding = new Padding(14, 9, 14, 9);
        _statusLabel.BackColor = Color.FromArgb(241, 245, 249);
        _statusLabel.ForeColor = MutedColor;
        _statusLabel.Font = UiFont(9F, FontStyle.Bold);
        _statusLabel.Margin = new Padding(8, 6, 0, 0);
        _statusLabel.AccessibleName = "Connection status";

        _themeButton.Size = new Size(44, 44);
        _themeButton.Margin = Padding.Empty;
        _themeButton.Cursor = Cursors.Hand;
        _themeButton.FlatStyle = FlatStyle.Flat;
        _themeButton.FlatAppearance.BorderSize = 1;
        _themeButton.Click += (_, _) => ToggleTheme();

        var headerActions = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = new Padding(0, 5, 0, 0),
            BackColor = Color.Transparent
        };
        headerActions.Controls.Add(_languageSelector);
        headerActions.Controls.Add(_themeButton);
        headerActions.Controls.Add(_statusLabel);

        header.Controls.Add(titleStack, 0, 0);
        header.Controls.Add(headerActions, 1, 0);
        return header;
    }

    private Control BuildConnectionCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 16, 0),
            Padding = new Padding(24),
            BackColor = CardColor
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 260));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));

        var label = new Label
        {
            Text = "Connect from your phone",
            AutoSize = true,
            Dock = DockStyle.Top,
            Font = DisplayFont(15F, FontStyle.Bold),
            ForeColor = TextColor
        };

        _qrPicture.Dock = DockStyle.Fill;
        _qrPicture.SizeMode = PictureBoxSizeMode.CenterImage;
        _qrPicture.BackColor = Color.White;
        _qrPicture.Tag = "qr";
        _qrPicture.AccessibleName = "Connection QR code to scan with iPhone";

        var instruction = new Label
        {
            Text = "Open the iPhone Camera app and scan the QR code.",
            AutoSize = true,
            MaximumSize = new Size(315, 0),
            ForeColor = MutedColor,
            Font = UiFont(9.5F),
            Margin = new Padding(0, 7, 0, 10)
        };

        _urlLabel.Text = "Preparing local address…";
        _urlLabel.AutoEllipsis = true;
        _urlLabel.Dock = DockStyle.Top;
        _urlLabel.Height = 32;
        _urlLabel.ForeColor = PrimaryColor;
        _urlLabel.Font = new Font("Consolas", 9F);
        _urlLabel.TextAlign = ContentAlignment.MiddleLeft;
        _urlLabel.AccessibleName = "Local connection address";

        var buttonRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty
        };
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        buttonRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        _copyButton.Margin = new Padding(0, 0, 8, 0);
        _refreshButton.Margin = new Padding(0);
        buttonRow.Controls.Add(_copyButton, 0, 0);
        buttonRow.Controls.Add(_refreshButton, 1, 0);

        layout.Controls.Add(label, 0, 0);
        layout.Controls.Add(_qrPicture, 0, 1);
        layout.Controls.Add(instruction, 0, 2);
        layout.Controls.Add(_urlLabel, 0, 3);
        layout.Controls.Add(new Panel(), 0, 4);
        layout.Controls.Add(buttonRow, 0, 5);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildRightColumn()
    {
        var right = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 0, 0, 0)
        };
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 31));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 25));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 44));
        right.Controls.Add(BuildStepsCard(), 0, 0);
        right.Controls.Add(BuildOutgoingCard(), 0, 1);
        right.Controls.Add(BuildHistoryCard(), 0, 2);
        return right;
    }

    private Control BuildOutgoingCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 14),
            Padding = new Padding(22, 16, 22, 16),
            BackColor = CardColor
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 146));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var title = new Label
        {
            Text = "Computer to phone",
            AutoSize = true,
            Font = DisplayFont(13.5F, FontStyle.Bold),
            ForeColor = TextColor
        };
        var chooseButton = CreateButton("Choose files");
        chooseButton.Margin = new Padding(0, 0, 0, 4);
        chooseButton.Click += (_, _) => SelectOutgoingFiles();

        _outgoingLabel.Text = "Choose files you want to download on your phone.";
        _outgoingLabel.AutoSize = false;
        _outgoingLabel.Dock = DockStyle.Fill;
        _outgoingLabel.ForeColor = MutedColor;
        _outgoingLabel.Font = UiFont(8.8F);
        _outgoingLabel.Margin = new Padding(0, 4, 12, 0);

        _clearOutgoingButton.Dock = DockStyle.Fill;
        _clearOutgoingButton.Margin = new Padding(0, 4, 0, 0);
        layout.Controls.Add(title, 0, 0);
        layout.Controls.Add(chooseButton, 1, 0);
        layout.Controls.Add(_outgoingLabel, 0, 1);
        layout.Controls.Add(_clearOutgoingButton, 1, 1);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildStepsCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 14),
            Padding = new Padding(22, 16, 22, 14),
            BackColor = CardColor
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 33));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 34));

        layout.Controls.Add(new Label
        {
            Text = "Transfer in three steps",
            AutoSize = true,
            Font = DisplayFont(14F, FontStyle.Bold),
            ForeColor = TextColor
        }, 0, 0);
        layout.Controls.Add(CreateStep("1", "Join the same network", "No internet is needed; the same Wi-Fi network is enough."), 0, 1);
        layout.Controls.Add(CreateStep("2", "Scan the QR code", "A temporary session page opens in Safari."), 0, 2);
        layout.Controls.Add(CreateStep("3", "Choose photos or files", "Files transfer directly to this computer."), 0, 3);
        card.Controls.Add(layout);
        return card;
    }

    private Control BuildHistoryCard()
    {
        var card = new CardPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(22, 16, 22, 16),
            BackColor = CardColor
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));

        var historyHeader = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        historyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        historyHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        var titlePanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = Padding.Empty };
        titlePanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        titlePanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        titlePanel.Controls.Add(new Label
        {
            Text = "Received files",
            AutoSize = true,
            Font = DisplayFont(14F, FontStyle.Bold),
            ForeColor = TextColor
        }, 0, 0);
        _folderLabel.Text = _uploadFolder;
        _folderLabel.AutoEllipsis = true;
        _folderLabel.Dock = DockStyle.Fill;
        _folderLabel.ForeColor = MutedColor;
        _folderLabel.Font = UiFont(8F);
        _folderLabel.AccessibleName = "Download folder";
        titlePanel.Controls.Add(_folderLabel, 0, 1);
        historyHeader.Controls.Add(titlePanel, 0, 0);
        _deviceLabel.Text = "No phone connected yet";
        _deviceLabel.AutoSize = false;
        _deviceLabel.Dock = DockStyle.Fill;
        _deviceLabel.AutoEllipsis = true;
        _deviceLabel.TextAlign = ContentAlignment.TopRight;
        _deviceLabel.ForeColor = MutedColor;
        _deviceLabel.Font = UiFont(8.5F);
        _deviceLabel.Margin = new Padding(8, 4, 0, 0);
        historyHeader.Controls.Add(_deviceLabel, 1, 0);

        var historyHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
        _historyList.Dock = DockStyle.Fill;
        _historyList.View = View.Details;
        _historyList.FullRowSelect = true;
        _historyList.HeaderStyle = ColumnHeaderStyle.Nonclickable;
        _historyList.BorderStyle = BorderStyle.None;
        _historyList.OwnerDraw = true;
        _historyList.BackColor = CardColor;
        _historyList.ForeColor = TextColor;
        _historyList.Font = UiFont(9F);
        _historyList.Columns.Add("File", 220);
        _historyList.Columns.Add("Size", 84);
        _historyList.Columns.Add("Time", 64);
        _historyList.SizeChanged += (_, _) => ResizeHistoryColumns();
        _historyList.DrawColumnHeader += DrawHistoryColumnHeader;
        _historyList.DrawItem += DrawHistoryItem;
        _historyList.DrawSubItem += DrawHistorySubItem;
        _historyList.DoubleClick += (_, _) => OpenSelectedFile();
        _historyList.AccessibleName = "Received files list";

        _emptyHistoryLabel.Text = "Transferred files will appear here.";
        _emptyHistoryLabel.Dock = DockStyle.Fill;
        _emptyHistoryLabel.TextAlign = ContentAlignment.MiddleCenter;
        _emptyHistoryLabel.ForeColor = MutedColor;
        _emptyHistoryLabel.BackColor = CardColor;
        historyHost.Controls.Add(_historyList);
        historyHost.Controls.Add(_emptyHistoryLabel);
        _emptyHistoryLabel.BringToFront();

        var footerButtons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = Padding.Empty };
        footerButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        footerButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var openFolderButton = CreateButton("Open folder");
        openFolderButton.Margin = new Padding(0, 0, 8, 0);
        openFolderButton.Click += (_, _) => OpenUploadFolder();
        var changeFolderButton = CreateButton("Change folder");
        changeFolderButton.Margin = Padding.Empty;
        changeFolderButton.Click += (_, _) => ChangeUploadFolder();
        footerButtons.Controls.Add(openFolderButton, 0, 0);
        footerButtons.Controls.Add(changeFolderButton, 1, 0);

        layout.Controls.Add(historyHeader, 0, 0);
        layout.Controls.Add(historyHost, 0, 1);
        layout.Controls.Add(footerButtons, 0, 2);
        card.Controls.Add(layout);
        return card;
    }

    private static Control CreateStep(string number, string title, string description)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            ColumnCount = 2,
            RowCount = 2
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 40));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        var numberLabel = new Label
        {
            Text = number,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 10, 3),
            BackColor = Color.FromArgb(239, 246, 255),
            ForeColor = PrimaryColor,
            TextAlign = ContentAlignment.MiddleCenter,
            Font = UiFont(8.5F, FontStyle.Bold)
        };
        var titleLabel = new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.BottomLeft,
            ForeColor = TextColor,
            Font = UiFont(9.2F, FontStyle.Bold)
        };
        var descriptionLabel = new Label
        {
            Text = description,
            Dock = DockStyle.Fill,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = MutedColor,
            Font = UiFont(8.2F)
        };
        panel.Controls.Add(numberLabel, 0, 0);
        panel.SetRowSpan(numberLabel, 2);
        panel.Controls.Add(titleLabel, 1, 0);
        panel.Controls.Add(descriptionLabel, 1, 1);
        return panel;
    }

    private static Button CreateButton(string text, bool primary = false)
    {
        var button = new FlatActionButton(primary)
        {
            Text = text,
            Dock = DockStyle.Fill,
            Cursor = Cursors.Hand,
            Font = UiFont(8.8F, FontStyle.Bold),
            TabStop = true,
            AccessibleName = text
        };
        return button;
    }

    private string T(string key, params object[] arguments) => Localizer.Get(_languageCode, key, arguments);

    private void ConfigureLanguageSelector()
    {
        _changingLanguage = true;
        _languageSelector.DropDownStyle = ComboBoxStyle.DropDownList;
        _languageSelector.FlatStyle = FlatStyle.Flat;
        _languageSelector.DrawMode = DrawMode.OwnerDrawFixed;
        _languageSelector.ItemHeight = 28;
        _languageSelector.Width = 154;
        _languageSelector.Margin = new Padding(0, 6, 10, 0);
        _languageSelector.Cursor = Cursors.Hand;
        _languageSelector.Font = UiFont(9F);
        _languageSelector.Items.Clear();
        _languageSelector.Items.AddRange(Localizer.Languages.Cast<object>().ToArray());
        _languageSelector.SelectedItem = Localizer.Languages.First(language => language.Code == _languageCode);
        _languageSelector.DrawItem += DrawLanguageItem;
        _languageSelector.SelectionChangeCommitted += (_, _) =>
        {
            if (_changingLanguage || _languageSelector.SelectedItem is not LanguageOption language)
                return;
            _languageCode = language.Code;
            _settings.Language = _languageCode;
            SaveSettings();
            ApplyLanguage(refreshConnection: true);
            ApplyTheme(_darkMode);
        };
        _changingLanguage = false;
    }

    private void DrawLanguageItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= _languageSelector.Items.Count)
            return;
        var selected = (e.State & DrawItemState.Selected) != 0;
        var background = selected
            ? (_darkMode ? Color.FromArgb(30, 58, 95) : Color.FromArgb(219, 234, 254))
            : (_darkMode ? DarkCardColor : CardColor);
        var foreground = _darkMode ? DarkTextColor : TextColor;
        using var brush = new SolidBrush(background);
        e.Graphics.FillRectangle(brush, e.Bounds);
        if (_languageSelector.Items[e.Index] is not LanguageOption language)
            return;
        TextRenderer.DrawText(e.Graphics, language.Name, _languageSelector.Font, Rectangle.Inflate(e.Bounds, -8, 0), foreground,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        if ((e.State & DrawItemState.Focus) != 0)
            e.DrawFocusRectangle();
    }

    private void ApplyLanguage(bool refreshConnection)
    {
        var rtl = Localizer.IsRightToLeft(_languageCode);
        RightToLeft = rtl ? RightToLeft.Yes : RightToLeft.No;
        RightToLeftLayout = rtl;
        TranslateStaticControls(this);

        _statusLabel.Text = T(_statusKey, _statusArguments);
        _deviceLabel.Text = _connectedAddress is null ? T(_deviceStateKey) : T("device.connected", _connectedAddress);
        UpdateOutgoingText();
        if (_historyList.Columns.Count == 3)
        {
            _historyList.Columns[0].Text = T("history.file");
            _historyList.Columns[1].Text = T("history.size");
            _historyList.Columns[2].Text = T("history.time");
        }
        _statusLabel.AccessibleName = T("access.status");
        _qrPicture.AccessibleName = T("access.qr");
        _urlLabel.AccessibleName = T("access.url");
        _folderLabel.AccessibleName = T("access.folder");
        _historyList.AccessibleName = T("access.history");
        _languageSelector.AccessibleName = T("language.label");
        _themeButton.AccessibleName = T(_darkMode ? "theme.toLight" : "theme.toDark");
        _languageSelector.Invalidate();
        if (refreshConnection && _server is not null)
            UpdateConnectionDisplay();
        PerformLayout();
    }

    private void TranslateStaticControls(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (control != _statusLabel && control != _deviceLabel && control != _outgoingLabel &&
                control != _urlLabel && control != _folderLabel && control != _languageSelector)
            {
                if (!_localizedControls.TryGetValue(control, out var key) && Localizer.TryFindEnglishKey(control.Text, out key))
                    _localizedControls[control] = key;
                if (_localizedControls.TryGetValue(control, out key))
                {
                    control.Text = T(key);
                    if (control is Button)
                        control.AccessibleName = control.Text;
                }
            }
            TranslateStaticControls(control);
        }
    }

    private void UpdateOutgoingText()
    {
        _outgoingLabel.Text = _outgoingFiles.Count switch
        {
            0 => T("outgoing.empty"),
            1 => T("outgoing.one", _outgoingFiles[0].FileName),
            _ => T("outgoing.many", _outgoingFiles.Count)
        };
    }

    private async Task StartServerAsync()
    {
        try
        {
            _server = new LocalTransferServer(_uploadFolder);
            _server.TransferCompleted += OnTransferCompleted;
            _server.DeviceConnected += OnDeviceConnected;
            await _server.StartAsync();
            UpdateConnectionDisplay();
        }
        catch (Exception ex)
        {
            SetStatus("status.failed", StatusKind.Error);
            _urlLabel.Text = T("connection.failed");
            MessageBox.Show(
                $"{T("error.server")}\n\n{ex.Message}",
                "local-transfer",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }

    private void UpdateConnectionDisplay()
    {
        var url = _server?.PrimaryUrl;
        if (url is not null)
            url += "&lang=" + Uri.EscapeDataString(_languageCode);
        _qrPicture.Image?.Dispose();

        if (url is null)
        {
            _qrPicture.Image = null;
            _urlLabel.Text = T("connection.noNetwork");
            SetStatus("status.waitingNetwork", StatusKind.Warning);
            _copyButton.Enabled = false;
            _refreshButton.Enabled = true;
            return;
        }

        _qrPicture.Image = QrBitmapFactory.Create(url);
        _urlLabel.Text = url.Split("?", 2)[0];
        _urlLabel.Tag = url;
        SetStatus("status.ready", StatusKind.Success);
        _copyButton.Enabled = true;
        _refreshButton.Enabled = true;
    }

    private void RefreshConnection()
    {
        if (_server is null)
            return;
        _server.RefreshConnection();
        _connectedAddress = null;
        _deviceStateKey = "device.waiting";
        _deviceLabel.Text = T(_deviceStateKey);
        UpdateConnectionDisplay();
    }

    private void CopyConnectionLink()
    {
        if (_urlLabel.Tag is not string url)
            return;
        Clipboard.SetText(url);
        var previous = _copyButton.Text;
        _copyButton.Text = T("button.copied");
        var timer = new System.Windows.Forms.Timer { Interval = 1600 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            timer.Dispose();
            if (!IsDisposed) _copyButton.Text = previous;
        };
        timer.Start();
    }

    private void OnDeviceConnected(string address)
    {
        if (IsDisposed) return;
        BeginInvoke(() =>
        {
            _connectedAddress = address;
            _deviceLabel.Text = T("device.connected", address);
        });
    }

    private void OnTransferCompleted(TransferRecord record)
    {
        if (IsDisposed) return;
        BeginInvoke(() =>
        {
            _emptyHistoryLabel.Visible = false;
            var item = new ListViewItem(record.FileName) { Tag = record.FullPath };
            item.SubItems.Add(FormatBytes(record.Size));
            item.SubItems.Add(record.ReceivedAt.ToString("HH:mm", CultureInfo.GetCultureInfo(_languageCode)));
            _historyList.Items.Insert(0, item);
            SetStatus("status.received", StatusKind.Success);
            var timer = new System.Windows.Forms.Timer { Interval = 2200 };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                timer.Dispose();
                if (!IsDisposed) SetStatus("status.ready", StatusKind.Success);
            };
            timer.Start();
        });
    }

    private void OpenUploadFolder()
    {
        Directory.CreateDirectory(_uploadFolder);
        Process.Start(new ProcessStartInfo("explorer.exe", _uploadFolder) { UseShellExecute = true });
    }

    private void ChangeUploadFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = T("folder.dialog"),
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            SelectedPath = Directory.Exists(_uploadFolder) ? _uploadFolder : string.Empty
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        try
        {
            var selectedFolder = Path.GetFullPath(dialog.SelectedPath);
            Directory.CreateDirectory(selectedFolder);
            _server?.SetUploadFolder(selectedFolder);
            _uploadFolder = selectedFolder;
            _folderLabel.Text = selectedFolder;
            _settings.UploadFolder = selectedFolder;
            SaveSettings();
            SetStatus("status.folderChanged", StatusKind.Success);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"{T("error.folder")}\n\n{ex.Message}", "local-transfer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SelectOutgoingFiles()
    {
        if (_server is null)
            return;

        using var dialog = new OpenFileDialog
        {
            Title = T("file.dialog"),
            Multiselect = true,
            CheckFileExists = true,
            Filter = T("file.filter")
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        _outgoingFiles = _server.SetOutgoingFiles(dialog.FileNames);
        UpdateOutgoingText();
        _clearOutgoingButton.Enabled = _outgoingFiles.Count > 0;
        SetStatus("status.outgoingReady", StatusKind.Success);
    }

    private void ClearOutgoingFiles()
    {
        _server?.ClearOutgoingFiles();
        _outgoingFiles = [];
        UpdateOutgoingText();
        _clearOutgoingButton.Enabled = false;
    }

    private void ToggleTheme()
    {
        _darkMode = !_darkMode;
        _settings.DarkMode = _darkMode;
        SaveSettings();
        ApplyTheme(_darkMode);
    }

    private void SaveSettings()
    {
        try { _settings.Save(); } catch { }
    }

    private void SetStatus(string key, StatusKind kind, params object[] arguments)
    {
        _statusKind = kind;
        _statusKey = key;
        _statusArguments = arguments;
        _statusLabel.Text = T(key, arguments);
        var dark = _darkMode;
        (_statusLabel.BackColor, _statusLabel.ForeColor) = kind switch
        {
            StatusKind.Success => (dark ? Color.FromArgb(18, 51, 33) : SuccessBackground, dark ? Color.FromArgb(134, 239, 172) : SuccessColor),
            StatusKind.Warning => (dark ? Color.FromArgb(66, 45, 13) : Color.FromArgb(255, 251, 235), dark ? Color.FromArgb(253, 186, 116) : Color.FromArgb(146, 64, 14)),
            StatusKind.Error => (dark ? Color.FromArgb(59, 23, 27) : Color.FromArgb(254, 242, 242), dark ? Color.FromArgb(252, 165, 165) : Color.FromArgb(153, 27, 27)),
            _ => (dark ? Color.FromArgb(25, 37, 58) : Color.FromArgb(241, 245, 249), dark ? DarkMutedColor : MutedColor)
        };
    }

    private void ApplyTheme(bool dark)
    {
        BackColor = dark ? DarkBackgroundColor : BackgroundColor;
        ForeColor = dark ? DarkTextColor : TextColor;
        ApplyThemeToChildren(this, dark);
        _themeButton.DarkMode = dark;
        _themeButton.AccessibleName = T(dark ? "theme.toLight" : "theme.toDark");
        _languageSelector.DarkMode = dark;
        SetStatus(_statusKey, _statusKind, _statusArguments);
        UpdateTitleBarTheme();
        Invalidate(true);
    }

    private void UpdateTitleBarTheme()
    {
        if (!IsHandleCreated || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            return;
        var enabled = _darkMode ? 1 : 0;
        if (DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int)) != 0)
            DwmSetWindowAttribute(Handle, 19, ref enabled, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr windowHandle, int attribute, ref int value, int size);

    private static void ApplyThemeToChildren(Control parent, bool dark)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is FlatActionButton actionButton)
            {
                actionButton.DarkMode = dark;
            }
            else if (control is Button button && control is not ThemeToggleButton)
            {
                var primary = Equals(button.Tag, "primary-button");
                button.BackColor = primary ? PrimaryColor : (dark ? DarkCardColor : CardColor);
                button.ForeColor = primary ? Color.White : (dark ? DarkTextColor : TextColor);
                button.FlatAppearance.BorderColor = primary ? PrimaryColor : (dark ? DarkBorderColor : BorderColor);
                button.FlatAppearance.MouseOverBackColor = primary
                    ? Color.FromArgb(29, 78, 216)
                    : (dark ? Color.FromArgb(25, 37, 58) : BackgroundColor);
                button.FlatAppearance.MouseDownBackColor = primary
                    ? Color.FromArgb(30, 64, 175)
                    : (dark ? Color.FromArgb(30, 41, 59) : Color.FromArgb(241, 245, 249));
            }
            else if (control.Tag as string != "qr")
            {
                control.BackColor = MapBackground(control.BackColor, dark);
                control.ForeColor = MapForeground(control.ForeColor, dark);
            }

            if (control is CardPanel card)
                card.BorderTone = dark ? DarkBorderColor : BorderColor;
            if (control is ListView list)
            {
                list.BackColor = dark ? DarkCardColor : CardColor;
                list.ForeColor = dark ? DarkTextColor : TextColor;
                list.Invalidate();
            }
            ApplyThemeToChildren(control, dark);
        }
    }

    private static Color MapBackground(Color color, bool dark)
    {
        if (color == BackgroundColor || color == DarkBackgroundColor) return dark ? DarkBackgroundColor : BackgroundColor;
        if (color == CardColor || color == DarkCardColor) return dark ? DarkCardColor : CardColor;
        if (color == Color.FromArgb(241, 245, 249) || color == Color.FromArgb(25, 37, 58)) return dark ? Color.FromArgb(25, 37, 58) : Color.FromArgb(241, 245, 249);
        if (color == Color.FromArgb(239, 246, 255) || color == Color.FromArgb(23, 37, 84)) return dark ? Color.FromArgb(23, 37, 84) : Color.FromArgb(239, 246, 255);
        if (color == SuccessBackground || color == Color.FromArgb(18, 51, 33)) return dark ? Color.FromArgb(18, 51, 33) : SuccessBackground;
        return color;
    }

    private static Color MapForeground(Color color, bool dark)
    {
        if (color == TextColor || color == DarkTextColor) return dark ? DarkTextColor : TextColor;
        if (color == MutedColor || color == DarkMutedColor) return dark ? DarkMutedColor : MutedColor;
        if (color == PrimaryColor || color == DarkPrimaryColor) return dark ? DarkPrimaryColor : PrimaryColor;
        if (color == SuccessColor || color == Color.FromArgb(134, 239, 172)) return dark ? Color.FromArgb(134, 239, 172) : SuccessColor;
        return color;
    }

    private void OpenSelectedFile()
    {
        if (_historyList.SelectedItems.Count == 0 || _historyList.SelectedItems[0].Tag is not string path)
            return;
        if (File.Exists(path))
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    }

    private void ResizeHistoryColumns()
    {
        if (_historyList.Columns.Count != 3 || _historyList.ClientSize.Width <= 0)
            return;

        var usableWidth = Math.Max(300, _historyList.ClientSize.Width);
        var sizeWidth = Math.Clamp((int)(usableWidth * 0.18), 76, 94);
        var timeWidth = Math.Clamp((int)(usableWidth * 0.14), 62, 76);
        _historyList.Columns[1].Width = sizeWidth;
        _historyList.Columns[2].Width = timeWidth;
        _historyList.Columns[0].Width = Math.Max(150, usableWidth - sizeWidth - timeWidth);
    }

    private void DrawHistoryColumnHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        var background = _darkMode ? Color.FromArgb(25, 37, 58) : Color.FromArgb(241, 245, 249);
        var foreground = _darkMode ? DarkTextColor : TextColor;
        var border = _darkMode ? DarkBorderColor : BorderColor;
        using var backgroundBrush = new SolidBrush(background);
        using var borderPen = new Pen(border);
        e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
        e.Graphics.DrawLine(borderPen, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        if (e.ColumnIndex > 0)
            e.Graphics.DrawLine(borderPen, e.Bounds.Left, e.Bounds.Top + 4, e.Bounds.Left, e.Bounds.Bottom - 5);

        var textBounds = Rectangle.Inflate(e.Bounds, -8, 0);
        using var headerFont = UiFont(8.5F, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, e.Header?.Text ?? string.Empty, headerFont, textBounds, foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private static void DrawHistoryItem(object? sender, DrawListViewItemEventArgs e)
    {
        if (e.Item.ListView?.View != View.Details)
            e.DrawDefault = true;
    }

    private void DrawHistorySubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        var item = e.Item;
        if (item is null)
            return;
        var selected = item.Selected;
        var background = selected
            ? (_darkMode ? Color.FromArgb(30, 58, 95) : Color.FromArgb(219, 234, 254))
            : (_darkMode ? DarkCardColor : CardColor);
        var foreground = _darkMode ? DarkTextColor : TextColor;
        using var backgroundBrush = new SolidBrush(background);
        e.Graphics.FillRectangle(backgroundBrush, e.Bounds);
        var textBounds = Rectangle.Inflate(e.Bounds, -7, 0);
        TextRenderer.DrawText(e.Graphics, e.SubItem?.Text ?? string.Empty, _historyList.Font, textBounds, foreground,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

        if (selected && e.ColumnIndex == _historyList.Columns.Count - 1 && _historyList.Focused)
            ControlPaint.DrawFocusRectangle(e.Graphics, item.Bounds, foreground, background);
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_server is null)
            return;
        var server = _server;
        _server = null;
        await server.DisposeAsync();
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        var value = (double)bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return unit == 0 ? $"{value:0} {units[unit]}" : $"{value:0.#} {units[unit]}";
    }

    private static string ResolveFontFamily(params string[] candidates)
    {
        using var fonts = new InstalledFontCollection();
        foreach (var candidate in candidates)
        {
            if (fonts.Families.Any(family => string.Equals(family.Name, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }
        return SystemFonts.MessageBoxFont?.FontFamily.Name ?? "Segoe UI";
    }

    private static Font UiFont(float size, FontStyle style = FontStyle.Regular) =>
        new(UiFontFamily, size, style, GraphicsUnit.Point);

    private static Font DisplayFont(float size, FontStyle style = FontStyle.Regular) =>
        new(DisplayFontFamily, size, style, GraphicsUnit.Point);

    private static System.Drawing.Drawing2D.GraphicsPath CreateRoundedPath(RectangleF bounds, float radius)
    {
        var path = new System.Drawing.Drawing2D.GraphicsPath();
        var diameter = Math.Min(radius * 2F, Math.Min(bounds.Width, bounds.Height));
        var arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }

    private static Color ResolveOpaqueBackColor(Control control, Color fallback)
    {
        for (var parent = control.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent.BackColor.A == byte.MaxValue)
                return parent.BackColor;
        }
        return fallback;
    }

    private sealed class CardPanel : Panel
    {
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Color BorderTone { get; set; } = BorderColor;

        public CardPanel()
        {
            DoubleBuffered = true;
            ResizeRedraw = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(ResolveOpaqueBackColor(this, BackColor));
            using var brush = new SolidBrush(BackColor);
            using var path = CreateRoundedPath(new RectangleF(0, 0, Width, Height), 10F);
            e.Graphics.FillPath(brush, path);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(BorderTone);
            using var path = CreateRoundedPath(new RectangleF(.5F, .5F, Width - 1F, Height - 1F), 9.5F);
            e.Graphics.DrawPath(pen, path);
        }
    }

    private sealed class FlatActionButton : Button
    {
        private readonly bool _primary;
        private bool _darkMode;
        private bool _hovered;
        private bool _pressed;

        public FlatActionButton(bool primary)
        {
            _primary = primary;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool DarkMode
        {
            get => _darkMode;
            set { _darkMode = value; Invalidate(); }
        }

        protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs mevent) { _pressed = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { _pressed = false; Invalidate(); base.OnMouseUp(mevent); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            e.Graphics.Clear(ResolveOpaqueBackColor(this, _darkMode ? DarkCardColor : CardColor));
            var bounds = new RectangleF(.5F, .5F, Width - 1F, Height - 1F);
            using var path = CreateRoundedPath(bounds, 4F);

            Color background;
            Color foreground;
            Color border;
            if (!Enabled)
            {
                background = _darkMode ? DarkCardColor : CardColor;
                foreground = _darkMode ? Color.FromArgb(148, 163, 184) : Color.FromArgb(100, 116, 139);
                border = _darkMode ? DarkBorderColor : BorderColor;
            }
            else if (_primary)
            {
                background = _pressed ? Color.FromArgb(30, 64, 175) : _hovered ? Color.FromArgb(29, 78, 216) : PrimaryColor;
                foreground = Color.White;
                border = background;
            }
            else
            {
                background = _pressed
                    ? (_darkMode ? Color.FromArgb(30, 41, 59) : Color.FromArgb(241, 245, 249))
                    : _hovered
                        ? (_darkMode ? Color.FromArgb(25, 37, 58) : BackgroundColor)
                        : (_darkMode ? DarkCardColor : CardColor);
                foreground = _darkMode ? DarkTextColor : TextColor;
                border = _darkMode ? DarkBorderColor : BorderColor;
            }

            using var brush = new SolidBrush(background);
            using var pen = new Pen(border);
            e.Graphics.FillPath(brush, path);
            e.Graphics.DrawPath(pen, path);
            TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Round(bounds), foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            if (Focused && ShowFocusCues)
            {
                using var focusPen = new Pen(_darkMode ? DarkPrimaryColor : PrimaryColor, 2F) { DashStyle = System.Drawing.Drawing2D.DashStyle.Dot };
                using var focusPath = CreateRoundedPath(new RectangleF(3.5F, 3.5F, Width - 7F, Height - 7F), 2F);
                e.Graphics.DrawPath(focusPen, focusPath);
            }
        }
    }

    private sealed class BrandMark : Control
    {
        public BrandMark()
        {
            DoubleBuffered = true;
            AccessibleName = "local-transfer logo";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var background = new SolidBrush(PrimaryColor);
            using var whitePen = new Pen(Color.White, 3F)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            };
            using var tile = CreateRoundedPath(new RectangleF(0, 0, Width, Height), Math.Max(8F, Width * .22F));
            e.Graphics.FillPath(background, tile);
            var scale = Width / 44F;
            whitePen.Width = Math.Max(2F, 2.2F * scale);
            e.Graphics.DrawRectangle(whitePen, 7 * scale, 10 * scale, 12 * scale, 20 * scale);
            e.Graphics.DrawRectangle(whitePen, 27 * scale, 14 * scale, 11 * scale, 16 * scale);
            e.Graphics.DrawLine(whitePen, 20 * scale, 17 * scale, 26 * scale, 17 * scale);
            e.Graphics.DrawLine(whitePen, 23 * scale, 14 * scale, 26 * scale, 17 * scale);
            e.Graphics.DrawLine(whitePen, 23 * scale, 20 * scale, 26 * scale, 17 * scale);
            e.Graphics.DrawLine(whitePen, 26 * scale, 25 * scale, 20 * scale, 25 * scale);
            e.Graphics.DrawLine(whitePen, 23 * scale, 22 * scale, 20 * scale, 25 * scale);
            e.Graphics.DrawLine(whitePen, 23 * scale, 28 * scale, 20 * scale, 25 * scale);
        }
    }

    private sealed class ThemeToggleButton : Button
    {
        private bool _darkMode;

        public ThemeToggleButton()
        {
            Text = string.Empty;
            TabStop = true;
            UseVisualStyleBackColor = false;
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool DarkMode
        {
            get => _darkMode;
            set
            {
                _darkMode = value;
                BackColor = value ? DarkCardColor : CardColor;
                ForeColor = value ? DarkPrimaryColor : PrimaryColor;
                FlatAppearance.BorderColor = value ? DarkBorderColor : BorderColor;
                FlatAppearance.MouseOverBackColor = value ? Color.FromArgb(25, 37, 58) : BackgroundColor;
                FlatAppearance.MouseDownBackColor = value ? Color.FromArgb(30, 41, 59) : Color.FromArgb(241, 245, 249);
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var center = new PointF(Width / 2F, Height / 2F);
            using var pen = new Pen(ForeColor, 2F) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            e.Graphics.DrawEllipse(pen, center.X - 5, center.Y - 5, 10, 10);
            for (var i = 0; i < 8; i++)
            {
                var angle = i * Math.PI / 4;
                var start = new PointF(center.X + (float)Math.Cos(angle) * 9, center.Y + (float)Math.Sin(angle) * 9);
                var end = new PointF(center.X + (float)Math.Cos(angle) * 12, center.Y + (float)Math.Sin(angle) * 12);
                e.Graphics.DrawLine(pen, start, end);
            }
        }
    }

    private sealed class FlatComboBox : ComboBox
    {
        private const int WindowPaintMessage = 0x000F;
        private bool _darkMode;

        public FlatComboBox()
        {
            FlatStyle = FlatStyle.Flat;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool DarkMode
        {
            get => _darkMode;
            set
            {
                _darkMode = value;
                BackColor = value ? DarkCardColor : CardColor;
                ForeColor = value ? DarkTextColor : TextColor;
                Invalidate();
            }
        }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == WindowPaintMessage && IsHandleCreated && Width > 0 && Height > 0)
                DrawFlatChrome();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        private void DrawFlatChrome()
        {
            using var graphics = CreateGraphics();
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            var background = _darkMode ? DarkCardColor : CardColor;
            var border = Focused
                ? (_darkMode ? DarkPrimaryColor : PrimaryColor)
                : (_darkMode ? DarkBorderColor : BorderColor);
            var arrow = _darkMode ? DarkMutedColor : MutedColor;
            var buttonWidth = Math.Max(24, SystemInformation.VerticalScrollBarWidth + 5);
            var buttonBounds = new Rectangle(Math.Max(1, Width - buttonWidth - 1), 1, buttonWidth, Math.Max(1, Height - 2));

            using var backgroundBrush = new SolidBrush(background);
            using var borderPen = new Pen(border);
            using var arrowPen = new Pen(arrow, 1.7F)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            };

            graphics.FillRectangle(backgroundBrush, buttonBounds);
            graphics.DrawLine(borderPen, buttonBounds.Left, buttonBounds.Top, buttonBounds.Left, buttonBounds.Bottom);
            graphics.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);

            var centerX = buttonBounds.Left + buttonBounds.Width / 2F;
            var centerY = buttonBounds.Top + buttonBounds.Height / 2F;
            graphics.DrawLine(arrowPen, centerX - 4F, centerY - 2F, centerX, centerY + 2F);
            graphics.DrawLine(arrowPen, centerX, centerY + 2F, centerX + 4F, centerY - 2F);
        }
    }

    private enum StatusKind
    {
        Neutral,
        Success,
        Warning,
        Error
    }
}
