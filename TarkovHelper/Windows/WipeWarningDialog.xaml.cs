using System.Diagnostics;
using System.Windows;
using TarkovHelper.Services;

namespace TarkovHelper.Windows;

/// <summary>
/// Wipe warning dialog window.
/// Warns users about potential issues when syncing after an account wipe.
/// </summary>
public partial class WipeWarningDialog : Window
{
    private readonly LocalizationService _loc = LocalizationService.Instance;
    private readonly string _logPath;

    /// <summary>
    /// Gets whether the user chose to continue with the sync.
    /// </summary>
    public bool ShouldContinue { get; private set; }

    /// <summary>
    /// Gets whether the user chose to hide this warning in the future.
    /// </summary>
    public bool DontShowAgain { get; private set; }

    public WipeWarningDialog(string logPath)
    {
        InitializeComponent();
        _logPath = logPath;
        TxtLogPath.Text = logPath;
        UpdateLocalizedText();
    }

    /// <summary>
    /// Show the wipe warning dialog and return whether to continue.
    /// </summary>
    /// <param name="logPath">The log folder path to display.</param>
    /// <param name="owner">Optional owner window for centering.</param>
    /// <returns>True if the user chose to continue, false otherwise.</returns>
    public static bool ShowWarning(string logPath, Window? owner = null)
    {
        var dialog = new WipeWarningDialog(logPath);
        if (owner != null)
        {
            dialog.Owner = owner;
        }
        dialog.ShowDialog();

        // Save preference if user chose to hide warning
        if (dialog.DontShowAgain)
        {
            SettingsService.Instance.HideWipeWarning = true;
        }

        return dialog.ShouldContinue;
    }

    /// <summary>
    /// Update localized text based on current language.
    /// </summary>
    private void UpdateLocalizedText()
    {
        TxtTitle.Text = "Check Before Quest Sync";
        TxtMessage.Text = "Did you recently reset your account (wipe)?";
        TxtDescription.Text = "Syncing after a wipe may mix previous season logs and show incorrect quest progress.";
        TxtLogFolderLabel.Text = "📁 Log Folder Path:";
        TxtRecommendation.Text = "💡 Recommendation: Delete or back up log folders from before the wipe date.";
        ChkDontShowAgain.Content = "Do not show this notice again";
        BtnOpenFolder.Content = "Open Folder";
        BtnContinue.Content = "Continue";
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        ShouldContinue = false;
        DontShowAgain = ChkDontShowAgain.IsChecked == true;
        Close();
    }

    private void BtnOpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_logPath))
        {
            return;
        }

        try
        {
            Process.Start("explorer.exe", _logPath);
        }
        catch (Exception)
        {
            // Copy path to clipboard if can't open
            try
            {
                Clipboard.SetText(_logPath);
                MessageBox.Show(
                    "Could not open folder. Path copied to clipboard.",
                    "Notice",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch
            {
                // Ignore clipboard errors
            }
        }
    }

    private void BtnContinue_Click(object sender, RoutedEventArgs e)
    {
        ShouldContinue = true;
        DontShowAgain = ChkDontShowAgain.IsChecked == true;
        Close();
    }
}
