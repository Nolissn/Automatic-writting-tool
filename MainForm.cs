#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenRoadTyper;

public sealed class MainForm : Form
{
    private const int HeaderExtraHeight = 50;
    private const int StartDelayExtraHeight = 150;

    private readonly TableLayoutPanel _headerLayout;
    private readonly TableLayoutPanel _bodyLayout;
    private readonly Control _headerTitlePanel;
    private readonly Control _headerBadgePanel;
    private readonly Control _textPanel;
    private readonly Control _sidebarPanel;
    private readonly RichTextBox _textInput;
    private readonly Label _headerStatusValueLabel;
    private readonly Label _characterCountLabel;
    private readonly Label _delayValueLabel;
    private readonly Button _typingSpeedButton;
    private readonly AutoWrapLabel _statusHeadlineLabel;
    private readonly AutoWrapLabel _statusDetailLabel;
    private readonly Label _countdownLabel;
    private readonly CheckBox _minimizeCheckBox;
    private readonly CheckBox _useEnterKeyCheckBox;
    private readonly Button _pasteClipboardButton;
    private readonly Button _clearTextButton;
    private readonly Button _startButton;
    private readonly Button _cancelButton;
    private readonly List<Control> _editableControls = new();

    private CancellationTokenSource? _runCts;
    private int _delaySeconds = 3;
    private decimal _typingDelayMs = 8.0m;
    private bool _headerIsStacked;
    private bool _bodyIsStacked;

    public MainForm()
    {
        SuspendLayout();

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);
        Text = "The Open Road Terminal";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(960, 780);
        ClientSize = new Size(1280, 900);
        BackColor = Palette.Background;
        ForeColor = Palette.TextPrimary;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        _textInput = BuildTextEditor();
        _characterCountLabel = CreateCounterLabel();
        _delayValueLabel = CreateDisplayLabel(32F, Palette.Accent, ContentAlignment.MiddleCenter);
        _statusHeadlineLabel = CreateWrapLabel(
            19F,
            FontStyle.Bold,
            "Bahnschrift SemiCondensed",
            Palette.Accent,
            ContentAlignment.MiddleLeft);
        _statusDetailLabel = CreateWrapLabel(
            10F,
            FontStyle.Regular,
            "Segoe UI",
            Palette.TextPrimary,
            ContentAlignment.MiddleLeft);
        _countdownLabel = CreateDisplayLabel(30F, Palette.TextPrimary, ContentAlignment.MiddleLeft);
        _minimizeCheckBox = CreateCheckBox("Fenster beim Start minimieren");
        _useEnterKeyCheckBox = CreateCheckBox("Enter-Taste verwenden");
        _pasteClipboardButton = CreateSecondaryButton("Aus Zwischenablage einf\u00fcgen");
        _clearTextButton = CreateSecondaryButton("Leeren");
        _typingSpeedButton = CreateSecondaryButton(string.Empty);
        _startButton = CreatePrimaryButton("START");
        _cancelButton = CreateSecondaryButton("ABBRECHEN");

        _headerLayout = CreateTransparentTable();
        _headerTitlePanel = BuildHeaderTitlePanel();
        _headerBadgePanel = BuildHeaderBadgePanel(out _headerStatusValueLabel);
        _textPanel = BuildTextPanel();
        _sidebarPanel = BuildSidebarPanel();
        _bodyLayout = CreateTransparentTable();

