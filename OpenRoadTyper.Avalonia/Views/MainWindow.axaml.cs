#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Threading;
using OpenRoadTyper.Core;
using OpenRoadTyper.Core.Abstractions;
using OpenRoadTyper.Core.Persistence;
using OpenRoadTyper.Core.Typing;

namespace OpenRoadTyper.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly IKeyboardInputService _keyboardInput = PlatformFactory.CreateKeyboardInputService();
    private readonly ISpeechRecognitionService _speechService = PlatformFactory.CreateNonWindowsSpeechRecognitionService();
    private readonly List<Control> _editableControls = new();

    private CultureInfo _recognitionCulture = CultureInfo.CurrentCulture;
    private CancellationTokenSource? _runCts;
    private bool _isListening;
    private int _delaySeconds = 3;
    private decimal _typingDelayMs;
    private bool _typingSpeedUsesSeconds = true;

    public MainWindow()
    {
        InitializeComponent();

        _editableControls.AddRange(new Control[]
        {
            PayloadTextBox, PasteClipboardButton, ClearTextButton, MicButton, MicLanguageButton,
            DecreaseDelayButton, IncreaseDelayButton, TypingSpeedButton, MinimizeCheckBox,
            UseEnterKeyCheckBox, SettingsButton,
        });

        WireEvents();
        LoadPersistedText();
        ApplySettings(SettingsStore.Load());
        RefreshDelayDisplay();
        RefreshTypingSpeedDisplay();
        RefreshCharacterCount();
        SetStatus("STANDBY", "Text eingeben, Verzögerung festlegen, Start drücken und in das Zielfeld wechseln.");
        UpdateUiState(isRunning: false);
    }

    private void WireEvents()
    {
        PayloadTextBox.TextChanged += (_, _) =>
        {
            RefreshCharacterCount();
            SavePersistedText();
        };
        PasteClipboardButton.Click += PasteClipboardButton_Click;
        ClearTextButton.Click += ClearTextButton_Click;
        MicButton.Click += MicButton_Click;
        MicLanguageButton.Click += MicLanguageButton_Click;
        DecreaseDelayButton.Click += (_, _) => AdjustDelay(-1);
        IncreaseDelayButton.Click += (_, _) => AdjustDelay(1);
        TypingSpeedButton.Click += TypingSpeedButton_Click;
        SettingsButton.Click += SettingsButton_Click;
        StartButton.Click += StartButton_Click;
        CancelButton.Click += (_, _) => _runCts?.Cancel();

        // Ask for keyboard-injection permission (XWayland on Linux) as soon
        // as the window is up, not when the first countdown runs out.
        Opened += (_, _) => Task.Run(() =>
        {
            try
            {
                _keyboardInput.RequestPermission();
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() => SetStatus("FAILSAFE", ex.Message));
            }
        });

        _speechService.Recognized += SpeechService_Recognized;
        _speechService.AudioLevelUpdated += SpeechService_AudioLevelUpdated;
        _speechService.Faulted += SpeechService_Faulted;

        Closing += (_, _) =>
        {
            _runCts?.Cancel();
            if (_isListening)
            {
                _speechService.Stop();
            }

            SavePersistedText();
        };
    }

    private void LoadPersistedText() => PayloadTextBox.Text = DraftStore.Load();

    private void SavePersistedText() => DraftStore.Save(PayloadTextBox.Text ?? string.Empty);

    private void ApplySettings(AppSettings settings)
    {
        SetDelay(settings.StartDelaySeconds);
        _typingDelayMs = settings.TypingDelayMilliseconds < 0 ? 0 : settings.TypingDelayMilliseconds;
        _typingSpeedUsesSeconds = settings.TypingSpeedUsesSeconds;
        RefreshTypingSpeedDisplay();
        MinimizeCheckBox.IsChecked = settings.MinimizeOnStart;
        UseEnterKeyCheckBox.IsChecked = settings.UseEnterKey;
        SetRecognitionLanguage(german: settings.MicLanguage != "en");
    }

    private async void SettingsButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new SettingsDialog(SettingsStore.Load());
        var settings = await dialog.ShowDialog<AppSettings?>(this);
        if (settings is null)
        {
            return;
        }

        SettingsStore.Save(settings);

        var wasListening = _isListening;
        if (wasListening)
        {
            StopListening();
        }

        ApplySettings(settings);

        if (wasListening)
        {
            StartListening();
        }

        SetStatus("SETTINGS SAVED", "Standard-Konfiguration gespeichert und übernommen.");
    }

    private async void PasteClipboardButton_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null)
        {
            return;
        }

        var text = await clipboard.TryGetTextAsync();
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("CLIPBOARD EMPTY", "Die Zwischenablage enthält aktuell keinen nutzbaren Text.");
            return;
        }

        PayloadTextBox.Text = text;
        PayloadTextBox.CaretIndex = PayloadTextBox.Text.Length;
        PayloadTextBox.Focus();

        SetStatus("CLIPBOARD READY", $"{text.Length} Zeichen wurden in den Textinhalt übernommen.");
    }

    private void ClearTextButton_Click(object? sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(PayloadTextBox.Text))
        {
            SetStatus("TEXT EMPTY", "Das Textfeld ist bereits leer.");
            return;
        }

        PayloadTextBox.Text = string.Empty;
        PayloadTextBox.Focus();
        SetStatus("TEXT CLEARED", "Der Textinhalt wurde entfernt.");
    }

    private void MicLanguageButton_Click(object? sender, RoutedEventArgs e)
    {
        var wasListening = _isListening;
        if (wasListening)
        {
            StopListening();
        }

        var switchToGerman = _recognitionCulture.TwoLetterISOLanguageName != "de";
        SetRecognitionLanguage(switchToGerman);
        SetStatus("LANGUAGE SET", switchToGerman
            ? "Spracherkennung auf Deutsch umgestellt."
            : "Spracherkennung auf Englisch umgestellt.");

        if (wasListening)
        {
            StartListening();
        }
    }

    private void SetRecognitionLanguage(bool german)
    {
        if (german)
        {
            _recognitionCulture = CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "de"
                ? CultureInfo.CurrentCulture
                : new CultureInfo("de-DE");
            MicLanguageButton.Content = "DE";
        }
        else
        {
            _recognitionCulture = new CultureInfo("en-US");
            MicLanguageButton.Content = "EN";
        }
    }

    private void MicButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_isListening)
        {
            StopListening();
        }
        else
        {
            StartListening();
        }
    }

    private void StartListening()
    {
        try
        {
            _speechService.Start(_recognitionCulture);
            _isListening = true;
            MicButton.Classes.Set("listening", true);
            MicButton.Classes.Set("processing", false);
            SetStatus("LISTENING", $"Erkennung aktiv ({_recognitionCulture.DisplayName}). Sprechen Sie jetzt.");
        }
        catch (Exception ex)
        {
            _isListening = false;
            MicButton.Classes.Set("listening", false);
            SetStatus("MIC ERROR", "Fehler: " + ex.Message);
        }
    }

    private void StopListening()
    {
        _speechService.Stop();
        _isListening = false;
        MicButton.Classes.Set("listening", false);
        MicButton.Classes.Set("processing", false);
        SetStatus("STANDBY", "Spracherkennung beendet.");
    }

    private void SpeechService_Recognized(object? sender, SpeechRecognizedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var text = e.Text;
            var current = PayloadTextBox.Text ?? string.Empty;

            if (current.Length > 0)
            {
                var lastChar = current[^1];
                if (!char.IsWhiteSpace(lastChar) || char.IsPunctuation(lastChar))
                {
                    current += " ";
                }
            }

            PayloadTextBox.Text = current + text;
            PayloadTextBox.CaretIndex = PayloadTextBox.Text.Length;
        });
    }

    private void SpeechService_AudioLevelUpdated(object? sender, SpeechAudioLevelEventArgs e)
    {
        // Reserved for a future audio-level meter; the Avalonia UI currently
        // only shows discrete listening/idle/processing states.
    }

    private void SpeechService_Faulted(object? sender, Exception e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _isListening = false;
            MicButton.Classes.Set("listening", false);
            SetStatus("MIC ERROR", e.Message);
        });
    }

    private async void StartButton_Click(object? sender, RoutedEventArgs e)
    {
        if (_runCts is not null)
        {
            return;
        }

        var payload = PayloadTextBox.Text;
        if (string.IsNullOrWhiteSpace(payload))
        {
            SetStatus("NO PAYLOAD", "Das Textfeld ist leer. Erst Text eintragen, dann starten.");
            PayloadTextBox.Focus();
            return;
        }

        // Check the keyboard-injection backend (e.g. xdotool/ydotool on
        // Linux) up front, so a missing dependency shows up immediately
        // instead of after the user has waited through the whole countdown.
        if (!_keyboardInput.TryPrepare(out var unavailableReason))
        {
            SetStatus("FAILSAFE", unavailableReason ?? "Tastatursimulation ist auf diesem System nicht verfügbar.");
            return;
        }

        _runCts = new CancellationTokenSource();
        UpdateUiState(isRunning: true);
        var token = _runCts.Token;

        try
        {
            if (MinimizeCheckBox.IsChecked == true)
            {
                WindowState = WindowState.Minimized;
                await Task.Delay(180, token);
            }

            for (var remaining = _delaySeconds; remaining > 0; remaining--)
            {
                SetStatus("LOCK TARGET", $"Jetzt in das Zielfeld wechseln. Das Tippen startet in {remaining} Sek.");
                CountdownText.Text = $"{remaining:00}s";
                await Task.Delay(1000, token);
            }

            SetStatus(
                "TRANSMITTING",
                $"Sende {payload.Length} Zeichen mit {TypingSpeedFormatter.Format(_typingDelayMs, _typingSpeedUsesSeconds)} pro Taste.");
            CountdownText.Text = "LIVE";

            var useEnterKey = UseEnterKeyCheckBox.IsChecked == true;
            await Task.Run(() => _keyboardInput.SendText(payload, (double)_typingDelayMs, useEnterKey, token), token);

            SetStatus("JOB COMPLETE", "Text wurde erfolgreich in das aktive Fenster gesendet.");
            CountdownText.Text = "DONE";
        }
        catch (OperationCanceledException)
        {
            SetStatus("ABORTED", "Vorgang wurde gestoppt.");
            CountdownText.Text = "--";
        }
        catch (Exception ex)
        {
            SetStatus("FAILSAFE", ex.Message);
            CountdownText.Text = "ERR";
        }
        finally
        {
            _runCts.Dispose();
            _runCts = null;
            UpdateUiState(isRunning: false);
            RefreshDelayDisplay();
        }
    }

    private void PresetDelayButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tagText } && int.TryParse(tagText, out var seconds))
        {
            SetDelay(seconds);
        }
    }

    private void SetDelay(int seconds)
    {
        _delaySeconds = TypingSpeedFormatter.Clamp(seconds, 1, 60);
        RefreshDelayDisplay();
    }

    private void AdjustDelay(int delta) => SetDelay(_delaySeconds + delta);

    private void RefreshDelayDisplay()
    {
        DelayValueText.Text = $"{_delaySeconds:00}s";
        if (_runCts is null)
        {
            CountdownText.Text = $"{_delaySeconds:00}s";
        }
    }

    private void RefreshTypingSpeedDisplay()
    {
        TypingSpeedButton.Content = $"Tastenintervall: {TypingSpeedFormatter.Format(_typingDelayMs, _typingSpeedUsesSeconds)}";
    }

    private async void TypingSpeedButton_Click(object? sender, RoutedEventArgs e)
    {
        var dialog = new TypingSpeedDialog(_typingDelayMs, _typingSpeedUsesSeconds);
        var result = await dialog.ShowDialog<TypingSpeedDialog.Result?>(this);
        if (result is null)
        {
            return;
        }

        _typingDelayMs = result.Value.DelayMilliseconds;
        _typingSpeedUsesSeconds = result.Value.UseSeconds;
        RefreshTypingSpeedDisplay();
        SetStatus("SPEED SET", $"Tastenintervall auf {TypingSpeedFormatter.Format(_typingDelayMs, _typingSpeedUsesSeconds)} pro Taste gesetzt.");
    }

    private void RefreshCharacterCount()
    {
        CharacterCountText.Text = $"{PayloadTextBox.Text?.Length ?? 0} Zeichen";
    }

    private void UpdateUiState(bool isRunning)
    {
        foreach (var control in _editableControls)
        {
            control.IsEnabled = !isRunning;
        }

        StartButton.IsEnabled = !isRunning;
        CancelButton.IsEnabled = isRunning;

        if (!isRunning && string.IsNullOrWhiteSpace(StatusHeadlineText.Text))
        {
            SetStatus("STANDBY", "Bereit für den nächsten Versand.");
        }
    }

    private void SetStatus(string headline, string detail)
    {
        StatusHeadlineText.Text = headline;
        StatusDetailText.Text = detail;
        HeaderStatusValueText.Text = headline == "STANDBY" ? "Bereit" : headline;
    }
}
