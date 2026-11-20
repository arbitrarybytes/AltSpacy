using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace AltSpacy;

public partial class App : Application
{
    private readonly string _configPath = ConfigStore.DefaultPath;
    private readonly Dictionary<Type, Window> _openWindows = [];
    private readonly IconService _icons = new(IconService.DefaultCacheDir);
    private AltSpacyConfig _config = new();
    private EventWaitHandle? _showSignal;
    private MainWindow? _bar;
    private GlobalHotkey? _hotkey;
    private WinForms.NotifyIcon? _tray;
    private FileSystemWatcher? _watcher;
    private DispatcherTimer? _reloadTimer;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args is ["--export-icon", var icoPath])
        {
            using (var file = File.Create(icoPath)) Logo.WriteIco(file, Logo.IcoSizes);
            Shutdown();
            return;
        }

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\AltSpacy.Show", out var isFirstInstance);
        if (!isFirstInstance)
        {
            _showSignal.Set();
            Shutdown();
            return;
        }
        ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) => Dispatcher.Invoke(() => _bar?.ShowBar()), null, -1, false);

        DispatcherUnhandledException += (_, args) =>
        {
            Log.Write("Unhandled UI error", args.Exception);
            Notify($"Something went wrong: {args.Exception.Message}. Details are in {AppPaths.Log}.", error: true);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Log.Write("Unhandled error", args.ExceptionObject as Exception);

        // Before 1.5 the app was called Omnibar: bring over its data folder and its Start with Windows entry.
        var renamed = AppPaths.Root == AppPaths.DefaultRoot && AppPaths.MigrateFrom(AppPaths.LegacyRoot, AppPaths.Root);
        var startupMoved = Environment.ProcessPath is { } exe && new StartupRegistration().TakeOverFrom("Omnibar", exe);

        // Versions before 1.4 kept shortcuts.json next to the exe.
        // Use the exe's folder, not AppContext.BaseDirectory: a single-file build runs from a temp extraction folder.
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
        var migrated = AppPaths.Migrate(Path.Combine(exeDir, "shortcuts.json"), _configPath);
        var firstRun = !File.Exists(_configPath);
        Log.Write($"AltSpacy {AppInfo.Version} starting from {Environment.ProcessPath}{(migrated ? " (config migrated to " + _configPath + ")" : "")}");
        if (renamed) Log.Write($"Copied settings from {AppPaths.LegacyRoot} (Omnibar)");
        if (startupMoved) Log.Write("Start with Windows moved from the Omnibar entry");

        var actions = SystemActions.Create(RunCommand);
        WarmIcons(actions);
        _bar = new MainWindow(() => _config, _icons, actions, RunCommand, new BarServices
        {
            History = new History(AppPaths.History),
            ReadClipboard = ReadClipboard,
        });
        new WindowInteropHelper(_bar).EnsureHandle();
        _tray = CreateTray();
        Reload(announce: false);
        WatchConfig();

        if (firstRun) Notify($"Press {_config.Hotkey} to open.");
        if (!e.Args.Contains(StartupRegistration.BackgroundArg)) _bar.ShowBar();
    }

    private static string? ReadClipboard()
    {
        try
        {
            return Clipboard.ContainsText() ? Clipboard.GetText() : null;
        }
        catch (System.Runtime.InteropServices.COMException)
        {
            return null; // another app is holding the clipboard
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotkey?.Dispose();
        _watcher?.Dispose();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }
        base.OnExit(e);
    }

    private void Reload(bool announce)
    {
        AltSpacyConfig config;
        try
        {
            config = ConfigStore.Load(_configPath);
        }
        catch (Exception ex)
        {
            Log.Write($"Config not loaded: {_configPath}", ex);
            Notify($"shortcuts.json not loaded: {ex.Message}", error: true);
            return;
        }

        var hotkeyChanged = _hotkey is null || !string.Equals(config.Hotkey, _config.Hotkey, StringComparison.OrdinalIgnoreCase);
        _config = config;
        foreach (var shortcut in config.Shortcuts) _icons.For(shortcut);
        if (hotkeyChanged) RegisterHotkey();
        if (announce) Notify($"Loaded {config.Shortcuts.Count} shortcuts.");
    }

    private void RegisterHotkey()
    {
        _hotkey?.Dispose();
        _hotkey = null;
        try
        {
            _hotkey = new GlobalHotkey(new WindowInteropHelper(_bar!).Handle, HotkeyBinding.Parse(_config.Hotkey));
            _hotkey.Pressed += () => _bar!.Toggle();
        }
        catch (Exception ex)
        {
            Log.Write($"Hotkey {_config.Hotkey} not registered", ex);
            Notify($"Hotkey {_config.Hotkey} is taken ({ex.Message}). Pick another in Settings.", error: true);
        }
    }

    private void SuspendHotkey(bool suspend)
    {
        if (suspend)
        {
            _hotkey?.Dispose();
            _hotkey = null;
        }
        else if (_hotkey is null)
        {
            RegisterHotkey();
        }
    }

    private void ShowSingle<T>(Func<T> create) where T : Window
    {
        if (!_openWindows.TryGetValue(typeof(T), out var window))
        {
            window = create();
            _openWindows[typeof(T)] = window;
            window.Closed += (_, _) => _openWindows.Remove(typeof(T));
            window.Show();
        }
        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
    }

    /// <summary>
    /// Loading ~100 Explorer icons takes close to a second, so do it on a background STA thread at startup
    /// instead of on the first keystrokes. Images are frozen and cached, so the UI thread can use them.
    /// </summary>
    private static void WarmIcons(IReadOnlyList<LauncherAction> actions)
    {
        var thread = new Thread(() =>
        {
            foreach (var action in actions) action.Icon();
        }) { IsBackground = true, Priority = ThreadPriority.BelowNormal, Name = "AltSpacy icon warm-up" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
    }

    private void WatchConfig()
    {
        _reloadTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        // Saves (including from Settings) reload silently; only errors raise a notification.
        _reloadTimer.Tick += (_, _) =>
        {
            _reloadTimer.Stop();
            Reload(announce: false);
        };

        _watcher = new FileSystemWatcher(Path.GetDirectoryName(_configPath)!, Path.GetFileName(_configPath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
        };
        FileSystemEventHandler onChange = (_, _) => Dispatcher.BeginInvoke(() =>
        {
            _reloadTimer.Stop();
            _reloadTimer.Start();
        });
        _watcher.Changed += onChange;
        _watcher.Created += onChange;
        _watcher.Renamed += (s, e) => onChange(s, e);
        _watcher.EnableRaisingEvents = true;
    }

    private WinForms.NotifyIcon CreateTray()
    {
        var menu = new WinForms.ContextMenuStrip();
        var open = menu.Items.Add("Open", null, (_, _) => _bar!.ShowBar());
        open.Font = new Drawing.Font(open.Font, Drawing.FontStyle.Bold);
        menu.Items.Add("Settings…", null, (_, _) => RunCommand("settings"));
        menu.Items.Add("About AltSpacy", null, (_, _) => RunCommand("about"));
        menu.Items.Add("-");
        menu.Items.Add("Edit config file", null, (_, _) => RunCommand("edit"));
        menu.Items.Add("Reload config", null, (_, _) => RunCommand("reload"));
        menu.Items.Add("-");
        menu.Items.Add("Exit", null, (_, _) => RunCommand("exit"));

        var tray = new WinForms.NotifyIcon
        {
            Icon = Logo.CreateTrayIcon(),
            Text = AppInfo.DisplayName,
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.MouseClick += (_, e) =>
        {
            if (e.Button == WinForms.MouseButtons.Left) _bar!.ShowBar();
        };
        return tray;
    }

    private void RunCommand(string command)
    {
        switch (command)
        {
            case "settings":
                ShowSingle(() => new SettingsWindow(_config, _configPath, SuspendHotkey, _icons));
                break;
            case "about":
                ShowSingle(() => new AboutWindow(_config.Hotkey));
                break;
            case "edit":
                Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_configPath}\"") { UseShellExecute = true });
                break;
            case "reload":
                Reload(announce: true);
                break;
            case "exit" or "quit":
                Shutdown();
                break;
            default:
                Notify($"Unknown command :{command}. Try :settings, :about, :edit, :reload or :exit.", error: true);
                break;
        }
    }

    private void Notify(string message, bool error = false) =>
        _tray?.ShowBalloonTip(4000, AppInfo.Name, message, error ? WinForms.ToolTipIcon.Error : WinForms.ToolTipIcon.Info);
}