        var scrollHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.Transparent,
        };

        var root = CreateTransparentTable();
        root.Dock = DockStyle.Top;
        root.AutoSize = true;
        root.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        root.Padding = new Padding(32);
        root.ColumnCount = 1;
        root.RowCount = 2;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(BuildHeaderPanel(), 0, 0);
        root.Controls.Add(BuildBodyPanel(), 0, 1);
        scrollHost.Controls.Add(root);
        Controls.Add(scrollHost);

        WireEvents();
        RefreshDelayDisplay();
        RefreshTypingSpeedDisplay();
        RefreshCharacterCount();
        SetStatus(
            "STANDBY",
            "Text eingeben, Verz\u00f6gerung festlegen, Start dr\u00fccken und in das Zielfeld wechseln.");
        UpdateUiState(isRunning: false);
        UpdateResponsiveLayout();

        ResumeLayout(performLayout: true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateResponsiveLayout();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateResponsiveLayout();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var rect = ClientRectangle;
        using var gradient = new LinearGradientBrush(
            rect,
            Color.FromArgb(17, 24, 27),
            Color.FromArgb(8, 12, 14),
            LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(gradient, rect);

        using var horizontalPen = new Pen(Color.FromArgb(18, Palette.Accent), 1F);
        for (var y = 0; y < Height; y += 34)
        {
            e.Graphics.DrawLine(horizontalPen, 0, y, Width, y);
        }

        using var diagonalPen = new Pen(Color.FromArgb(10, Palette.AccentGlow), 1F);
        for (var x = -Height; x < Width; x += 78)
        {
            e.Graphics.DrawLine(diagonalPen, x, 0, x + Height, Height);
        }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        _runCts?.Cancel();
        base.OnFormClosing(e);
    }

    private Control BuildHeaderPanel()
    {
        var header = new TerminalPanel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(32, 48, 32, 48 + ScaleLogical(HeaderExtraHeight)),
            Margin = new Padding(0, 0, 0, 24),
            MinimumSize = new Size(0, ScaleLogical(244 + HeaderExtraHeight)),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        _headerLayout.Dock = DockStyle.Fill;
        header.Controls.Add(_headerLayout);
        ApplyHeaderLayout(stacked: false);

        return header;
    }

    private TableLayoutPanel BuildBodyPanel()
    {
        _bodyLayout.Dock = DockStyle.Top;
        _bodyLayout.AutoSize = true;
        _bodyLayout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        ApplyBodyLayout(stacked: false);
        return _bodyLayout;
    }

    private Control BuildHeaderTitlePanel()
    {
        var titleLayout = CreateTransparentTable();
        titleLayout.Dock = DockStyle.Fill;
        titleLayout.ColumnCount = 1;
        titleLayout.RowCount = 3;
        titleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var eyebrow = CreateStandardLabel(
            "CRIMINAL ENTERPRISE TERMINAL",
            "Bahnschrift SemiCondensed",
            11F,
            FontStyle.Bold,
            Palette.TextMuted);
        eyebrow.Margin = new Padding(0, 0, 0, 8);

        var title = CreateStandardLabel(
            "THE OPEN ROAD",
            "Bahnschrift Condensed",
            32F,
            FontStyle.Bold,
            Palette.Accent);
        title.Padding = new Padding(0, 0, 0, 4);
        title.Margin = new Padding(0, 0, 0, 4);

        var subtitle = CreateWrapLabel(
            12F,
            FontStyle.Bold,
            "Bahnschrift SemiCondensed",
            Palette.TextPrimary,
            ContentAlignment.MiddleLeft);
        subtitle.Text = "AUTO-TYPE TERMINAL / ACTIVE WINDOW DELIVERY";
        subtitle.Margin = new Padding(0, 2, 0, 0);
        subtitle.BindToWidth(titleLayout);

        titleLayout.Controls.Add(eyebrow, 0, 0);
        titleLayout.Controls.Add(title, 0, 1);
        titleLayout.Controls.Add(subtitle, 0, 2);

        return titleLayout;
    }

    private Control BuildHeaderBadgePanel(out Label statusValueLabel)
    {
        var badgeLayout = CreateTransparentTable();
        badgeLayout.Dock = DockStyle.Fill;
        badgeLayout.ColumnCount = 1;
        badgeLayout.RowCount = 3;
        badgeLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        badgeLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        badgeLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        badgeLayout.Controls.Add(CreateMetricBadge("TARGET", "Aktives Fenster"), 0, 0);
        badgeLayout.Controls.Add(CreateMetricBadge("INPUT MODE", "Hardware Key Simulation"), 0, 1);
        badgeLayout.Controls.Add(CreateMetricBadge("STATUS", "Bereit", out statusValueLabel), 0, 2);

        return badgeLayout;
    }

    private Control BuildTextPanel()
    {
        var textPanel = new TerminalPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28),
            MinimumSize = new Size(560, 380),
        };

        var layout = CreateTransparentTable();
        layout.Dock = DockStyle.Fill;
        layout.ColumnCount = 1;
        layout.RowCount = 5;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        textPanel.Controls.Add(layout);

        var title = CreateSectionTitle("Textinhalt");
        title.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(title, 0, 0);

        var subtitle = CreateBodyLabel(
            "Hier kommt der Text hinein, den das Tool sp\u00e4ter in das aktuell fokussierte Eingabefeld tippt.");
        subtitle.Margin = new Padding(0, 0, 0, 18);
        subtitle.BindToWidth(layout);
        layout.Controls.Add(subtitle, 0, 1);

        var buttonStrip = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 18),
        };
        buttonStrip.Controls.Add(_pasteClipboardButton);
        buttonStrip.Controls.Add(_clearTextButton);
        layout.Controls.Add(buttonStrip, 0, 2);

        _editableControls.Add(_pasteClipboardButton);
        _editableControls.Add(_clearTextButton);

        var editorChrome = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            BackColor = Palette.Border,
            Margin = new Padding(0, 0, 0, 18),
            MinimumSize = new Size(0, 320),
        };

        var editorInset = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(18, 16, 18, 16),
            BackColor = Palette.Input,
            Margin = new Padding(0),
        };

        _textInput.Dock = DockStyle.Fill;
        editorInset.Controls.Add(_textInput);
        editorChrome.Controls.Add(editorInset);
        layout.Controls.Add(editorChrome, 0, 3);

        var footer = CreateTransparentTable();
        footer.Dock = DockStyle.Fill;
        footer.ColumnCount = 2;
        footer.RowCount = 1;
        footer.AutoSize = true;
        footer.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var hintHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 16, 0),
            BackColor = Color.Transparent,
        };

        var hint = CreateMetaLabel(
            "Hinweis: Nach dem Start in das gew\u00fcnschte Zielfeld wechseln. Dort landet der Text.",
            ContentAlignment.MiddleLeft);
        hint.BindToWidth(hintHost);
        hintHost.Controls.Add(hint);
        footer.Controls.Add(hintHost, 0, 0);

        _characterCountLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        footer.Controls.Add(_characterCountLabel, 1, 0);
        layout.Controls.Add(footer, 0, 4);

        return textPanel;
    }

    private Control BuildSidebarPanel()
    {
        var sidebar = CreateTransparentTable();
        sidebar.Dock = DockStyle.Top;
        sidebar.AutoSize = true;
        sidebar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        sidebar.ColumnCount = 1;
        sidebar.RowCount = 3;
        sidebar.MinimumSize = new Size(380, 0);
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var controlPanel = BuildControlPanel();
        controlPanel.Margin = new Padding(0, 0, 0, 20);
        sidebar.Controls.Add(controlPanel, 0, 0);

        var statusPanel = BuildStatusPanel();
        statusPanel.Margin = new Padding(0, 0, 0, 20);
        sidebar.Controls.Add(statusPanel, 0, 1);

        sidebar.Controls.Add(BuildInstructionPanel(), 0, 2);

        return sidebar;
    }

    private TerminalPanel BuildControlPanel()
    {
        var panel = new TerminalPanel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(26, 26, 26, 26 + ScaleLogical(StartDelayExtraHeight)),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(380, 0),
        };

        var layout = CreateTransparentTable();
        layout.Dock = DockStyle.Top;
        layout.AutoSize = true;
        layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        layout.ColumnCount = 1;
        layout.RowCount = 7;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var i = 0; i < 7; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var title = CreateSectionTitle("Startverz\u00f6gerung");
        title.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(title, 0, 0);

        var delayInfo = CreateMetaLabel(
            "Zeit zwischen START und dem Beginn der Tastatureingaben.",
            ContentAlignment.MiddleLeft);
        delayInfo.Margin = new Padding(0, 0, 0, 18);
        delayInfo.BindToWidth(layout);
        layout.Controls.Add(delayInfo, 0, 1);

        var delayPanel = CreateTransparentTable();
        delayPanel.Dock = DockStyle.Fill;
        delayPanel.AutoSize = true;
        delayPanel.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        delayPanel.ColumnCount = 3;
        delayPanel.RowCount = 1;
        delayPanel.Margin = new Padding(0, 0, 0, 18);
        delayPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        delayPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        delayPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var minusButton = CreateIconButton("-");
        minusButton.Click += (_, _) => AdjustDelay(-1);
        delayPanel.Controls.Add(minusButton, 0, 0);
        _editableControls.Add(minusButton);

        var valueLayout = CreateTransparentTable();
        valueLayout.Dock = DockStyle.Fill;
        valueLayout.ColumnCount = 1;
        valueLayout.RowCount = 2;
        valueLayout.Margin = new Padding(14, 0, 14, 0);
        valueLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        valueLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        delayPanel.Controls.Add(valueLayout, 1, 0);

        _delayValueLabel.Anchor = AnchorStyles.None;
        _delayValueLabel.Margin = new Padding(0, 0, 0, 6);
        valueLayout.Controls.Add(_delayValueLabel, 0, 0);

        var delayHint = CreateMetaLabel(
            "Sekunden zwischen START und dem Tippen",
            ContentAlignment.MiddleCenter);
        delayHint.Margin = new Padding(0);
        delayHint.BindToWidth(valueLayout);
        valueLayout.Controls.Add(delayHint, 0, 1);

        var plusButton = CreateIconButton("+");
        plusButton.Click += (_, _) => AdjustDelay(1);
        delayPanel.Controls.Add(plusButton, 2, 0);
        _editableControls.Add(plusButton);

        layout.Controls.Add(delayPanel, 0, 2);

        var presetWrap = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.None,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 12),
        };
        foreach (var preset in new[] { 3, 5, 10, 15 })
        {
            var presetButton = CreatePresetButton($"{preset}s");
            presetButton.Click += (_, _) => SetDelay(preset);
            presetWrap.Controls.Add(presetButton);
            _editableControls.Add(presetButton);
        }
        layout.Controls.Add(presetWrap, 0, 3);

        _typingSpeedButton.Anchor = AnchorStyles.None;
        _typingSpeedButton.Margin = new Padding(0, 0, 0, 16);
        layout.Controls.Add(_typingSpeedButton, 0, 4);
        _editableControls.Add(_typingSpeedButton);

        var optionWrap = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 16),
        };

        _minimizeCheckBox.Checked = false;
        _minimizeCheckBox.Margin = new Padding(0, 0, 18, 0);
        optionWrap.Controls.Add(_minimizeCheckBox);
        _editableControls.Add(_minimizeCheckBox);

        _useEnterKeyCheckBox.Checked = true;
        _useEnterKeyCheckBox.Margin = new Padding(0);
        optionWrap.Controls.Add(_useEnterKeyCheckBox);
        _editableControls.Add(_useEnterKeyCheckBox);

        layout.Controls.Add(optionWrap, 0, 5);

        var actionWrap = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Anchor = AnchorStyles.None,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };

        _startButton.Click += StartButton_Click;
        _cancelButton.Click += (_, _) => _runCts?.Cancel();
        actionWrap.Controls.Add(_startButton);
        actionWrap.Controls.Add(_cancelButton);
        layout.Controls.Add(actionWrap, 0, 6);

        panel.Controls.Add(layout);
        return panel;
    }

    private TerminalPanel BuildStatusPanel()
    {
        var panel = new TerminalPanel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(30),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(380, ScaleLogical(220)),
        };

        var layout = CreateTransparentTable();
        layout.Dock = DockStyle.Top;
        layout.AutoSize = true;
        layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        layout.ColumnCount = 1;
        layout.RowCount = 5;
        for (var i = 0; i < 5; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var title = CreateSectionTitle("Status");
        title.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(title, 0, 0);

        _statusHeadlineLabel.Margin = new Padding(0, 0, 0, 10);
        _statusHeadlineLabel.BindToWidth(layout);
        layout.Controls.Add(_statusHeadlineLabel, 0, 1);

        _statusDetailLabel.Margin = new Padding(0, 0, 0, 22);
        _statusDetailLabel.BindToWidth(layout);
        layout.Controls.Add(_statusDetailLabel, 0, 2);

        var caption = CreateStandardLabel(
            "Countdown",
            "Bahnschrift SemiCondensed",
            10.5F,
            FontStyle.Bold,
            Palette.TextMuted);
        caption.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(caption, 0, 3);

        _countdownLabel.Margin = new Padding(0);
        layout.Controls.Add(_countdownLabel, 0, 4);

        panel.Controls.Add(layout);
        return panel;
    }

    private Control BuildInstructionPanel()
    {
        var panel = new TerminalPanel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(26),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(380, 220),
        };

        var layout = CreateTransparentTable();
        layout.Dock = DockStyle.Top;
        layout.AutoSize = true;
        layout.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        layout.ColumnCount = 1;
        layout.RowCount = 2;
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        panel.Controls.Add(layout);

        var title = CreateSectionTitle("Bedienung");
        title.Margin = new Padding(0, 0, 0, 12);
        layout.Controls.Add(title, 0, 0);

        var instructionText = CreateBodyLabel(
            "1. Text links eintragen." + Environment.NewLine + Environment.NewLine +
            "2. Verz\u00f6gerung und Tippgeschwindigkeit einstellen." + Environment.NewLine + Environment.NewLine +
            "3. START dr\u00fccken und w\u00e4hrend des Countdowns das Zielfeld fokussieren." + Environment.NewLine + Environment.NewLine +
            "4. Das Tool sendet den Text \u00fcber simulierte Tastatureingaben in das aktive Fenster." + Environment.NewLine + Environment.NewLine +
            "Hinweis: Wenn das Zielprogramm Administratorrechte hat, muss dieses Tool gegebenenfalls ebenfalls erh\u00f6ht gestartet werden.",
            ContentAlignment.TopLeft);
        instructionText.ForeColor = Palette.TextPrimary;
        instructionText.BindToWidth(layout);
        layout.Controls.Add(instructionText, 0, 1);

        return panel;
    }

    private RichTextBox BuildTextEditor()
    {
        var textBox = new TerminalRichTextBox
        {
            Margin = new Padding(0),
        };

        _editableControls.Add(textBox);
        return textBox;
    }

    private void WireEvents()
    {
        _textInput.TextChanged += (_, _) => RefreshCharacterCount();
        _pasteClipboardButton.Click += PasteClipboardButton_Click;
        _clearTextButton.Click += ClearTextButton_Click;
        _typingSpeedButton.Click += TypingSpeedButton_Click;
    }

    private void PasteClipboardButton_Click(object? sender, EventArgs e)
    {
        try
        {
            if (!Clipboard.ContainsText())
            {
                SetStatus("CLIPBOARD EMPTY", "Die Zwischenablage enth\u00e4lt aktuell keinen Text.");
                return;
            }

            var clipboardText = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(clipboardText))
            {
                SetStatus("CLIPBOARD EMPTY", "Die Zwischenablage enth\u00e4lt aktuell keinen nutzbaren Text.");
                return;
            }

            _textInput.Text = clipboardText;
            _textInput.SelectionStart = _textInput.TextLength;
            _textInput.ScrollToCaret();
            _textInput.Focus();

            SetStatus(
                "CLIPBOARD READY",
                $"{clipboardText.Length} Zeichen wurden in den Textinhalt \u00fcbernommen.");
        }
        catch (ExternalException)
        {
            SetStatus(
                "CLIPBOARD BUSY",
                "Auf die Zwischenablage konnte gerade nicht zugegriffen werden. Bitte kurz erneut versuchen.");
        }
    }

    private void ClearTextButton_Click(object? sender, EventArgs e)
    {
        if (_textInput.TextLength == 0)
        {
            SetStatus("TEXT EMPTY", "Das Textfeld ist bereits leer.");
            return;
        }

        _textInput.Clear();
        _textInput.Focus();
        SetStatus("TEXT CLEARED", "Der Textinhalt wurde entfernt.");
    }

    private async void StartButton_Click(object? sender, EventArgs e)
    {
        if (_runCts is not null)
        {
            return;
        }

        var payload = _textInput.Text;
        if (string.IsNullOrWhiteSpace(payload))
        {
            SetStatus("NO PAYLOAD", "Das Textfeld ist leer. Erst Text eintragen, dann starten.");
            _textInput.Focus();
            return;
        }

        _runCts = new CancellationTokenSource();
        UpdateUiState(isRunning: true);
        var token = _runCts.Token;

        try
        {
            if (_minimizeCheckBox.Checked)
            {
                WindowState = FormWindowState.Minimized;
                await Task.Delay(180, token);
            }

            for (var remaining = _delaySeconds; remaining > 0; remaining--)
            {
                SetStatus(
                    "LOCK TARGET",
                    $"Jetzt in das Zielfeld wechseln. Das Tippen startet in {remaining} Sek.");
                _countdownLabel.Text = $"{remaining:00}s";
                await Task.Delay(1000, token);
            }

            SetStatus(
                "TRANSMITTING",
                $"Sende {payload.Length} Zeichen mit {FormatMilliseconds(_typingDelayMs)} ms pro Taste \u00fcber die Windows-Tastatur-API.");
            _countdownLabel.Text = "LIVE";

            var useEnterKey = _useEnterKeyCheckBox.Checked;
            await Task.Run(() => KeyboardTransmitter.SendText(payload, (double)_typingDelayMs, useEnterKey, token), token);

            SetStatus("JOB COMPLETE", "Text wurde erfolgreich in das aktive Fenster gesendet.");
            _countdownLabel.Text = "DONE";
        }
        catch (OperationCanceledException)
        {
            SetStatus("ABORTED", "Vorgang wurde gestoppt.");
            _countdownLabel.Text = "--";
        }
        catch (Exception ex)
        {
            SetStatus("FAILSAFE", ex.Message);
            _countdownLabel.Text = "ERR";
        }
        finally
        {
            _runCts.Dispose();
            _runCts = null;
            UpdateUiState(isRunning: false);
            RefreshDelayDisplay();
        }
    }

    private void SetDelay(int seconds)
    {
        _delaySeconds = Clamp(seconds, 1, 60);
        RefreshDelayDisplay();
    }

    private void AdjustDelay(int delta)
    {
        SetDelay(_delaySeconds + delta);
    }

    private void RefreshDelayDisplay()
    {
        _delayValueLabel.Text = $"{_delaySeconds:00}s";
        if (_runCts is null)
        {
            _countdownLabel.Text = $"{_delaySeconds:00}s";
        }
    }

    private void RefreshTypingSpeedDisplay()
    {
        _typingSpeedButton.Text = $"Tippgeschwindigkeit: {FormatMilliseconds(_typingDelayMs)} ms";
    }

    private void TypingSpeedButton_Click(object? sender, EventArgs e)
    {
        if (!TryPromptTypingSpeed(out var typingDelayMs))
        {
            return;
        }

        _typingDelayMs = typingDelayMs;
        RefreshTypingSpeedDisplay();
        SetStatus("SPEED SET", $"Tippgeschwindigkeit auf {FormatMilliseconds(_typingDelayMs)} ms pro Taste gesetzt.");
    }

    private bool TryPromptTypingSpeed(out decimal typingDelayMs)
    {
        using var dialog = new Form
        {
            Text = "Tippgeschwindigkeit",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            ClientSize = new Size(420, 190),
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            BackColor = Palette.Panel,
            ForeColor = Palette.TextPrimary,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
        };

        var layout = CreateTransparentTable();
        layout.Dock = DockStyle.Fill;
        layout.Padding = new Padding(18);
        layout.ColumnCount = 1;
        layout.RowCount = 4;
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (var i = 0; i < 4; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var title = CreateStandardLabel(
            "Tippgeschwindigkeit",
            "Bahnschrift SemiCondensed",
            17F,
            FontStyle.Bold,
            Palette.TextPrimary);
        title.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(title, 0, 0);

        var description = CreateMetaLabel(
            "Verz\u00f6gerung zwischen zwei Tasten in Millisekunden. 0 ist erlaubt.",
            ContentAlignment.MiddleLeft);
        description.Margin = new Padding(0, 0, 0, 12);
        description.BindToWidth(layout);
        layout.Controls.Add(description, 0, 1);

        var inputWrap = CreateTransparentTable();
        inputWrap.AutoSize = true;
        inputWrap.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        inputWrap.ColumnCount = 2;
        inputWrap.RowCount = 1;
        inputWrap.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inputWrap.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        inputWrap.Margin = new Padding(0, 0, 0, 18);

        var numericInput = new NumericUpDown
        {
            DecimalPlaces = 2,
            Increment = 0.10m,
            Minimum = 0m,
            Maximum = 1000m,
            Value = _typingDelayMs,
            Width = 140,
            TextAlign = HorizontalAlignment.Right,
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = Palette.Input,
            ForeColor = Palette.TextPrimary,
            Font = new Font("Consolas", 11F, FontStyle.Regular, GraphicsUnit.Point),
            Margin = new Padding(0, 0, 10, 0),
            ThousandsSeparator = false,
        };
        inputWrap.Controls.Add(numericInput, 0, 0);

        var unitLabel = CreateStandardLabel(
            "ms",
            "Bahnschrift SemiCondensed",
            12F,
            FontStyle.Bold,
            Palette.Accent);
        unitLabel.Anchor = AnchorStyles.Left;
        inputWrap.Controls.Add(unitLabel, 1, 0);
        layout.Controls.Add(inputWrap, 0, 2);

        var buttonWrap = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };

        var applyButton = CreatePrimaryButton("\u00dcbernehmen");
        applyButton.MinimumSize = new Size(148, 50);
        applyButton.Margin = new Padding(0, 0, 10, 0);
        applyButton.DialogResult = DialogResult.OK;
        buttonWrap.Controls.Add(applyButton);

        var cancelButton = CreateSecondaryButton("Abbrechen");
        cancelButton.MinimumSize = new Size(138, 50);
        cancelButton.Margin = new Padding(0);
        cancelButton.DialogResult = DialogResult.Cancel;
        buttonWrap.Controls.Add(cancelButton);

        layout.Controls.Add(buttonWrap, 0, 3);
        dialog.AcceptButton = applyButton;
        dialog.CancelButton = cancelButton;
        dialog.Controls.Add(layout);

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            typingDelayMs = _typingDelayMs;
            return false;
        }

        typingDelayMs = numericInput.Value;
        return true;
    }

    private void RefreshCharacterCount()
    {
        _characterCountLabel.Text = $"{_textInput.TextLength} Zeichen";
    }

    private void UpdateUiState(bool isRunning)
    {
        foreach (var control in _editableControls)
        {
            control.Enabled = !isRunning;
        }

        _startButton.Enabled = !isRunning;
        _cancelButton.Enabled = isRunning;

        if (!isRunning && string.IsNullOrWhiteSpace(_statusHeadlineLabel.Text))
        {
            SetStatus("STANDBY", "Bereit f\u00fcr den n\u00e4chsten Versand.");
        }
    }

    private void SetStatus(string headline, string detail)
    {
        _statusHeadlineLabel.Text = headline;
        _statusDetailLabel.Text = detail;
        _headerStatusValueLabel.Text = headline == "STANDBY" ? "Bereit" : headline;
        _statusHeadlineLabel.Parent?.PerformLayout();
        _statusDetailLabel.Parent?.PerformLayout();
        _headerStatusValueLabel.Parent?.PerformLayout();
        _headerStatusValueLabel.Parent?.Parent?.PerformLayout();
    }

    private void UpdateResponsiveLayout()
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        ApplyHeaderLayout(ClientSize.Width < ScaleLogical(1180));
        ApplyBodyLayout(ClientSize.Width < ScaleLogical(1220));
    }

    private void ApplyHeaderLayout(bool stacked)
    {
        if (_headerLayout.Controls.Count > 0 && _headerIsStacked == stacked)
        {
            return;
        }

        _headerIsStacked = stacked;
        _headerLayout.SuspendLayout();
        _headerLayout.Controls.Clear();
        _headerLayout.ColumnStyles.Clear();
        _headerLayout.RowStyles.Clear();

        if (stacked)
        {
            _headerLayout.ColumnCount = 1;
            _headerLayout.RowCount = 2;
            _headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _headerTitlePanel.Margin = new Padding(0, 0, 0, 20);
            _headerBadgePanel.Margin = new Padding(0);

            _headerLayout.Controls.Add(_headerTitlePanel, 0, 0);
            _headerLayout.Controls.Add(_headerBadgePanel, 0, 1);
        }
        else
        {
            _headerLayout.ColumnCount = 2;
            _headerLayout.RowCount = 1;
            _headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63F));
            _headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37F));
            _headerLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _headerTitlePanel.Margin = new Padding(0, 0, 28, 0);
            _headerBadgePanel.Margin = new Padding(0, 4, 0, 0);

            _headerLayout.Controls.Add(_headerTitlePanel, 0, 0);
            _headerLayout.Controls.Add(_headerBadgePanel, 1, 0);
        }

        _headerLayout.ResumeLayout(performLayout: true);
    }

    private void ApplyBodyLayout(bool stacked)
    {
        if (_bodyLayout.Controls.Count > 0 && _bodyIsStacked == stacked)
        {
            return;
        }

        _bodyIsStacked = stacked;
        _bodyLayout.SuspendLayout();
        _bodyLayout.Controls.Clear();
        _bodyLayout.ColumnStyles.Clear();
        _bodyLayout.RowStyles.Clear();

        if (stacked)
        {
            _bodyLayout.ColumnCount = 1;
            _bodyLayout.RowCount = 2;
            _bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            _bodyLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _bodyLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _textPanel.Margin = new Padding(0, 0, 0, 10);
            _sidebarPanel.Margin = new Padding(0);

            _bodyLayout.Controls.Add(_textPanel, 0, 0);
            _bodyLayout.Controls.Add(_sidebarPanel, 0, 1);
        }
        else
        {
            _bodyLayout.ColumnCount = 2;
            _bodyLayout.RowCount = 1;
            _bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
            _bodyLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
            _bodyLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _textPanel.Margin = new Padding(0, 0, 24, 10);
            _sidebarPanel.Margin = new Padding(0);

            _bodyLayout.Controls.Add(_textPanel, 0, 0);
            _bodyLayout.Controls.Add(_sidebarPanel, 1, 0);
        }

        _bodyLayout.ResumeLayout(performLayout: true);
    }

    private int ScaleLogical(int logicalPixels)
    {
        var dpi = DeviceDpi > 0 ? DeviceDpi : 96;
        return (int)Math.Round(logicalPixels * dpi / 96D);
    }

    private static TableLayoutPanel CreateTransparentTable()
    {
        return new TableLayoutPanel
        {
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
    }

    private static Label CreateSectionTitle(string text)
    {
        var label = CreateStandardLabel(
            text.ToUpperInvariant(),
            "Bahnschrift SemiCondensed",
            18F,
            FontStyle.Bold,
            Palette.TextPrimary);
        label.Padding = new Padding(0, 0, 0, 3);
        return label;
    }

    private static AutoWrapLabel CreateBodyLabel(
        string text,
        ContentAlignment textAlign = ContentAlignment.MiddleLeft)
    {
        var label = CreateWrapLabel(
            10.25F,
            FontStyle.Regular,
            "Segoe UI",
            Palette.TextMuted,
            textAlign);
        label.Text = text;
        return label;
    }

    private static AutoWrapLabel CreateMetaLabel(string text, ContentAlignment textAlign)
    {
        var label = CreateWrapLabel(
            9.5F,
            FontStyle.Regular,
            "Segoe UI",
            Palette.TextMuted,
            textAlign);
        label.Text = text;
        return label;
    }

    private static AutoWrapLabel CreateWrapLabel(
        float size,
        FontStyle style,
        string fontFamily,
        Color foreColor,
        ContentAlignment textAlign)
    {
        return new AutoWrapLabel
        {
            ForeColor = foreColor,
            Font = new Font(fontFamily, size, style, GraphicsUnit.Point),
            TextAlign = textAlign,
            Padding = new Padding(0, 0, 0, 4),
            Margin = new Padding(0),
        };
    }

    private static Label CreateStandardLabel(
        string text,
        string fontFamily,
        float size,
        FontStyle style,
        Color foreColor)
    {
        return new Label
        {
            AutoSize = true,
            Text = text,
            ForeColor = foreColor,
            Font = new Font(fontFamily, size, style, GraphicsUnit.Point),
            UseMnemonic = false,
            UseCompatibleTextRendering = true,
            Margin = new Padding(0),
        };
    }

    private static Label CreateDisplayLabel(float size, Color foreColor, ContentAlignment textAlign)
    {
        return new Label
        {
            AutoSize = true,
            ForeColor = foreColor,
            Font = new Font("Bahnschrift Condensed", size, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = textAlign,
            UseMnemonic = false,
            UseCompatibleTextRendering = true,
            Padding = new Padding(0, 0, 0, 3),
            Margin = new Padding(0),
        };
    }

    private static Label CreateCounterLabel()
    {
        return new Label
        {
            AutoSize = true,
            ForeColor = Palette.Accent,
            Font = new Font("Bahnschrift SemiBold", 10.5F, FontStyle.Bold, GraphicsUnit.Point),
            TextAlign = ContentAlignment.MiddleRight,
            UseMnemonic = false,
            UseCompatibleTextRendering = true,
            Padding = new Padding(0, 0, 0, 2),
            Margin = new Padding(0),
        };
    }

    private static Control CreateMetricBadge(string label, string value)
    {
        return CreateMetricBadge(label, value, out _);
    }

    private static Control CreateMetricBadge(string label, string value, out Label valueLabel)
    {
        var badge = CreateTransparentTable();
        badge.Dock = DockStyle.Top;
        badge.AutoSize = true;
        badge.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        badge.ColumnCount = 2;
        badge.RowCount = 1;
        badge.BackColor = Color.FromArgb(30, 39, 42);
        badge.Margin = new Padding(0, 0, 0, 12);
        badge.Padding = new Padding(0);
        badge.MinimumSize = new Size(0, 64);
        badge.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        badge.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var left = CreateStandardLabel(
            label,
            "Bahnschrift SemiCondensed",
            10.5F,
            FontStyle.Bold,
            Palette.TextMuted);
        left.Padding = new Padding(14, 12, 16, 12);

        var valueWrapLabel = new AutoWrapLabel
        {
            Text = value,
            ForeColor = Palette.Accent,
            Font = new Font("Bahnschrift SemiCondensed", 10.5F, FontStyle.Bold, GraphicsUnit.Point),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(12, 12, 14, 12),
            Margin = new Padding(0),
        };
        valueWrapLabel.BindToWidth(badge, left.GetPreferredSize(Size.Empty).Width + 8);
        valueLabel = valueWrapLabel;

        badge.Controls.Add(left, 0, 0);
        badge.Controls.Add(valueLabel, 1, 0);
        return badge;
    }

    private static CheckBox CreateCheckBox(string text)
    {
        return new CheckBox
        {
            AutoSize = true,
            Text = text,
            ForeColor = Palette.TextPrimary,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            Padding = new Padding(0, 4, 0, 5),
            Margin = new Padding(0),
            CheckAlign = ContentAlignment.MiddleLeft,
            TextAlign = ContentAlignment.MiddleLeft,
            UseMnemonic = false,
            UseVisualStyleBackColor = false,
            UseCompatibleTextRendering = true,
        };
    }

    private static Button CreatePrimaryButton(string text)
    {
        var button = CreateButton(text, Palette.Accent, Palette.Background, Palette.AccentGlow);
        button.MinimumSize = new Size(204, 54);
        return button;
    }

    private static Button CreateSecondaryButton(string text)
    {
        var button = CreateButton(text, Color.FromArgb(24, 31, 34), Palette.TextPrimary, Palette.Border);
        button.MinimumSize = new Size(158, 50);
        return button;
    }

    private static Button CreatePresetButton(string text)
    {
        var button = CreateButton(text, Color.FromArgb(23, 29, 32), Palette.TextPrimary, Palette.Border);
        button.MinimumSize = new Size(68, 42);
        button.Padding = new Padding(16, 8, 16, 8);
        return button;
    }

    private static Button CreateIconButton(string text)
    {
        var button = CreateButton(text, Color.FromArgb(23, 29, 32), Palette.Accent, Palette.Border);
        button.AutoSize = false;
        button.Dock = DockStyle.Fill;
        button.MinimumSize = new Size(66, 66);
        button.Padding = new Padding(0);
        button.Margin = new Padding(0);
        button.Font = new Font("Bahnschrift SemiCondensed", 18F, FontStyle.Bold, GraphicsUnit.Point);
        return button;
    }

    private static Button CreateButton(string text, Color backColor, Color foreColor, Color borderColor)
    {
        var button = new Button
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Text = text,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = foreColor,
            Font = new Font("Bahnschrift SemiCondensed", 11.25F, FontStyle.Bold, GraphicsUnit.Point),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 10, 10),
            Padding = new Padding(18, 11, 18, 11),
            UseMnemonic = false,
            UseCompatibleTextRendering = true,
        };

        button.FlatAppearance.BorderSize = 1;
        button.FlatAppearance.BorderColor = borderColor;
        button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(backColor, 0.08F);
        button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(backColor, 0.08F);

        return button;
    }

    private static int Clamp(int value, int minimum, int maximum)
    {
        return Math.Min(Math.Max(value, minimum), maximum);
    }

    private static string FormatMilliseconds(decimal value)
    {
        return value.ToString("0.0#", CultureInfo.CurrentCulture);
    }
}

