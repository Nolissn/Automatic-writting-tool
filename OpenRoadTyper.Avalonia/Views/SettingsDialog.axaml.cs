#nullable enable

using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenRoadTyper.Core.Persistence;
using OpenRoadTyper.Core.Typing;

namespace OpenRoadTyper.Avalonia.Views;

public partial class SettingsDialog : Window
{
    private bool _useSeconds;
    private string _micLanguage = "de";

    // Parameterless constructor required by the Avalonia XAML loader/previewer.
    public SettingsDialog()
        : this(new AppSettings())
    {
    }

    public SettingsDialog(AppSettings settings)
    {
        InitializeComponent();
        LoadIntoFields(settings);
    }

    private void LoadIntoFields(AppSettings settings)
    {
        StartDelayInput.Value = TypingSpeedFormatter.Clamp(settings.StartDelaySeconds, 1, 60);
        _useSeconds = settings.TypingSpeedUsesSeconds;
        ConfigureTypingDelayInput(TypingSpeedFormatter.GetDisplayValue(settings.TypingDelayMilliseconds, _useSeconds));
        _micLanguage = settings.MicLanguage == "en" ? "en" : "de";
        MinimizeCheckBox.IsChecked = settings.MinimizeOnStart;
        UseEnterKeyCheckBox.IsChecked = settings.UseEnterKey;
        RefreshSegmentButtons();
    }

    private void ConfigureTypingDelayInput(decimal value)
    {
        TypingDelayInput.Minimum = 0m;
        TypingDelayInput.FormatString = _useSeconds ? "0.###" : "0.##";
        TypingDelayInput.Increment = _useSeconds ? 0.05m : 10m;
        TypingDelayInput.Maximum = _useSeconds ? 60m : 60000m;
        TypingDelayInput.Value = System.Math.Clamp(value, TypingDelayInput.Minimum, TypingDelayInput.Maximum);
    }

    private void RefreshSegmentButtons()
    {
        SecondsButton.IsChecked = _useSeconds;
        MillisecondsButton.IsChecked = !_useSeconds;
        GermanButton.IsChecked = _micLanguage == "de";
        EnglishButton.IsChecked = _micLanguage == "en";
    }

    private void SecondsButton_Click(object? sender, RoutedEventArgs e) => SelectUnit(useSeconds: true);

    private void MillisecondsButton_Click(object? sender, RoutedEventArgs e) => SelectUnit(useSeconds: false);

    private void SelectUnit(bool useSeconds)
    {
        if (useSeconds != _useSeconds)
        {
            var milliseconds = TypingSpeedFormatter.ConvertToMilliseconds(TypingDelayInput.Value ?? 0m, _useSeconds);
            _useSeconds = useSeconds;
            ConfigureTypingDelayInput(TypingSpeedFormatter.GetDisplayValue(milliseconds, _useSeconds));
        }

        RefreshSegmentButtons();
    }

    private void GermanButton_Click(object? sender, RoutedEventArgs e) => SelectLanguage("de");

    private void EnglishButton_Click(object? sender, RoutedEventArgs e) => SelectLanguage("en");

    private void SelectLanguage(string language)
    {
        _micLanguage = language;
        RefreshSegmentButtons();
    }

    private void SaveButton_Click(object? sender, RoutedEventArgs e)
    {
        var settings = new AppSettings
        {
            StartDelaySeconds = TypingSpeedFormatter.Clamp((int)(StartDelayInput.Value ?? 3m), 1, 60),
            TypingDelayMilliseconds = TypingSpeedFormatter.ConvertToMilliseconds(TypingDelayInput.Value ?? 0m, _useSeconds),
            TypingSpeedUsesSeconds = _useSeconds,
            MinimizeOnStart = MinimizeCheckBox.IsChecked == true,
            UseEnterKey = UseEnterKeyCheckBox.IsChecked == true,
            MicLanguage = _micLanguage,
        };
        Close(settings);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void ResetButton_Click(object? sender, RoutedEventArgs e) => LoadIntoFields(new AppSettings());
}
