#nullable enable

using Avalonia.Controls;
using Avalonia.Interactivity;
using OpenRoadTyper.Core.Typing;

namespace OpenRoadTyper.Avalonia.Views;

public partial class TypingSpeedDialog : Window
{
    public readonly record struct Result(decimal DelayMilliseconds, bool UseSeconds);

    private bool _useSeconds;

    // Parameterless constructor required by the Avalonia XAML loader/previewer.
    public TypingSpeedDialog()
        : this(0m, true)
    {
    }

    public TypingSpeedDialog(decimal currentDelayMilliseconds, bool useSeconds)
    {
        InitializeComponent();

        _useSeconds = useSeconds;
        ConfigureInput(TypingSpeedFormatter.GetDisplayValue(currentDelayMilliseconds, useSeconds));
        RefreshSegmentButtons();
    }

    private void ConfigureInput(decimal value)
    {
        ValueInput.Minimum = 0m;
        ValueInput.FormatString = _useSeconds ? "0.###" : "0.##";
        ValueInput.Increment = _useSeconds ? 0.05m : 0.10m;
        ValueInput.Maximum = _useSeconds ? 60m : 60000m;
        ValueInput.Value = System.Math.Clamp(value, ValueInput.Minimum, ValueInput.Maximum);
    }

    private void RefreshSegmentButtons()
    {
        SecondsButton.IsChecked = _useSeconds;
        MillisecondsButton.IsChecked = !_useSeconds;
    }

    private void SecondsButton_Click(object? sender, RoutedEventArgs e) => SelectUnit(useSeconds: true);

    private void MillisecondsButton_Click(object? sender, RoutedEventArgs e) => SelectUnit(useSeconds: false);

    private void SelectUnit(bool useSeconds)
    {
        if (useSeconds == _useSeconds)
        {
            RefreshSegmentButtons();
            return;
        }

        var currentValue = ValueInput.Value ?? 0m;
        var milliseconds = TypingSpeedFormatter.ConvertToMilliseconds(currentValue, _useSeconds);
        _useSeconds = useSeconds;
        ConfigureInput(TypingSpeedFormatter.GetDisplayValue(milliseconds, _useSeconds));
        RefreshSegmentButtons();
    }

    private void ApplyButton_Click(object? sender, RoutedEventArgs e)
    {
        var value = ValueInput.Value ?? 0m;
        var milliseconds = TypingSpeedFormatter.ConvertToMilliseconds(value, _useSeconds);
        Close(new Result(milliseconds, _useSeconds));
    }

    private void CancelDialogButton_Click(object? sender, RoutedEventArgs e) => Close(null);
}
