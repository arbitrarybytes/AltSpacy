using System.Windows;
using System.Windows.Navigation;

namespace AltSpacy;

public partial class AboutWindow : Window
{
    public AboutWindow(string hotkey)
    {
        InitializeComponent();
        MaxHeight = SystemParameters.WorkArea.Height - 40;
        Icon = Logo.Image;
        LogoImage.Source = Logo.Image;
        VersionText.Text = $"Version {AppInfo.Version}";
        HotkeyText.Text = $"Press {hotkey} anywhere to open it. Right-click the tray icon for Settings.";
        HotkeyKeys.ItemsSource = hotkey.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        Notes.ItemsSource = ReleaseNotes.All;
        CopyrightText.Text = AppInfo.Copyright;
        LicenseRun.Text = AppInfo.License;
        LicenseLink.NavigateUri = new Uri(AppInfo.LicenseUrl);
    }

    private void OnOk(object sender, RoutedEventArgs e) => Close();

    private void OnWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Escape) return;
        e.Handled = true;
        Close();
    }

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        AppInfo.OpenUri(e.Uri.AbsoluteUri);
        e.Handled = true;
    }
}
