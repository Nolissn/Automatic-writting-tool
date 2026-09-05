#nullable enable

using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading;
using OpenRoadTyper.Core.Abstractions;
using Vosk;

namespace OpenRoadTyper.Core.PlatformLinux;

/// <summary>
/// Offline dictation for Linux (and any other non-Windows OS) using the Vosk
/// speech engine, which ships as a self-contained native library for Linux,
/// Windows and macOS. Windows has System.Speech/SAPI built in, but Linux has
/// no first-party equivalent, so Vosk is the closest match: fully offline,
/// no cloud dependency, works with the same "install a language model" model
/// as SAPI's own language packs.
///
/// Microphone audio is captured by shelling out to whichever recorder is
/// already on the system (PipeWire's pw-record, PulseAudio's parecord, or
/// plain ALSA arecord) rather than adding a native audio-capture dependency
/// - these tools are what actually own the audio device on a modern Ubuntu
/// desktop, so this is the same approach real Linux voice-input tools use.
/// </summary>
public sealed class VoskSpeechRecognitionService : ISpeechRecognitionService
{
    private const int SampleRateHz = 16000;
    private const float RecognitionConfidenceThreshold = 0.3f;

    private Process? _recorderProcess;
    private Thread? _captureThread;
    private CancellationTokenSource? _stopCts;
    private Model? _model;
    private VoskRecognizer? _recognizer;

    public bool IsListening { get; private set; }

    public event EventHandler<SpeechRecognizedEventArgs>? Recognized;

    public event EventHandler<SpeechAudioLevelEventArgs>? AudioLevelUpdated;

    public event EventHandler<Exception>? Faulted;

    public void Start(CultureInfo culture)
    {
        if (IsListening)
        {
            return;
        }

        try
        {
            var modelPath = ResolveModelPath(culture);
            if (modelPath is null)
            {
                throw new InvalidOperationException(
                    $"Kein Vosk-Sprachmodell für '{culture.Name}' gefunden. Erwartet unter: " +
                    $"{GetModelRoot(culture)}. Siehe README für Download-Anweisungen.");
            }

            var (recorderCommand, recorderArgs) = ResolveRecorder();

            _model = new Model(modelPath);
            _recognizer = new VoskRecognizer(_model, SampleRateHz);
            _recognizer.SetWords(true);

            var startInfo = new ProcessStartInfo(recorderCommand)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (var argument in recorderArgs)
            {
                startInfo.ArgumentList.Add(argument);
            }

            _recorderProcess = new Process { StartInfo = startInfo };
            _recorderProcess.Start();

            _stopCts = new CancellationTokenSource();
            IsListening = true;

            _captureThread = new Thread(() => CaptureLoop(_recorderProcess, _recognizer, _stopCts.Token))
            {
                IsBackground = true,
                Name = "OpenRoadTyper.VoskCapture",
            };
            _captureThread.Start();
        }
        catch (Win32Exception ex)
        {
            CleanUp();
            Faulted?.Invoke(this, new InvalidOperationException(
                "Kein Audio-Aufnahmewerkzeug gefunden. Installation: 'sudo apt install pipewire-bin' oder 'sudo apt install alsa-utils'.",
                ex));
        }
        catch (Exception ex)
        {
            CleanUp();
            Faulted?.Invoke(this, ex);
        }
    }

    public void Stop()
    {
        if (!IsListening)
        {
            return;
        }

        IsListening = false;
        _stopCts?.Cancel();

        try
        {
            if (_recorderProcess is { HasExited: false })
            {
                _recorderProcess.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort.
        }

        _captureThread?.Join(TimeSpan.FromSeconds(2));
        CleanUp();
    }

    public void Dispose()
    {
        Stop();
    }

    private void CaptureLoop(Process recorderProcess, VoskRecognizer recognizer, CancellationToken cancellationToken)
    {
        var stream = recorderProcess.StandardOutput.BaseStream;
        var buffer = new byte[8000]; // ~0.25s of 16kHz mono 16-bit PCM.

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var bytesRead = stream.Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0)
                {
                    break;
                }

                AudioLevelUpdated?.Invoke(this, new SpeechAudioLevelEventArgs(ComputeLevel(buffer, bytesRead)));

                if (recognizer.AcceptWaveform(buffer, bytesRead))
                {
                    EmitResult(recognizer.Result());
                }
            }

