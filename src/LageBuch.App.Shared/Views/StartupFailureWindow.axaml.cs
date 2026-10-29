using Avalonia.Controls;
using Avalonia.Interactivity;

namespace LageBuch.App.Shared.Views;

/// <summary>
/// What a start that threw before the main window existed shows instead of it. Unlike a crash
/// report, this one is only ever on screen, so it carries the exception's message too: that is
/// usually the one line that says what broke ("no such column: postal_code").
/// </summary>
public partial class StartupFailureWindow : Window
{
    /// <summary>The report line when no report could be written.</summary>
    public const string NoReportWritten = "Ein Fehlerbericht konnte nicht gespeichert werden.";

    public StartupFailureWindow()
    {
        InitializeComponent();
        WindowsTitleBar.ApplyDarkMode(this);
    }

    /// <param name="failure">What the start threw.</param>
    /// <param name="reportFolder">Where its crash report went, or <see langword="null"/> when none was written.</param>
    public StartupFailureWindow(Exception failure, string? reportFolder)
        : this()
    {
        ArgumentNullException.ThrowIfNull(failure);
        FailureDetails.Text = failure.GetType().FullName + ": " + failure.Message;
        ReportLocation.Text = reportFolder is null
            ? NoReportWritten
            : $"Ein Fehlerbericht ohne Einsatzdaten liegt im Ordner {reportFolder}.";
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