internal sealed class TerminalPanel : Panel
{
    public TerminalPanel()
    {
        DoubleBuffered = true;
        BackColor = Palette.Panel;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var rect = ClientRectangle;
        using var brush = new LinearGradientBrush(
            rect,
            Color.FromArgb(31, 39, 42),
            Color.FromArgb(18, 24, 27),
            LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(brush, rect);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);

        var rect = Rectangle.Inflate(ClientRectangle, -1, -1);
        if (rect.Width <= 0 || rect.Height <= 0)
        {
            return;
        }

        using var borderPen = new Pen(Palette.Border, 1.25F);
        e.Graphics.DrawRectangle(borderPen, rect);

        using var accentPen = new Pen(Palette.Accent, 2.4F);
        e.Graphics.DrawLine(accentPen, rect.Left + 18, rect.Top + 16, rect.Left + 154, rect.Top + 16);

        using var cornerPen = new Pen(Color.FromArgb(80, Palette.Accent), 1F);
        e.Graphics.DrawLine(cornerPen, rect.Right - 42, rect.Bottom - 16, rect.Right - 14, rect.Bottom - 16);
        e.Graphics.DrawLine(cornerPen, rect.Right - 14, rect.Bottom - 42, rect.Right - 14, rect.Bottom - 16);
    }
}

