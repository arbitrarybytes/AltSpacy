using System.Collections;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace AltSpacy;

public partial class SettingsWindow : Window
{
    private readonly string _path;
    private readonly Action<bool> _suspendHotkey;
    private readonly ObservableCollection<Shortcut> _shortcuts;
    private readonly ListCollectionView _view;
    private readonly Dictionary<string, string> _projects;
    private readonly StartupRegistration _startup;
    private readonly string _exePath;
    private string _hotkey;
    private bool _recording;

    public SettingsWindow(AltSpacyConfig config, string path, Action<bool> suspendHotkey, IconService? icons = null,
        IReadOnlyList<BrowserInfo>? browsers = null, string? defaultBrowserName = null,
        StartupRegistration? startup = null, string? exePath = null)
    {
        _projects = new Dictionary<string, string>(config.Projects, StringComparer.OrdinalIgnoreCase);
        _startup = startup ?? new StartupRegistration();
        _exePath = exePath ?? Environment.ProcessPath ?? "AltSpacy.exe";
        InitializeComponent();
        var area = SystemParameters.WorkArea;
        Width = Math.Min(Width, area.Width - 40);
        Height = Math.Min(Height, area.Height - 40);
        _path = path;
        _suspendHotkey = suspendHotkey;
        _shortcuts = new(config.Shortcuts.Select(s => s.Clone()));
        _hotkey = config.Hotkey;
        ((ShortcutIconConverter)Resources["IconOf"]).Icons = icons ?? new IconService(IconService.DefaultCacheDir);

        Icon = Logo.Image;
        LogoImage.Source = Logo.Image;
        ApplyTheme();
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
        Closed += (_, _) => Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        VersionText.Text = $"Version {AppInfo.Version}";
        ConfigPathRun.Text = path;
        ShowHotkey();
        LoadBrowsers(config.Browser, browsers ?? Browsers.Installed(), defaultBrowserName ?? Browsers.DefaultName());

        _view = new ListCollectionView(_shortcuts);
        _view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(Shortcut.App)));
        ShortcutGrid.ItemsSource = _view;
        Closed += (_, _) => _suspendHotkey(false);
        StartWithWindowsSwitch.IsChecked = _startup.IsEnabled;
        TestBox.Text = _shortcuts.FirstOrDefault(s => s.UsesValue) is { } sample ? $"{sample.Key} 41" : "";
    }

    internal string Hotkey => _hotkey;

    /// <summary>Dark icons get a light plate only in dark mode; re-evaluated when Windows switches theme.</summary>
    private void ApplyTheme() =>
        Resources["Brand.IconPlate"] = new System.Windows.Media.SolidColorBrush(
            WindowsTheme.IsLight ? System.Windows.Media.Colors.Transparent : System.Windows.Media.Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF));

    private void OnUserPreferenceChanged(object? sender, Microsoft.Win32.UserPreferenceChangedEventArgs e) =>
        Dispatcher.BeginInvoke(ApplyTheme);

    internal string Browser =>
        DefaultBrowserSwitch.IsChecked == true || BrowserList.SelectedItem is not BrowserInfo browser ? "" : browser.Path;

    private void LoadBrowsers(string? configured, IReadOnlyList<BrowserInfo> installed, string? defaultName)
    {
        var list = installed.ToList();
        var match = Browsers.Match(configured, list);
        if (match is null && !string.IsNullOrWhiteSpace(configured))
        {
            match = new BrowserInfo(Path.GetFileNameWithoutExtension(configured.Trim('"')), configured.Trim('"'));
            list.Add(match);
        }
        BrowserList.ItemsSource = list;
        BrowserList.SelectedItem = match ?? list.FirstOrDefault();
        DefaultBrowserName.Text = defaultName is null ? "Whatever Windows uses for links" : $"Currently {defaultName}";
        DefaultBrowserSwitch.IsChecked = match is null;
        BrowserList.IsEnabled = match is not null;
    }

    private void OnDefaultBrowserChanged(object sender, RoutedEventArgs e)
    {
        if (BrowserList is null) return;
        BrowserList.IsEnabled = DefaultBrowserSwitch.IsChecked != true;
        if (BrowserList.IsEnabled && BrowserList.SelectedItem is null && BrowserList.Items.Count > 0) BrowserList.SelectedIndex = 0;
    }

    private void ShowHotkey()
    {
        HotkeyKeys.ItemsSource = _hotkey.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        HotkeyKeys.Visibility = _recording ? Visibility.Collapsed : Visibility.Visible;
        HotkeyPrompt.Visibility = _recording ? Visibility.Visible : Visibility.Collapsed;
        HotkeyButton.Tag = _recording ? "Recording" : null;
        HotkeyHint.Text = _recording ? "Press the keys together. Esc cancels." : "Click to change.";
    }

    private void SetRecording(bool recording)
    {
        if (_recording == recording) return;
        _recording = recording;
        _suspendHotkey(recording);
        ShowHotkey();
    }

    /// <summary>Applies a captured key combination; ignores modifier-only or modifier-less presses.</summary>
    internal void CaptureHotkey(ModifierKeys modifiers, Key key)
    {
        if (HotkeyBinding.Format(modifiers, key) is not { } text) return;
        _hotkey = text;
        SetRecording(false);
    }

    private void OnHotkeyClick(object sender, RoutedEventArgs e) => SetRecording(!_recording);

    private void OnHotkeyKeyDown(object sender, KeyEventArgs e)
    {
        if (!_recording) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape) SetRecording(false);
        else CaptureHotkey(Keyboard.Modifiers, key);
    }

    private void OnHotkeyBlur(object sender, KeyboardFocusChangedEventArgs e) => SetRecording(false);

    /// <summary>Re-groups after an edit so a shortcut moves under its new app when its URL changes.</summary>
    private void OnRowEditEnding(object? sender, DataGridRowEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(() =>
        {
            if (!_view.IsAddingNew && !_view.IsEditingItem) _view.Refresh();
        }, System.Windows.Threading.DispatcherPriority.Background);

    private AltSpacyConfig BuildConfig() => new()
    {
        Hotkey = _hotkey,
        Browser = Browser,
        Shortcuts = _shortcuts
            .Where(s => !string.IsNullOrWhiteSpace(s.Key) || !string.IsNullOrWhiteSpace(s.Url))
            .Select(s => new Shortcut
            {
                Key = s.Key.Trim(),
                Url = s.Url.Trim(),
                Description = NullIfBlank(s.Description),
                PrefixIfNumeric = NullIfBlank(s.PrefixIfNumeric),
                DefaultProject = NullIfBlank(s.DefaultProject),
                Icon = NullIfBlank(s.Icon),
            })
            .ToList(),
        Projects = new Dictionary<string, string>(_projects, StringComparer.OrdinalIgnoreCase),
    };

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private void UpdateTest()
    {
        var resolution = ShortcutResolver.Resolve(TestBox.Text, BuildConfig().Shortcuts, _projects);
        TestResult.Text = resolution?.Url ?? "No match. Type a key, a space, then the value: mr 1190";
        OpenTestButton.IsEnabled = resolution is not null;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        ShortcutGrid.CommitEdit(DataGridEditingUnit.Row, true);
        try
        {
            ConfigStore.Save(_path, BuildConfig());
            var startWithWindows = StartWithWindowsSwitch.IsChecked == true;
            // Re-register when enabled so the entry follows the exe if it was moved.
            if (startWithWindows || _startup.IsEnabled) _startup.Set(startWithWindows, _exePath);
            Close();
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            ErrorText.Text = ex.Message;
        }
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var shortcut = new Shortcut();
        _shortcuts.Add(shortcut);
        ShortcutGrid.SelectedItem = shortcut;
        ShortcutGrid.ScrollIntoView(shortcut);
        ShortcutGrid.CurrentCell = new DataGridCellInfo(shortcut, ShortcutGrid.Columns[0]);
        ShortcutGrid.Focus();
        ShortcutGrid.BeginEdit();
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        ShortcutGrid.CancelEdit();
        foreach (var shortcut in ShortcutGrid.SelectedItems.OfType<Shortcut>().ToList()) _shortcuts.Remove(shortcut);
        UpdateTest();
    }

    private void OnOpenTest(object sender, RoutedEventArgs e)
    {
        if (ShortcutResolver.Resolve(TestBox.Text, BuildConfig().Shortcuts, _projects) is not { } resolution) return;
        try
        {
            BrowserLauncher.Open(resolution.Url, Browser);
        }
        catch (Exception ex)
        {
            ErrorText.Text = "Could not open: " + ex.Message;
        }
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Escape) return;
        e.Handled = true;
        Close();
    }

    private void OnTestKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        OnOpenTest(sender, e);
    }

    private void OnTestChanged(object sender, TextChangedEventArgs e) => UpdateTest();

    private void OnGridChanged(object? sender, EventArgs e) => UpdateTest();

    private void OnCellEditEnding(object? sender, DataGridCellEditEndingEventArgs e) =>
        Dispatcher.BeginInvoke(UpdateTest);
}

/// <summary>Icon for a shortcut, or for the first shortcut of a DataGrid group (the app's icon).</summary>
public sealed class ShortcutIconConverter : IValueConverter
{
    public IconService? Icons { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var shortcut = value as Shortcut ?? (value as IEnumerable)?.OfType<Shortcut>().FirstOrDefault();
        return shortcut is null || Icons is null ? null : Icons.For(shortcut);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
