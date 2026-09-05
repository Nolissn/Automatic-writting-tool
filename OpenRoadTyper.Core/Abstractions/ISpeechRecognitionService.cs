#nullable enable

using System;
using System.Globalization;

namespace OpenRoadTyper.Core.Abstractions;

public sealed class SpeechRecognizedEventArgs : EventArgs
{
    public SpeechRecognizedEventArgs(string text, float confidence)
    {
        Text = text;
        Confidence = confidence;
    }

    public string Text { get; }

    public float Confidence { get; }
}

public sealed class SpeechAudioLevelEventArgs : EventArgs
{
    public SpeechAudioLevelEventArgs(int level)
    {
        Level = level;
    }

    /// <summary>Normalized microphone level, 0-100.</summary>
    public int Level { get; }
}

/// <summary>
/// Dictation-style speech recognition abstraction. Windows uses the built-in
/// System.Speech/SAPI engine (see the WinForms app's WindowsSpeechRecognitionService);
/// Linux (and any other platform) uses the offline Vosk engine
/// (see PlatformLinux/VoskSpeechRecognitionService.cs). Both feed recognized
/// text and microphone level updates through the same events so the UI layer
/// never needs to know which engine is behind it.
/// </summary>
public interface ISpeechRecognitionService : IDisposable
{
    bool IsListening { get; }

    /// <summary>Raised whenever a phrase has been recognized with usable confidence.</summary>
    event EventHandler<SpeechRecognizedEventArgs>? Recognized;

    /// <summary>Raised frequently while listening so the UI can animate a microphone level meter.</summary>
    event EventHandler<SpeechAudioLevelEventArgs>? AudioLevelUpdated;

    /// <summary>Raised when recognition could not start or failed while running.</summary>
    event EventHandler<Exception>? Faulted;

    void Start(CultureInfo culture);

    void Stop();
}