internal sealed class AutoWrapLabel : Label
{
    private Control? _widthReference;
    private int _horizontalInset;

    public AutoWrapLabel()
    {
        AutoSize = true;
        AutoEllipsis = false;
        UseMnemonic = false;
        UseCompatibleTextRendering = true;
    }

    public void BindToWidth(Control widthReference, int horizontalInset = 0)
    {
        if (_widthReference is not null)
        {
            _widthReference.SizeChanged -= WidthReference_SizeChanged;
        }

        _widthReference = widthReference;
        _horizontalInset = horizontalInset;

        if (_widthReference is not null)
        {
            _widthReference.SizeChanged += WidthReference_SizeChanged;
            UpdateMaximumWidth();
        }
    }

    protected override void OnParentChanged(EventArgs e)
    {
        base.OnParentChanged(e);
        UpdateMaximumWidth();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _widthReference is not null)
        {
            _widthReference.SizeChanged -= WidthReference_SizeChanged;
        }

        base.Dispose(disposing);
    }

    private void WidthReference_SizeChanged(object? sender, EventArgs e)
    {
        UpdateMaximumWidth();
    }

    private void UpdateMaximumWidth()
    {
        if (_widthReference is null)
        {
            return;
        }

        var width = _widthReference.ClientSize.Width - _horizontalInset - Margin.Horizontal;
        if (width <= 1)
        {
            return;
        }

        if (MaximumSize.Width != width)
        {
            MaximumSize = new Size(width, 0);
        }
    }
}

