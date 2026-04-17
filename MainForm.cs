#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenRoadTyper;

public sealed class MainForm : Form
{
    private readonly TableLayoutPanel _bodyLayout;
    private readonly RichTextBox _textInput;
    private readonly Label _characterCountLabel;
    private readonly Label _delayValueLabel;
    private readonly WrappingLabel _statusHeadlineLabel;
    private readonly WrappingLabel _statusDetailLabel;
    private readonly Label _countdownLabel;
    private readonly CheckBox _minimizeCheckBox;
    private readonly Button _pasteClipboardButton;
    private readonly Button _clearTextButton;
    private readonly Button _startButton;
    private readonly Button _cancelButton;
    private readonly List<Control> _editableControls = new();

    private CancellationTokenSource? _runCts;
    private int _delaySeconds = 3;

    public MainForm()
    {
        SuspendLayout();

        Text = "The Open Road Terminal";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1080, 760);
        ClientSize = new Size(1240, 840);
        BackColor = Palette.Background;
        ForeColor = Palette.TextPrimary;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96F, 96F);

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        _textInput = BuildTextEditor();
        _characterCountLabel = CreateCounterLabel();
        _delayValueLabel = CreateDisplayLabel(30F, Palette.Accent, ContentAlignment.MiddleCenter);
        _statusHeadlineLabel = CreateWrappingLabel(
            18F,
            FontStyle.Bold,
            "Bahnschrift SemiCondensed",
            Palette.Accent,
            ContentAlignment.MiddleLeft);
        _statusDetailLabel = CreateWrappingLabel(
            10F,
            FontStyle.Regular,
            "Segoe UI",
            Palette.TextPrimary,
            ContentAlignment.MiddleLeft);
        _countdownLabel = CreateDisplayLabel(28F, Palette.TextPrimary, ContentAlignment.MiddleLeft);
        _minimizeCheckBox = CreateCheckBox("Fenster beim Start minimieren");
        _pasteClipboardButton = CreateSecondaryButton("Aus Zwischenablage einfügen");
        _clearTextButton = CreateSecondaryButton("Textfeld leeren");
        _startButton = CreatePrimaryButton("START ROUTE");
        _cancelButton = CreateSecondaryButton("ABBRECHEN");

        _bodyLayout = BuildBodyPanel();

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.Controls.Add(BuildHeaderPanel(), 0, 0);
        root.Controls.Add(_bodyLayout, 0, 1);
        Controls.Add(root);

        WireEvents();
        RefreshDelayDisplay();
        RefreshCharacterCount();
        SetStatus("STANDBY", "Text eingeben, Verzögerung festlegen, Start drücken und in das Zielfeld wechseln.");
        UpdateUiState(isRunning: false);

        ResumeLayout(performLayout: true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        UpdateSidebarWidth();
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
            Dock = DockStyle.Fill,
            Padding = new Padding(28, 22, 28, 22),
            Margin = new Padding(0, 0, 0, 22),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32F));
        header.Controls.Add(layout);

        var titleLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        titleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(titleLayout, 0, 0);

        var eyebrow = new Label
        {
            AutoSize = true,
            Text = "CRIMINAL ENTERPRISE TERMINAL",
            ForeColor = Palette.TextMuted,
            Font = new Font("Bahnschrift SemiCondensed", 10.5F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 4),
        };

        var title = new Label
        {
            AutoSize = true,
            Text = "THE OPEN ROAD",
            ForeColor = Palette.Accent,
            Font = new Font("Bahnschrift Condensed", 30F, FontStyle.Bold),
            Margin = new Padding(0),
        };

        var subtitle = CreateWrappingLabel(
            12F,
            FontStyle.Bold,
            "Bahnschrift SemiCondensed",
            Palette.TextPrimary,
            ContentAlignment.MiddleLeft);
        subtitle.Text = "AUTO-TYPE TERMINAL / ACTIVE WINDOW DELIVERY";
        subtitle.Dock = DockStyle.Fill;
        subtitle.Margin = new Padding(0, 6, 0, 0);

        titleLayout.Controls.Add(eyebrow, 0, 0);
        titleLayout.Controls.Add(title, 0, 1);
        titleLayout.Controls.Add(subtitle, 0, 2);

        var badgeLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 4, 0, 0),
            MinimumSize = new Size(300, 0),
        };
        badgeLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        badgeLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        badgeLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(badgeLayout, 1, 0);

        badgeLayout.Controls.Add(CreateMetricBadge("TARGET", "Aktives Fenster"), 0, 0);
        badgeLayout.Controls.Add(CreateMetricBadge("INPUT MODE", "Hardware Key Simulation"), 0, 1);
        badgeLayout.Controls.Add(CreateMetricBadge("STATUS", "Bereit"), 0, 2);

        return header;
    }

    private TableLayoutPanel BuildBodyPanel()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 390F));
        body.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        body.SizeChanged += (_, _) => UpdateSidebarWidth();

        var textPanel = BuildTextPanel();
        textPanel.Margin = new Padding(0, 0, 18, 0);
        textPanel.MinimumSize = new Size(520, 0);
        body.Controls.Add(textPanel, 0, 0);

        var sidebar = BuildSidebarPanel();
        sidebar.MinimumSize = new Size(360, 0);
        body.Controls.Add(sidebar, 1, 0);

        return body;
    }

    private Control BuildTextPanel()
    {
        var textPanel = new TerminalPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        textPanel.Controls.Add(layout);

        var title = CreateSectionTitle("Textinhalt");
        title.Margin = new Padding(0);
        layout.Controls.Add(title, 0, 0);

        var subtitle = CreateBodyLabel("Hier kommt der Text hinein, den das Tool später in das aktuell fokussierte Eingabefeld tippt.");
        subtitle.Dock = DockStyle.Fill;
        subtitle.Margin = new Padding(0, 6, 0, 14);
        layout.Controls.Add(subtitle, 0, 1);

        var buttonStrip = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = true,
            FlowDirection = FlowDirection.LeftToRight,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 14),
        };
        buttonStrip.Controls.Add(_pasteClipboardButton);
        buttonStrip.Controls.Add(_clearTextButton);
        layout.Controls.Add(buttonStrip, 0, 2);

        _editableControls.Add(_pasteClipboardButton);
        _editableControls.Add(_clearTextButton);

        var editorShell = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            BackColor = Palette.Border,
            Margin = new Padding(0, 0, 0, 14),
        };
        _textInput.Dock = DockStyle.Fill;
        editorShell.Controls.Add(_textInput);
        layout.Controls.Add(editorShell, 0, 3);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0),
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.Controls.Add(footer, 0, 4);

        var hint = CreateMetaLabel("Hinweis: Nach dem Start in das gewünschte Zielfeld wechseln. Dort landet der Text.", ContentAlignment.MiddleLeft);
        hint.Dock = DockStyle.Fill;
        hint.Margin = new Padding(0, 0, 12, 0);
        footer.Controls.Add(hint, 0, 0);

        _characterCountLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
        footer.Controls.Add(_characterCountLabel, 1, 0);

        return textPanel;
    }

    private Control BuildSidebarPanel()
    {
        var sidebar = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        var controlPanel = BuildControlPanel();
        controlPanel.Margin = new Padding(0, 0, 0, 18);
        sidebar.Controls.Add(controlPanel, 0, 0);

        var statusPanel = BuildStatusPanel();
        statusPanel.Margin = new Padding(0, 0, 0, 18);
        sidebar.Controls.Add(statusPanel, 0, 1);

        sidebar.Controls.Add(BuildInstructionPanel(), 0, 2);

        return sidebar;
    }

    private TerminalPanel BuildControlPanel()
    {
        var panel = new TerminalPanel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(22),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 6,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        for (var i = 0; i < 6; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var title = CreateSectionTitle("Startverzögerung");
        title.Margin = new Padding(0, 0, 0, 6);
        layout.Controls.Add(title, 0, 0);

        var delayInfo = CreateMetaLabel("Zeit zwischen START und dem Beginn der Tastatureingaben.", ContentAlignment.MiddleLeft);
        delayInfo.Dock = DockStyle.Fill;
        delayInfo.Margin = new Padding(0, 0, 0, 14);
        layout.Controls.Add(delayInfo, 0, 1);

        var delayPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 16),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        delayPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62F));
        delayPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        delayPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 62F));
        layout.Controls.Add(delayPanel, 0, 2);

        var minusButton = CreateIconButton("-");
        minusButton.Click += (_, _) => AdjustDelay(-1);
        delayPanel.Controls.Add(minusButton, 0, 0);
        _editableControls.Add(minusButton);

        var valueLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(10, 0, 10, 0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        valueLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        valueLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        delayPanel.Controls.Add(valueLayout, 1, 0);

        _delayValueLabel.Anchor = AnchorStyles.None;
        _delayValueLabel.Margin = new Padding(0, 0, 0, 4);
        valueLayout.Controls.Add(_delayValueLabel, 0, 0);

        var delayHint = CreateMetaLabel("Sekunden zwischen START und dem Tippen", ContentAlignment.MiddleCenter);
        delayHint.Dock = DockStyle.Fill;
        delayHint.Margin = new Padding(0);
        valueLayout.Controls.Add(delayHint, 0, 1);

        var plusButton = CreateIconButton("+");
        plusButton.Click += (_, _) => AdjustDelay(1);
        delayPanel.Controls.Add(plusButton, 2, 0);
        _editableControls.Add(plusButton);

        var presetWrap = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 0, 0, 10),
        };
        foreach (var preset in new[] { 3, 5, 10, 15 })
        {
            var presetButton = CreatePresetButton($"{preset}s");
            presetButton.Click += (_, _) => SetDelay(preset);
            presetWrap.Controls.Add(presetButton);
            _editableControls.Add(presetButton);
        }
        layout.Controls.Add(presetWrap, 0, 3);

        _minimizeCheckBox.Checked = true;
        _minimizeCheckBox.Margin = new Padding(0, 0, 0, 12);
        layout.Controls.Add(_minimizeCheckBox, 0, 4);
        _editableControls.Add(_minimizeCheckBox);

        var actionWrap = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };

        _startButton.Click += StartButton_Click;
        _cancelButton.Click += (_, _) => _runCts?.Cancel();
        actionWrap.Controls.Add(_startButton);
        actionWrap.Controls.Add(_cancelButton);
        layout.Controls.Add(actionWrap, 0, 5);

        panel.Controls.Add(layout);
        return panel;
    }

    private TerminalPanel BuildStatusPanel()
    {
        var panel = new TerminalPanel
        {
            Dock = DockStyle.Top,
            Padding = new Padding(22),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        for (var i = 0; i < 5; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        var title = CreateSectionTitle("Status");
        title.Margin = new Padding(0, 0, 0, 8);
        layout.Controls.Add(title, 0, 0);

        _statusHeadlineLabel.Dock = DockStyle.Fill;
        _statusHeadlineLabel.Margin = new Padding(0, 0, 0, 6);
        layout.Controls.Add(_statusHeadlineLabel, 0, 1);

        _statusDetailLabel.Dock = DockStyle.Fill;
        _statusDetailLabel.Margin = new Padding(0, 0, 0, 16);
        layout.Controls.Add(_statusDetailLabel, 0, 2);

        var caption = new Label
        {
            AutoSize = true,
            Text = "Countdown",
            ForeColor = Palette.TextMuted,
            Font = new Font("Bahnschrift SemiCondensed", 10F, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 2),
        };
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
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.Controls.Add(layout);

        var title = CreateSectionTitle("Bedienung");
        title.Margin = new Padding(0, 0, 0, 10);
        layout.Controls.Add(title, 0, 0);

        var instructionText = CreateBodyLabel(
            "1. Text links eintragen." + Environment.NewLine + Environment.NewLine +
            "2. Verzögerung in Sekunden einstellen." + Environment.NewLine + Environment.NewLine +
            "3. START drücken und während des Countdowns das Zielfeld fokussieren." + Environment.NewLine + Environment.NewLine +
            "4. Das Tool sendet den Text über simulierte Tastatureingaben in das aktive Fenster." + Environment.NewLine + Environment.NewLine +
            "Hinweis: Wenn das Zielprogramm Administratorrechte hat, muss dieses Tool gegebenenfalls ebenfalls erhöht gestartet werden.",
            ContentAlignment.TopLeft);
        instructionText.Dock = DockStyle.Fill;
        instructionText.ForeColor = Palette.TextPrimary;
        layout.Controls.Add(instructionText, 0, 1);

        return panel;
    }

    private RichTextBox BuildTextEditor()
    {
        var textBox = new RichTextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Palette.Input,
            ForeColor = Palette.TextPrimary,
            Font = new Font("Consolas", 11F, FontStyle.Regular, GraphicsUnit.Point),
            AcceptsTab = true,
            DetectUrls = false,
            EnableAutoDragDrop = false,
            HideSelection = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
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
    }

    private void PasteClipboardButton_Click(object? sender, EventArgs e)
    {
        try
        {
            if (!Clipboard.ContainsText())
            {
                SetStatus("CLIPBOARD EMPTY", "Die Zwischenablage enthält aktuell keinen Text.");
                return;
            }

            var clipboardText = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(clipboardText))
            {
                SetStatus("CLIPBOARD EMPTY", "Die Zwischenablage enthält aktuell keinen nutzbaren Text.");
                return;
            }

            _textInput.Text = clipboardText;
            _textInput.SelectionStart = _textInput.TextLength;
            _textInput.ScrollToCaret();
            _textInput.Focus();

            SetStatus("CLIPBOARD READY", $"{clipboardText.Length} Zeichen wurden in den Textinhalt übernommen.");
        }
        catch (ExternalException)
        {
            SetStatus("CLIPBOARD BUSY", "Auf die Zwischenablage konnte gerade nicht zugegriffen werden. Bitte kurz erneut versuchen.");
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
                SetStatus("LOCK TARGET", $"Jetzt in das Zielfeld wechseln. Das Tippen startet in {remaining} Sek.");
                _countdownLabel.Text = $"{remaining:00}s";
                await Task.Delay(1000, token);
            }

            SetStatus("TRANSMITTING", $"Sende {payload.Length} Zeichen über die Windows-Tastatur-API.");
            _countdownLabel.Text = "LIVE";

            await Task.Run(() => KeyboardTransmitter.SendText(payload, 8, token), token);

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
            SetStatus("STANDBY", "Bereit für den nächsten Versand.");
        }
    }

    private void SetStatus(string headline, string detail)
    {
        _statusHeadlineLabel.Text = headline;
        _statusDetailLabel.Text = detail;
    }

    private void UpdateSidebarWidth()
    {
        if (_bodyLayout.ClientSize.Width <= 0 || _bodyLayout.ColumnStyles.Count < 2)
        {
            return;
        }

        const int sidebarMinWidth = 360;
        const int sidebarMaxWidth = 460;
        const int mainMinWidth = 520;

        var availableWidth = _bodyLayout.ClientSize.Width;
        var preferredSidebarWidth = Clamp((int)Math.Round(availableWidth * 0.34), sidebarMinWidth, sidebarMaxWidth);
        var maxSidebarWidth = Math.Max(sidebarMinWidth, availableWidth - mainMinWidth);
        var sidebarWidth = Math.Min(preferredSidebarWidth, maxSidebarWidth);

        _bodyLayout.ColumnStyles[1].Width = sidebarWidth;
    }

    private static Label CreateSectionTitle(string text)
    {
        return new Label
        {
            AutoSize = true,
            Text = text.ToUpperInvariant(),
            ForeColor = Palette.TextPrimary,
            Font = new Font("Bahnschrift SemiCondensed", 18F, FontStyle.Bold),
        };
    }

    private static WrappingLabel CreateBodyLabel(string text, ContentAlignment textAlign = ContentAlignment.MiddleLeft)
    {
        var label = CreateWrappingLabel(10F, FontStyle.Regular, "Segoe UI", Palette.TextMuted, textAlign);
        label.Text = text;
        return label;
    }

    private static WrappingLabel CreateMetaLabel(string text, ContentAlignment textAlign)
    {
        var label = CreateWrappingLabel(9.25F, FontStyle.Regular, "Segoe UI", Palette.TextMuted, textAlign);
        label.Text = text;
        return label;
    }

    private static WrappingLabel CreateWrappingLabel(
        float size,
        FontStyle style,
        string fontFamily,
        Color foreColor,
        ContentAlignment textAlign)
    {
        return new WrappingLabel
        {
            ForeColor = foreColor,
            Font = new Font(fontFamily, size, style, GraphicsUnit.Point),
            TextAlign = textAlign,
            Margin = new Padding(0),
        };
    }

    private static Label CreateDisplayLabel(float size, Color foreColor, ContentAlignment textAlign)
    {
        return new Label
        {
            AutoSize = true,
            ForeColor = foreColor,
            Font = new Font("Bahnschrift Condensed", size, FontStyle.Bold),
            TextAlign = textAlign,
            Margin = new Padding(0),
        };
    }

    private static Label CreateCounterLabel()
    {
        return new Label
        {
            AutoSize = true,
            ForeColor = Palette.Accent,
            Font = new Font("Bahnschrift SemiBold", 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight,
            Margin = new Padding(0),
        };
    }

    private static Control CreateMetricBadge(string label, string value)
    {
        var badge = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.FromArgb(30, 39, 42),
            Margin = new Padding(0, 0, 0, 10),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(290, 38),
        };
        badge.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        badge.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var left = new Label
        {
            AutoSize = true,
            Text = label,
            ForeColor = Palette.TextMuted,
            Font = new Font("Bahnschrift SemiCondensed", 10F, FontStyle.Bold),
            Padding = new Padding(12, 9, 14, 9),
            Margin = new Padding(0),
        };

        var right = new Label
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(140, 38),
            Text = value,
            ForeColor = Palette.Accent,
            Font = new Font("Bahnschrift SemiCondensed", 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(10, 0, 12, 0),
            Margin = new Padding(0),
        };

        badge.Controls.Add(left, 0, 0);
        badge.Controls.Add(right, 1, 0);
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
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
            Padding = new Padding(0, 2, 0, 2),
            Margin = new Padding(0),
            UseVisualStyleBackColor = false,
        };
    }

    private static Button CreatePrimaryButton(string text)
    {
        var button = CreateButton(text, Palette.Accent, Palette.Background, Palette.AccentGlow);
        button.MinimumSize = new Size(196, 52);
        return button;
    }

    private static Button CreateSecondaryButton(string text)
    {
        var button = CreateButton(text, Color.FromArgb(24, 31, 34), Palette.TextPrimary, Palette.Border);
        button.MinimumSize = new Size(150, 48);
        return button;
    }

    private static Button CreatePresetButton(string text)
    {
        var button = CreateButton(text, Color.FromArgb(23, 29, 32), Palette.TextPrimary, Palette.Border);
        button.MinimumSize = new Size(64, 40);
        button.Padding = new Padding(16, 8, 16, 8);
        return button;
    }

    private static Button CreateIconButton(string text)
    {
        var button = CreateButton(text, Color.FromArgb(23, 29, 32), Palette.Accent, Palette.Border);
        button.AutoSize = false;
        button.Dock = DockStyle.Fill;
        button.MinimumSize = new Size(62, 62);
        button.Padding = new Padding(0);
        button.Font = new Font("Bahnschrift SemiCondensed", 18F, FontStyle.Bold);
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
            Font = new Font("Bahnschrift SemiCondensed", 11F, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(0, 0, 10, 10),
            Padding = new Padding(18, 10, 18, 10),
            UseMnemonic = false,
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

internal sealed class WrappingLabel : Label
{
    public WrappingLabel()
    {
        AutoSize = false;
        AutoEllipsis = false;
        UseMnemonic = false;
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        AdjustHeight();
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        AdjustHeight();
    }

    protected override void OnPaddingChanged(EventArgs e)
    {
        base.OnPaddingChanged(e);
        AdjustHeight();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        AdjustHeight();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        var width = proposedSize.Width > 0 ? proposedSize.Width : Width;
        if (width <= 0)
        {
            width = 1;
        }

        var availableTextBounds = new Size(Math.Max(1, width - Padding.Horizontal), int.MaxValue);
        var measured = TextRenderer.MeasureText(
            Text ?? string.Empty,
            Font,
            availableTextBounds,
            TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

        return new Size(width, Math.Max(MinimumSize.Height, measured.Height + Padding.Vertical + 2));
    }

    private void AdjustHeight()
    {
        if (Width <= 0)
        {
            return;
        }

        var preferredHeight = GetPreferredSize(new Size(Width, 0)).Height;
        if (Height != preferredHeight)
        {
            Height = preferredHeight;
        }
    }
}

internal static class KeyboardTransmitter
{
    private const int InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;
    private const ushort VirtualKeyReturn = 0x0D;
    private const ushort VirtualKeyTab = 0x09;
    private const ushort VirtualKeyBack = 0x08;

    public static void SendText(string text, int keyDelayMs, CancellationToken cancellationToken)
    {
        foreach (var character in text)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (character)
            {
                case '\r':
                    continue;
                case '\n':
                    SendVirtualKey(VirtualKeyReturn);
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

            Thread.Sleep(keyDelayMs);
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
                    wScan = character,
                    dwFlags = KeyEventUnicode | (keyUp ? KeyEventKeyUp : 0),
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
                    dwFlags = keyUp ? KeyEventKeyUp : 0,
                    dwExtraInfo = IntPtr.Zero,
                    time = 0,
                },
            },
        };
    }

    private static void SubmitInputs(INPUT[] inputs)
    {
        var sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
        if (sent != (uint)inputs.Length)
        {
            var errorCode = Marshal.GetLastWin32Error();
            throw new InvalidOperationException($"SendInput fehlgeschlagen. Win32-Fehlercode: {errorCode}.");
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public KEYBDINPUT ki;
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
}

internal static class Palette
{
    public static readonly Color Background = Color.FromArgb(9, 13, 15);
    public static readonly Color Panel = Color.FromArgb(19, 24, 27);
    public static readonly Color Input = Color.FromArgb(12, 17, 19);
    public static readonly Color Border = Color.FromArgb(64, 83, 81);
    public static readonly Color Accent = Color.FromArgb(141, 198, 63);
    public static readonly Color AccentGlow = Color.FromArgb(92, 165, 54);
    public static readonly Color TextPrimary = Color.FromArgb(229, 232, 226);
    public static readonly Color TextMuted = Color.FromArgb(149, 164, 155);
}