            if (!cancellationToken.IsCancellationRequested)
            {
                EmitResult(recognizer.FinalResult());
            }
        }
        catch (ObjectDisposedException)
        {
            // Stream/process was torn down concurrently by Stop(); nothing to report.
        }
        catch (IOException)
        {
            // Recorder process was killed; nothing to report.
        }
        catch (Exception ex)
        {
            Faulted?.Invoke(this, ex);
        }
    }

    private void EmitResult(string resultJson)
    {
        try
        {
            using var document = JsonDocument.Parse(resultJson);
            if (!document.RootElement.TryGetProperty("text", out var textElement))
            {
                return;
            }

            var text = textElement.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var confidence = ComputeAverageConfidence(document.RootElement);
            if (confidence < RecognitionConfidenceThreshold)
            {
                return;
            }

            Recognized?.Invoke(this, new SpeechRecognizedEventArgs(text, confidence));
        }
        catch (JsonException)
        {
            // Malformed/empty result payload - ignore this segment.
        }
    }

    private static float ComputeAverageConfidence(JsonElement root)
    {
        if (!root.TryGetProperty("result", out var words) || words.ValueKind != JsonValueKind.Array)
        {
            return 1.0f;
        }

        double sum = 0;
        var count = 0;
        foreach (var word in words.EnumerateArray())
        {
            if (word.TryGetProperty("conf", out var confElement) && confElement.TryGetDouble(out var conf))
            {
                sum += conf;
                count++;
            }
        }

        return count > 0 ? (float)(sum / count) : 1.0f;
    }

    private static int ComputeLevel(byte[] buffer, int length)
    {
        long sumOfSquares = 0;
        var sampleCount = length / 2;
        if (sampleCount == 0)
        {
            return 0;
        }

        for (var i = 0; i + 1 < length; i += 2)
        {
            var sample = (short)(buffer[i] | (buffer[i + 1] << 8));
            sumOfSquares += (long)sample * sample;
        }

        var rms = Math.Sqrt(sumOfSquares / (double)sampleCount);
        var normalized = rms / short.MaxValue * 100d * 4d; // Empirical gain so quiet speech is still visible.
        return (int)Math.Clamp(normalized, 0, 100);
    }

    private void CleanUp()
    {
        IsListening = false;

        try
        {
            _recorderProcess?.Dispose();
        }
        catch
        {
            // Ignore.
        }

        _recorderProcess = null;
        _captureThread = null;
        _stopCts?.Dispose();
        _stopCts = null;
        _recognizer = null;
        _model = null;
    }

    private static (string Command, string[] Arguments) ResolveRecorder()
    {
        if (IsToolAvailable("pw-record"))
        {
            return ("pw-record", new[] { "-a", "--format=s16", $"--rate={SampleRateHz}", "--channels=1", "-" });
        }

        if (IsToolAvailable("parecord"))
        {
            return ("parecord", new[] { "--raw", "--format=s16le", $"--rate={SampleRateHz}", "--channels=1" });
        }

        if (IsToolAvailable("arecord"))
        {
            return ("arecord", new[] { "-q", "-f", "S16_LE", "-r", SampleRateHz.ToString(), "-c", "1", "-t", "raw", "-" });
        }

        throw new InvalidOperationException(
            "Kein Audio-Aufnahmewerkzeug gefunden (pw-record, parecord oder arecord). " +
            "Installation z.B. mit 'sudo apt install alsa-utils'.");
    }

    private static bool IsToolAvailable(string toolName)
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo(toolName, "--help")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            });

            if (process is null)
            {
                return false;
            }

            process.WaitForExit(2000);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetModelRoot(CultureInfo culture)
    {
        var languageFolder = culture.TwoLetterISOLanguageName.Equals("de", StringComparison.OrdinalIgnoreCase)
            ? "de"
            : "en";

        var overrideVariable = $"OPENROADTYPER_VOSK_MODEL_{languageFolder.ToUpperInvariant()}";
        var overridePath = Environment.GetEnvironmentVariable(overrideVariable);
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenRoadTyper", "speech-models", languageFolder);
    }

    private static string? ResolveModelPath(CultureInfo culture)
    {
        var root = GetModelRoot(culture);
        return Directory.Exists(root) && Directory.EnumerateFileSystemEntries(root).GetEnumerator().MoveNext()
            ? root
            : null;
    }
}