internal sealed class TerminalRichTextBox : RichTextBox
{
    private const int CaretWidth = 3;
    private const uint WindowMessageSetFocus = 0x0007;
    private const uint WindowMessageKillFocus = 0x0008;
    private const uint WindowMessageSetFont = 0x0030;
    private const uint WindowMessageKeyUp = 0x0101;
    private const uint WindowMessageChar = 0x0102;
    private const uint WindowMessageMouseWheel = 0x020A;
    private const uint WindowMessageLButtonUp = 0x0202;
    private const uint WindowMessageVScroll = 0x0115;
    private const uint WindowMessageHScroll = 0x0114;
    private IntPtr _caretBitmapHandle;

    public TerminalRichTextBox()
    {
        BorderStyle = BorderStyle.None;
        BackColor = Palette.Input;
        ForeColor = Palette.TerminalText;
        Font = new Font("Consolas", 11.25F, FontStyle.Regular, GraphicsUnit.Point);
        AcceptsTab = true;
        DetectUrls = false;
        EnableAutoDragDrop = false;
        HideSelection = false;
        ScrollBars = RichTextBoxScrollBars.Vertical;
        WordWrap = true;
        Margin = new Padding(0);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        RecreateTerminalCaret();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        ReleaseTerminalCaret();
        base.OnHandleDestroyed(e);
    }

