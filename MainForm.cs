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
    private readonly RichTextBox _textInput;
    private readonly Label _characterCountLabel;
    private readonly Label _delayValueLabel;
    private readonly Label _statusHeadlineLabel;
    private readonly Label _statusDetailLabel;
    private readonly Label _countdownLabel;
    private readonly CheckBox _minimizeCheckBox;
    private readonly Button _startButton;
    private readonly Button _cancelButton;
    private readonly List<Control> _editableControls = new();

    private CancellationTokenSource? _runCts;
    private int _delaySeconds = 3;

    public MainForm()
    {
        Text = "The Open Road Terminal";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(980, 680);
        ClientSize = new Size(1120, 760);
        BackColor = Palette.Background;
        ForeColor = Palette.TextPrimary;
        Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);

        SetStyle(
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.ResizeRedraw,
            true);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(28),
            ColumnCount = 1,
            RowCount = 2,
            BackColor = Color.Transparent,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 148F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        Controls.Add(root);

        root.Controls.Add(BuildHeaderPanel(), 0, 0);
        root.Controls.Add(BuildBodyPanel(), 0, 1);

        _textInput = BuildTextEditor();
        _characterCountLabel = new Label();
        _delayValueLabel = new Label();
        _statusHeadlineLabel = new Label();
        _statusDetailLabel = new Label();
        _countdownLabel = new Label();
        _minimizeCheckBox = CreateCheckBox("Fenster beim Start minimieren");
        _startButton = CreatePrimaryButton("START ROUTE");
        _cancelButton = CreateSecondaryButton("ABBRECHEN");

        BuildInteractiveContent(root);
        WireEvents();
        RefreshDelayDisplay();
        RefreshCharacterCount();
        SetStatus("STANDBY", "Text eingeben, Verzögerung festlegen, Start drücken und in das Zielfeld wechseln.");
        UpdateUiState(isRunning: false);
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
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 70F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
        header.Controls.Add(layout);

        var titleWrap = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
        };
        layout.Controls.Add(titleWrap, 0, 0);

        var eyebrow = new Label
        {
            AutoSize = true,
            Text = "CRIMINAL ENTERPRISE TERMINAL",
            ForeColor = Palette.TextMuted,
            Font = new Font("Bahnschrift SemiCondensed", 10.5F, FontStyle.Bold),
            Location = new Point(0, 4),
        };

        var title = new Label
        {
            AutoSize = true,
            Text = "THE OPEN ROAD",
            ForeColor = Palette.Accent,
            Font = new Font("Bahnschrift Condensed", 30F, FontStyle.Bold),
            Location = new Point(0, 24),
        };

        var subtitle = new Label
        {
            AutoSize = true,
            Text = "AUTO-TYPE TERMINAL / ACTIVE WINDOW DELIVERY",
            ForeColor = Palette.TextPrimary,
            Font = new Font("Bahnschrift SemiCondensed", 12F, FontStyle.Bold),
            Location = new Point(2, 82),
        };

        titleWrap.Controls.Add(eyebrow);
        titleWrap.Controls.Add(title);
        titleWrap.Controls.Add(subtitle);

        var badgeWrap = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 12, 0, 0),
        };
        layout.Controls.Add(badgeWrap, 1, 0);

        badgeWrap.Controls.Add(CreateMetricBadge("TARGET", "Aktives Fenster"));
        badgeWrap.Controls.Add(CreateMetricBadge("INPUT MODE", "Hardware Key Simulation"));
        badgeWrap.Controls.Add(CreateMetricBadge("STATUS", "Bereit"));

        return header;
    }

    private Control BuildBodyPanel()
    {
        var body = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
        };
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
        body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));

        return body;
    }

    private void BuildInteractiveContent(TableLayoutPanel root)
    {
        var body = (TableLayoutPanel)root.Controls[1];

        var textPanel = new TerminalPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24),
            Margin = new Padding(0, 0, 18, 0),
        };
        body.Controls.Add(textPanel, 0, 0);

        var rightPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = Color.Transparent,
        };
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 268F));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 178F));
        rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        body.Controls.Add(rightPanel, 1, 0);

        BuildTextPanel(textPanel);
        rightPanel.Controls.Add(BuildControlPanel(), 0, 0);
        rightPanel.Controls.Add(BuildStatusPanel(), 0, 1);
        rightPanel.Controls.Add(BuildInstructionPanel(), 0, 2);
    }

    private void BuildTextPanel(TerminalPanel textPanel)
    {
        var title = CreateSectionTitle("Textinhalt");
        title.Dock = DockStyle.Top;
        textPanel.Controls.Add(title);

        var subtitle = CreateBodyLabel("Hier kommt der Text hinein, den das Tool später in das aktuell fokussierte Eingabefeld tippt.");
        subtitle.Dock = DockStyle.Top;
        subtitle.Margin = new Padding(0, 8, 0, 18);
        textPanel.Controls.Add(subtitle);

        var editorShell = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(1),
            BackColor = Palette.Border,
            Margin = new Padding(0, 18, 0, 14),
        };
        textPanel.Controls.Add(editorShell);

        _textInput.Parent = editorShell;
        _textInput.Dock = DockStyle.Fill;
        editorShell.Controls.Add(_textInput);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            ColumnCount = 2,
            Height = 36,
            BackColor = Color.Transparent,
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 72F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
        textPanel.Controls.Add(footer);

        var hint = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = "Hinweis: Nach dem Start in das gewünschte Zielfeld wechseln. Dort landet der Text.",
            ForeColor = Palette.TextMuted,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
        };

        _characterCountLabel.AutoSize = true;
        _characterCountLabel.Dock = DockStyle.Fill;
        _characterCountLabel.ForeColor = Palette.Accent;
        _characterCountLabel.Font = new Font("Bahnschrift SemiBold", 10F, FontStyle.Bold);
        _characterCountLabel.TextAlign = ContentAlignment.MiddleRight;

        footer.Controls.Add(hint, 0, 0);
        footer.Controls.Add(_characterCountLabel, 1, 0);
    }

    private Control BuildControlPanel()
    {
        var panel = new TerminalPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            Margin = new Padding(0, 0, 0, 18),
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 5,
            BackColor = Color.Transparent,
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        panel.Controls.Add(layout);

        var title = CreateSectionTitle("Startverzögerung");
        title.Dock = DockStyle.Fill;
        layout.Controls.Add(title, 0, 0);

        var delayPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = Color.Transparent,
        };
        delayPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56F));
        delayPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        delayPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56F));
        layout.Controls.Add(delayPanel, 0, 1);

        var minusButton = CreateSquareButton("-");
        minusButton.Click += (_, _) => AdjustDelay(-1);
        delayPanel.Controls.Add(minusButton, 0, 0);
        _editableControls.Add(minusButton);

        var delayValueShell = new Panel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(0, 8, 0, 0),
            BackColor = Color.Transparent,
        };
        delayPanel.Controls.Add(delayValueShell, 1, 0);

        _delayValueLabel.Dock = DockStyle.Top;
        _delayValueLabel.Height = 46;
        _delayValueLabel.TextAlign = ContentAlignment.MiddleCenter;
        _delayValueLabel.ForeColor = Palette.Accent;
        _delayValueLabel.Font = new Font("Bahnschrift Condensed", 30F, FontStyle.Bold);
        delayValueShell.Controls.Add(_delayValueLabel);

        var delayHint = new Label
        {
            Dock = DockStyle.Top,
            Height = 26,
            Text = "Sekunden zwischen START und dem Tippen",
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = Palette.TextMuted,
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point),
        };
        delayValueShell.Controls.Add(delayHint);

        var plusButton = CreateSquareButton("+");
        plusButton.Click += (_, _) => AdjustDelay(1);
        delayPanel.Controls.Add(plusButton, 2, 0);
        _editableControls.Add(plusButton);

        var presetWrap = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = new Padding(0),
        };
        layout.Controls.Add(presetWrap, 0, 2);

        foreach (var preset in new[] { 3, 5, 10, 15 })
        {
            var presetButton = CreatePresetButton($"{preset}s");
            presetButton.Click += (_, _) => SetDelay(preset);
            presetWrap.Controls.Add(presetButton);
            _editableControls.Add(presetButton);
        }

        _minimizeCheckBox.Dock = DockStyle.Fill;
        _minimizeCheckBox.Checked = true;
        layout.Controls.Add(_minimizeCheckBox, 0, 3);
        _editableControls.Add(_minimizeCheckBox);

        var buttonWrap = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = Color.Transparent,
        };
        buttonWrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62F));
        buttonWrap.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38F));
        layout.Controls.Add(buttonWrap, 0, 4);

        _startButton.Dock = DockStyle.Fill;
        _startButton.Click += StartButton_Click;
        buttonWrap.Controls.Add(_startButton, 0, 0);

        _cancelButton.Dock = DockStyle.Fill;
        _cancelButton.Click += (_, _) => _runCts?.Cancel();
        buttonWrap.Controls.Add(_cancelButton, 1, 0);

        return panel;
    }

    private Control BuildStatusPanel()
    {
        var panel = new TerminalPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
            Margin = new Padding(0, 0, 0, 18),
        };

        var title = CreateSectionTitle("Status");
        title.Dock = DockStyle.Top;
        panel.Controls.Add(title);

        _statusHeadlineLabel.Dock = DockStyle.Top;
        _statusHeadlineLabel.Height = 44;
        _statusHeadlineLabel.ForeColor = Palette.Accent;
        _statusHeadlineLabel.Font = new Font("Bahnschrift SemiCondensed", 18F, FontStyle.Bold);
        _statusHeadlineLabel.Padding = new Padding(0, 12, 0, 0);
        panel.Controls.Add(_statusHeadlineLabel);

        _statusDetailLabel.Dock = DockStyle.Top;
        _statusDetailLabel.Height = 56;
        _statusDetailLabel.ForeColor = Palette.TextPrimary;
        _statusDetailLabel.Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
        panel.Controls.Add(_statusDetailLabel);

        var countdownWrap = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Padding = new Padding(0, 10, 0, 0),
        };
        panel.Controls.Add(countdownWrap);

        var caption = new Label
        {
            Dock = DockStyle.Top,
            Height = 20,
            Text = "Countdown",
            ForeColor = Palette.TextMuted,
            Font = new Font("Bahnschrift SemiCondensed", 10F, FontStyle.Bold),
        };
        countdownWrap.Controls.Add(caption);

        _countdownLabel.Dock = DockStyle.Fill;
        _countdownLabel.TextAlign = ContentAlignment.MiddleLeft;
        _countdownLabel.ForeColor = Palette.TextPrimary;
        _countdownLabel.Font = new Font("Bahnschrift Condensed", 28F, FontStyle.Bold);
        countdownWrap.Controls.Add(_countdownLabel);

        return panel;
    }

    private Control BuildInstructionPanel()
    {
        var panel = new TerminalPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(22),
        };

        var title = CreateSectionTitle("Bedienung");
        title.Dock = DockStyle.Top;
        panel.Controls.Add(title);

        var instructionText = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = Palette.TextPrimary,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
            Text =
                "1. Text links eintragen." + Environment.NewLine + Environment.NewLine +
                "2. Verzögerung in Sekunden einstellen." + Environment.NewLine + Environment.NewLine +
                "3. START drücken und während des Countdowns das Zielfeld fokussieren." + Environment.NewLine + Environment.NewLine +
                "4. Das Tool sendet den Text über simulierte Tastatureingaben in das aktive Fenster." + Environment.NewLine + Environment.NewLine +
                "Hinweis: Wenn das Zielprogramm Administratorrechte hat, muss dieses Tool gegebenenfalls ebenfalls erhöht gestartet werden.",
        };
        panel.Controls.Add(instructionText);

        return panel;
    }

    private RichTextBox BuildTextEditor()
    {
        var textBox = new RichTextBox
        {
            BorderStyle = BorderStyle.None,
            BackColor = Palette.Input,
            ForeColor = Palette.TextPrimary,
            Font = new Font("Consolas", 11.25F, FontStyle.Regular, GraphicsUnit.Point),
            AcceptsTab = true,
            DetectUrls = false,
            EnableAutoDragDrop = false,
            HideSelection = false,
            ScrollBars = RichTextBoxScrollBars.Vertical,
        };

        _editableControls.Add(textBox);
        return textBox;
    }

    private void WireEvents()
    {
        _textInput.TextChanged += (_, _) => RefreshCharacterCount();
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

    private static Label CreateSectionTitle(string text)
    {
        return new Label
        {
            AutoSize = false,
            Height = 38,
            Text = text.ToUpperInvariant(),
            ForeColor = Palette.TextPrimary,
            Font = new Font("Bahnschrift SemiCondensed", 18F, FontStyle.Bold),
        };
    }

    private static Label CreateBodyLabel(string text)
    {
        return new Label
        {
            AutoSize = true,
            Text = text,
            ForeColor = Palette.TextMuted,
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
        };
    }

    private static Panel CreateMetricBadge(string label, string value)
    {
        var panel = new Panel
        {
            Size = new Size(290, 28),
            Margin = new Padding(0, 0, 0, 10),
            BackColor = Color.FromArgb(30, 39, 42),
        };

        var left = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Left,
            Width = 108,
            Text = label,
            ForeColor = Palette.TextMuted,
            Font = new Font("Bahnschrift SemiCondensed", 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(10, 0, 0, 0),
        };

        var right = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = value,
            ForeColor = Palette.Accent,
            Font = new Font("Bahnschrift SemiCondensed", 10F, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleRight,
            Padding = new Padding(0, 0, 10, 0),
        };

        panel.Controls.Add(right);
        panel.Controls.Add(left);
        return panel;
    }

    private static CheckBox CreateCheckBox(string text)
    {
        return new CheckBox
        {
            AutoSize = false,
            Height = 32,
            Text = text,
            ForeColor = Palette.TextPrimary,
            BackColor = Color.Transparent,
            Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
        };
    }

    private static Button CreatePrimaryButton(string text)
    {
        return CreateButton(text, Palette.Accent, Palette.Background, Palette.AccentGlow);
    }

    private static Button CreateSecondaryButton(string text)
    {
        return CreateButton(text, Color.FromArgb(24, 31, 34), Palette.TextPrimary, Palette.Border);
    }

    private static Button CreatePresetButton(string text)
    {
        var button = CreateButton(text, Color.FromArgb(23, 29, 32), Palette.TextPrimary, Palette.Border);
        button.Width = 62;
        button.Height = 32;
        button.Margin = new Padding(0, 0, 8, 0);
        return button;
    }

    private static Button CreateSquareButton(string text)
    {
        var button = CreateButton(text, Color.FromArgb(23, 29, 32), Palette.Accent, Palette.Border);
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0);
        button.Font = new Font("Bahnschrift SemiCondensed", 18F, FontStyle.Bold);
        return button;
    }

    private static Button CreateButton(string text, Color backColor, Color foreColor, Color borderColor)
    {
        var button = new Button
        {
            Text = text,
            Height = 44,
            FlatStyle = FlatStyle.Flat,
            BackColor = backColor,
            ForeColor = foreColor,
            Font = new Font("Bahnschrift SemiCondensed", 11F, FontStyle.Bold),
            Cursor = Cursors.Hand,
            Margin = new Padding(0),
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
        using var borderPen = new Pen(Palette.Border, 1.25F);
        e.Graphics.DrawRectangle(borderPen, rect);

        using var accentPen = new Pen(Palette.Accent, 2.4F);
        e.Graphics.DrawLine(accentPen, rect.Left + 18, rect.Top + 16, rect.Left + 154, rect.Top + 16);

        using var cornerPen = new Pen(Color.FromArgb(80, Palette.Accent), 1F);
        e.Graphics.DrawLine(cornerPen, rect.Right - 42, rect.Bottom - 16, rect.Right - 14, rect.Bottom - 16);
        e.Graphics.DrawLine(cornerPen, rect.Right - 14, rect.Bottom - 42, rect.Right - 14, rect.Bottom - 16);
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