    protected override void OnForeColorChanged(EventArgs e)
    {
        base.OnForeColorChanged(e);
        RecreateTerminalCaret();
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);

        switch ((uint)m.Msg)
        {
            case WindowMessageSetFocus:
            case WindowMessageSetFont:
            case WindowMessageKeyUp:
            case WindowMessageChar:
            case WindowMessageMouseWheel:
            case WindowMessageLButtonUp:
            case WindowMessageVScroll:
            case WindowMessageHScroll:
                RecreateTerminalCaret();
                break;
            case WindowMessageKillFocus:
                ReleaseTerminalCaret();
                break;
        }
    }

    private void RecreateTerminalCaret()
    {
        if (!IsHandleCreated || !Focused)
        {
            return;
        }

        ReleaseTerminalCaret();

        using var bitmap = new Bitmap(CaretWidth, Math.Max(18, Font.Height));
        using (var graphics = Graphics.FromImage(bitmap))
        using (var brush = new SolidBrush(Palette.TerminalCursor))
        {
            graphics.Clear(Color.Transparent);
            graphics.FillRectangle(brush, 0, 0, bitmap.Width, bitmap.Height);
        }

        _caretBitmapHandle = bitmap.GetHbitmap();
        if (!CreateCaret(Handle, _caretBitmapHandle, 0, 0))
        {
            ReleaseTerminalCaret();
            return;
        }

        ShowCaret(Handle);
    }

    private void ReleaseTerminalCaret()
    {
        if (IsHandleCreated)
        {
            DestroyCaret();
        }

        if (_caretBitmapHandle != IntPtr.Zero)
        {
            DeleteObject(_caretBitmapHandle);
            _caretBitmapHandle = IntPtr.Zero;
        }
    }

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool CreateCaret(IntPtr hWnd, IntPtr hBitmap, int nWidth, int nHeight);

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool ShowCaret(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool DestroyCaret();

    [DllImport("gdi32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern bool DeleteObject(IntPtr hObject);
}

internal static class KeyboardTransmitter
{
    private const uint InputKeyboard = 1u;
    private const uint KeyEventKeyUp = 0x0002u;
    private const uint KeyEventUnicode = 0x0004u;
    private const ushort VirtualKeyReturn = 0x0D;
    private const ushort VirtualKeyTab = 0x09;
    private const ushort VirtualKeyBack = 0x08;
    private static readonly int InputSize = Marshal.SizeOf(typeof(INPUT));

    public static void SendText(string text, double keyDelayMs, bool useEnterKey, CancellationToken cancellationToken)
    {
        foreach (var character in text)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (character)
            {
                case '\r':
                    continue;
                case '\n':
                    if (useEnterKey)
                    {
                        SendVirtualKey(VirtualKeyReturn);
                    }
                    break;
                case '\t':
                    SendVirtualKey(VirtualKeyTab);
                    break;
                case '\b':
                    SendVirtualKey(VirtualKeyBack);
                    break;
                default:
                    SendUnicodeCharacter(character);
                    break;
            }

            DelayBetweenKeys(keyDelayMs, cancellationToken);
        }
    }

    private static void DelayBetweenKeys(double keyDelayMs, CancellationToken cancellationToken)
    {
        if (keyDelayMs <= 0)
        {
            return;
        }

        var wholeMilliseconds = (int)Math.Floor(keyDelayMs);
        if (wholeMilliseconds > 0)
        {
            cancellationToken.WaitHandle.WaitOne(wholeMilliseconds);
            cancellationToken.ThrowIfCancellationRequested();
        }

        var fractionalMilliseconds = keyDelayMs - wholeMilliseconds;
        if (fractionalMilliseconds <= 0)
        {
            return;
        }

        var targetTimestamp = Stopwatch.GetTimestamp() +
            (long)Math.Round(fractionalMilliseconds / 1000d * Stopwatch.Frequency);

        while (Stopwatch.GetTimestamp() < targetTimestamp)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Thread.SpinWait(32);
        }
    }

    private static void SendUnicodeCharacter(char character)
    {
        var inputs = new[]
        {
            CreateUnicodeInput(character, keyUp: false),
            CreateUnicodeInput(character, keyUp: true),
        };

        SubmitInputs(inputs);
    }

    private static void SendVirtualKey(ushort keyCode)
    {
        var inputs = new[]
        {
            CreateVirtualKeyInput(keyCode, keyUp: false),
            CreateVirtualKeyInput(keyCode, keyUp: true),
        };

        SubmitInputs(inputs);
    }

    private static INPUT CreateUnicodeInput(char character, bool keyUp)
    {
        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = 0,
                    wScan = (ushort)character,
                    dwFlags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0u),
                    dwExtraInfo = IntPtr.Zero,
                    time = 0,
                },
            },
        };
    }

    private static INPUT CreateVirtualKeyInput(ushort keyCode, bool keyUp)
    {
        return new INPUT
        {
            type = InputKeyboard,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = keyCode,
                    wScan = 0,
                    dwFlags = keyUp ? KeyEventKeyUp : 0u,
                    dwExtraInfo = IntPtr.Zero,
                    time = 0,
                },
            },
        };
    }

    private static void SubmitInputs(INPUT[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, InputSize);
        if (sent != (uint)inputs.Length)
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new InvalidOperationException(
                $"SendInput fehlgeschlagen. Gesendet: {sent}/{inputs.Length}. Win32-Fehlercode: {errorCode}.");
        }
    }

    [DllImport("user32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    // The union must match the native Win32 INPUT union so sizeof(INPUT) is
    // correct on both x86 and x64. KEYBDINPUT alone is too small on x64.
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;

        [FieldOffset(0)]
        public KEYBDINPUT ki;

        [FieldOffset(0)]
        public HARDWAREINPUT hi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct HARDWAREINPUT
    {
        public uint uMsg;
        public ushort wParamL;
        public ushort wParamH;
    }
}

internal static class Palette
{
    public static readonly Color Background = Color.FromArgb(9, 13, 15);
    public static readonly Color Panel = Color.FromArgb(19, 24, 27);
    public static readonly Color Input = Color.FromArgb(12, 17, 19);
    public static readonly Color Border = Color.FromArgb(64, 83, 81);
    public static readonly Color Accent = Color.FromArgb(141, 198, 63);
    public static readonly Color AccentGlow = Color.FromArgb(92, 165, 54);
    public static readonly Color TerminalText = Color.FromArgb(104, 255, 92);
    public static readonly Color TerminalCursor = Color.FromArgb(132, 255, 120);
    public static readonly Color TextPrimary = Color.FromArgb(229, 232, 226);
    public static readonly Color TextMuted = Color.FromArgb(149, 164, 155);
}
