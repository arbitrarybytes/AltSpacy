using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace AltSpacy;

/// <summary>Everything the bar does to the outside world, so tests can swap it out.</summary>
public sealed class BarServices
{
    public Action<string, string?> OpenUrl { get; init; } = BrowserLauncher.Open;
    public CommandResolver Resolver { get; init; } = CommandResolver.System;
    public History History { get; init; } = new(null);
    public Func<string?> ReadClipboard { get; init; } = () => null;
    public Action<string> CopyText { get; init; } = text => Clipboard.SetText(text);
    /// <summary>Starts a URI with its registered handler: mailto: (mail app), msteams: (Teams).</summary>
    public Action<string> Launch { get; init; } = uri => new RunTarget(uri, "", null).Start();
    public Action<string> SearchTeams { get; init; } = TeamsLauncher.Search;
}

public partial class MainWindow : Window
{
    public const string SuggestedGroup = "Suggested", RecentGroup = "Recent", ClipboardGroup = "From clipboard",
        CalculatorGroup = "Calculator", PeopleGroup = "Teams & mail", WebGroup = "Web";

    private const string TeamsGlyph = "\uE8BD", MailGlyph = "\uE715", WebGlyph = "\uE774", CalculatorGlyph = "\uE8EF";
    private static readonly Color TeamsPurple = Color.FromRgb(0x5B, 0x5F, 0xC7), MailBlue = Color.FromRgb(0x0F, 0x6C, 0xBD),
        WebTeal = Color.FromRgb(0x0F, 0x76, 0x6E), Slate = Color.FromRgb(0x4A, 0x51, 0x60);

    /// <summary>Glyphs used by rows created here (checked against the installed icon font in tests).</summary>
    public static readonly string[] Glyphs = [TeamsGlyph, MailGlyph, WebGlyph, CalculatorGlyph];

    private readonly Func<AltSpacyConfig> _config;
    private readonly IconService _icons;
    private readonly IReadOnlyList<LauncherAction> _actions;
    private readonly Action<string> _runCommand;
    private readonly BarServices _services;
    private readonly IconHandle _logo = new(Logo.Image);
    private readonly Dictionary<ImageSource, IconHandle> _actionIcons = [];
    private readonly RotateTransform _glowSpin = new() { CenterX = 0.5, CenterY = 0.5 };
    private IReadOnlyList<LauncherItem> _items = [];
    private IconHandle? _shownIcon;
    private bool _hasBackdrop;
    private string? _clipboardText;
    private bool _clipboardRead;

    /// <summary>Respects Settings > Accessibility > Visual effects > Animation effects.</summary>
    private static bool Animate => SystemParameters.ClientAreaAnimation;

    public MainWindow(Func<AltSpacyConfig> config, IconService icons, IReadOnlyList<LauncherAction> actions,
        Action<string> runCommand, BarServices? services = null)
    {
        _config = config;
        _icons = icons;
        _actions = actions;
        _runCommand = runCommand;
        _services = services ?? new BarServices();
        InitializeComponent();
        Icon = Logo.Image;
        FooterLogo.Source = Logo.Image;
        var glow = new LinearGradientBrush(
            [new GradientStop(Color.FromRgb(0xFF, 0x6A, 0x3D), 0), new GradientStop(Color.FromRgb(0xE0, 0x39, 0x9E), 0.35),
             new GradientStop(Color.FromRgb(0x8B, 0x5C, 0xF6), 0.7), new GradientStop(Color.FromRgb(0xFF, 0x6A, 0x3D), 1)],
            new Point(0, 0), new Point(1, 1)) { RelativeTransform = _glowSpin };
        Shell.BorderBrush = glow;
        GlowRing.BorderBrush = glow;
        IsVisibleChanged += (_, _) => SpinGlow(IsVisible && Animate);
        ShowIcon(_logo, animate: false);
    }

    internal IReadOnlyList<LauncherItem> Items => _items;

    private History History => _services.History;

    public void ShowBar()
    {
        ApplyPalette(WindowsTheme.IsLight);
        PlaceOnActiveMonitor();
        _clipboardRead = false;
        Refresh();
        Show();
        Activate();
        Input.Focus();
        Keyboard.Focus(Input);
        Input.SelectAll();
        AnimateIn();
    }

    public void Toggle()
    {
        if (IsVisible && IsActive) Hide();
        else ShowBar();
    }

    /// <summary>Centers the bar on the monitor under the mouse, like Spotlight follows your focus.</summary>
    private void PlaceOnActiveMonitor()
    {
        var area = System.Windows.Forms.Screen.FromPoint(System.Windows.Forms.Cursor.Position).WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        double left = area.Left / dpi.DpiScaleX, top = area.Top / dpi.DpiScaleY, width = area.Width / dpi.DpiScaleX, height = area.Height / dpi.DpiScaleY;
        Left = left + (width - Width) / 2;
        Top = top + height * 0.2;
    }

    private void SpinGlow(bool on) =>
        _glowSpin.BeginAnimation(RotateTransform.AngleProperty,
            on ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(9)) { RepeatBehavior = RepeatBehavior.Forever } : null);

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        Backdrop.HideCaptionButtons(this);
        _hasBackdrop = Backdrop.Apply(this, BackdropKind.Acrylic, dark: !WindowsTheme.IsLight);
        ApplyPalette(WindowsTheme.IsLight);
    }

    /// <summary>Light or dark glass to match Windows. Without acrylic the tint is made opaque so text stays readable.</summary>
    private void ApplyPalette(bool light)
    {
        var tintAlpha = _hasBackdrop ? (light ? 0xD9 : 0xC8) : 0xFF;
        var palette = light
            ? new Dictionary<string, Color>
            {
                ["Bar.Tint"] = Color.FromArgb((byte)tintAlpha, 0xFA, 0xFA, 0xFC),
                ["Bar.Text"] = Color.FromRgb(0x11, 0x13, 0x18), ["Bar.SubText"] = Color.FromRgb(0x4B, 0x52, 0x60),
                ["Bar.Faint"] = Color.FromRgb(0x7A, 0x80, 0x8C), ["Bar.Selected"] = Color.FromArgb(0x14, 0, 0, 0),
                ["Bar.Chip"] = Color.FromArgb(0x0A, 0, 0, 0), ["Bar.ChipStroke"] = Color.FromArgb(0x1A, 0, 0, 0),
                ["Bar.Divider"] = Color.FromArgb(0x12, 0, 0, 0), ["Bar.Badge"] = Color.FromRgb(0xB4, 0x53, 0x09),
                ["Bar.Scroll"] = Color.FromArgb(0x38, 0, 0, 0), ["Bar.ScrollHover"] = Color.FromArgb(0x70, 0, 0, 0),
                ["Bar.IconPlate"] = Colors.Transparent,
            }
            : new Dictionary<string, Color>
            {
                ["Bar.Tint"] = Color.FromArgb((byte)tintAlpha, 0x1C, 0x1C, 0x22),
                ["Bar.Text"] = Color.FromRgb(0xF5, 0xF6, 0xF8), ["Bar.SubText"] = Color.FromRgb(0xA1, 0xA7, 0xB3),
                ["Bar.Faint"] = Color.FromRgb(0x7C, 0x82, 0x90), ["Bar.Selected"] = Color.FromArgb(0x1C, 0xFF, 0xFF, 0xFF),
                ["Bar.Chip"] = Color.FromArgb(0x12, 0xFF, 0xFF, 0xFF), ["Bar.ChipStroke"] = Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF),
                ["Bar.Divider"] = Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF), ["Bar.Badge"] = Color.FromRgb(0xFF, 0xD0, 0x8A),
                ["Bar.Scroll"] = Color.FromArgb(0x38, 0xFF, 0xFF, 0xFF), ["Bar.ScrollHover"] = Color.FromArgb(0x80, 0xFF, 0xFF, 0xFF),
                ["Bar.IconPlate"] = Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF),
            };
        foreach (var (key, color) in palette) Resources[key] = new SolidColorBrush(color);
        GlowRing.Opacity = light ? 0.4 : 0.6;
        if (IsInitialized && new System.Windows.Interop.WindowInteropHelper(this).Handle != IntPtr.Zero) Backdrop.SetDarkMode(this, !light);
    }

    private void AnimateIn()
    {
        if (!Animate) return;
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        Shell.BeginAnimation(OpacityProperty, new DoubleAnimation(0.35, 1, TimeSpan.FromMilliseconds(170)) { EasingFunction = ease });
        ShellShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(-14, 0, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        ShellScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        ShellScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.97, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = ease });
        AccentScale.BeginAnimation(ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(520)) { EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }, BeginTime = TimeSpan.FromMilliseconds(60) });
    }

    private void Refresh()
    {
        var text = Input.Text;
        var trimmed = text.Trim();
        var config = _config();
        var items = new List<LauncherItem>();

        if (trimmed.StartsWith(':'))
        {
            var commands = _actions.Where(a => a.Group == SystemActions.AltSpacyGroup);
            var query = trimmed[1..];
            items.AddRange((query.Length == 0 ? commands.ToList() : ActionCatalog.Match(query, commands)).Select(a => ToItem(a, SystemActions.AltSpacyGroup)));
        }
        else if (trimmed.Length == 0)
        {
            items.AddRange(ClipboardItems(config));
            var recent = RecentItems().ToList();
            items.AddRange(recent);
            items.AddRange(ShortcutItems(text, config));
            var recentKeys = recent.Select(r => r.Action?.Key).ToHashSet();
            items.AddRange(_actions.Where(a => IsSuggested(a) && !recentKeys.Contains(a.Key)).Select(a => ToItem(a, SuggestedGroup)));
        }
        else
        {
            if (Calculator.TryEvaluate(trimmed) is { } value) items.Add(ToItem(CalculatorAction(trimmed, value), CalculatorGroup));
            var resolved = ShortcutResolver.Resolve(text, config.Shortcuts, config.Projects);
            items.AddRange(ShortcutItems(text, config));
            items.AddRange(SmartActions(trimmed, resolved is not null, config).Select(a => ToItem(a, a.Group)));
            if (trimmed.Length >= 2)
            {
                var matched = ActionCatalog.Match(text, _actions, a => History.Score(a.HistoryKind, a.HistoryId ?? a.Key));
                items.AddRange(matched.GroupBy(a => a.Group).SelectMany(g => g).Select(a => ToItem(a, a.Group)));
                if (resolved is null && SmartInput.WebUrl(trimmed) is null && !SmartInput.IsEmail(trimmed) && !IsTeamsQuery(trimmed, config)
                    && !IsCatalogCommand(text, matched) && SystemActions.RunText(text, _services.Resolver) is { } run)
                    items.Add(ToItem(run, run.Group));
            }
        }

        _items = items;
        var view = new ListCollectionView(items);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LauncherItem.Group)));
        Suggestions.ItemsSource = view;
        Suggestions.SelectedIndex = _items.Count > 0 && trimmed.Length > 0 ? 0 : -1;
        Suggestions.Visibility = _items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Placeholder.Visibility = text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdatePreview();
    }

    /// <summary>Shortcuts matching the typed key, the ones you use most first, grouped by app.</summary>
    private IEnumerable<LauncherItem> ShortcutItems(string text, AltSpacyConfig config) =>
        ShortcutResolver.Suggest(text, config.Shortcuts)
            .OrderByDescending(s => History.Score(History.ShortcutKey, s.Key))
            .Select(s => new LauncherItem(s.App, s.Key, s.Description ?? s.Key, IconService.HostOf(s), _icons.For(s), Shortcut: s))
            .GroupBy(i => i.Group).SelectMany(g => g);

    /// <summary>What you used most recently and most often, ready to run again.</summary>
    private IEnumerable<LauncherItem> RecentItems()
    {
        foreach (var entry in History.Top(5))
        {
            switch (entry.Kind)
            {
                case History.Url:
                    yield return new LauncherItem(RecentGroup, "", entry.Title, entry.Detail, _icons.For(new Shortcut { Url = entry.Id }),
                        Action: OpenAction(entry.Id, entry.Title, null));
                    break;
                case History.Run when SystemActions.RunText(entry.Id, _services.Resolver) is { } run:
                    yield return ToItem(run, RecentGroup);
                    break;
                case History.Action when _actions.FirstOrDefault(a => a.Key.Equals(entry.Id, StringComparison.OrdinalIgnoreCase)) is { } action:
                    yield return ToItem(action, RecentGroup);
                    break;
                case History.Launch:
                    yield return ToItem(LaunchAction(entry.Id, entry.Title, entry.Detail), RecentGroup);
                    break;
                case History.TeamsSearch:
                    yield return ToItem(TeamsSearchAction(entry.Id), RecentGroup);
                    break;
            }
        }
    }

    /// <summary>A Jira key, link or email you just copied, offered at the top when the bar opens.</summary>
    private IEnumerable<LauncherItem> ClipboardItems(AltSpacyConfig config)
    {
        if (!_clipboardRead)
        {
            _clipboardText = _services.ReadClipboard()?.Trim();
            _clipboardRead = true;
        }
        var text = _clipboardText;
        if (string.IsNullOrEmpty(text) || text.Length > 500 || text.Contains('\n')) yield break;

        if (SmartInput.IsJiraKey(text)
            && config.Shortcuts.FirstOrDefault(s => s.App == "Jira" && s.UsesValue && !s.UsesProject) is { } jira
            && ShortcutResolver.Resolve($"{jira.Key} {text}", config.Shortcuts, config.Projects) is { } issue)
            yield return new LauncherItem(ClipboardGroup, jira.Key, $"Open {text}", issue.Url, _icons.For(jira), Action: OpenAction(issue.Url, $"{jira.Key} {text}", jira));
        else if (SmartInput.WebUrl(text) is { } url)
            yield return new LauncherItem(ClipboardGroup, "", "Open copied link", url, _icons.For(new Shortcut { Url = url }), Action: OpenAction(url, url, null));
        else if (SmartInput.IsEmail(text))
            yield return ToItem(TeamsChatAction(text), ClipboardGroup);
    }

    private static bool IsTeamsQuery(string text, AltSpacyConfig config) =>
        text.Length > 2 && text.StartsWith("t ", StringComparison.OrdinalIgnoreCase)
        && !config.Shortcuts.Any(s => s.Key.Equals("t", StringComparison.OrdinalIgnoreCase));

    /// <summary>"t name" / "t email" for Teams, a bare email (Teams or mail), or a web address.</summary>
    private IEnumerable<LauncherAction> SmartActions(string text, bool shortcutResolved, AltSpacyConfig config)
    {
        if (shortcutResolved) yield break;
        if (IsTeamsQuery(text, config))
        {
            var who = text[2..].Trim();
            if (who.Length < 2) yield break;
            if (SmartInput.IsEmail(who)) yield return TeamsChatAction(who);
            yield return TeamsSearchAction(who);
        }
        else if (SmartInput.IsEmail(text))
        {
            yield return TeamsChatAction(text);
            yield return LaunchAction(SmartInput.MailTo(text), $"Email {text}", "Opens a new message in your mail app");
        }
        else if (SmartInput.WebUrl(text) is { } url)
        {
            yield return OpenAction(url, text, null, WebGroup);
        }
    }

    private LauncherAction TeamsChatAction(string email) =>
        LaunchAction(SmartInput.TeamsChat(email), $"Chat with {email} in Teams", "Opens the chat in Microsoft Teams");

    private LauncherAction TeamsSearchAction(string who) => new()
    {
        Title = () => $"Search Teams for \u201C{who}\u201D", Detail = () => "Opens Teams and searches people, chats and messages",
        Keywords = [], Icon = () => IconArt.Glyph(TeamsGlyph, TeamsPurple), Run = () => _services.SearchTeams(who),
        Group = PeopleGroup, Glyphs = [TeamsGlyph], HistoryKind = History.TeamsSearch, HistoryId = who,
    };

    private LauncherAction LaunchAction(string uri, string title, string detail)
    {
        var teams = uri.StartsWith("msteams:", StringComparison.OrdinalIgnoreCase);
        var glyph = teams ? TeamsGlyph : MailGlyph;
        var color = teams ? TeamsPurple : MailBlue;
        return new()
        {
            Title = () => title, Detail = () => detail, Keywords = [], Icon = () => IconArt.Glyph(glyph, color),
            Run = () => _services.Launch(uri), Group = PeopleGroup, Glyphs = [glyph], HistoryKind = History.Launch, HistoryId = uri,
        };
    }

    /// <summary>Opens a URL in the configured browser. Open() records it in history itself.</summary>
    private LauncherAction OpenAction(string url, string title, Shortcut? shortcut, string group = RecentGroup) => new()
    {
        Title = () => group == WebGroup ? $"Open {title}" : title, Detail = () => url, Keywords = [],
        Icon = () => IconArt.Glyph(WebGlyph, WebTeal), Run = () => Open(url, shortcut, title, hide: false),
        Group = group, Glyphs = [WebGlyph], HistoryKind = "",
    };

    private LauncherAction CalculatorAction(string expression, double value) => new()
    {
        Title = () => "= " + Calculator.Format(value), Detail = () => $"{expression}    Enter copies {Calculator.Plain(value)}",
        Keywords = [], Icon = () => IconArt.Glyph(CalculatorGlyph, Slate), Run = () => _services.CopyText(Calculator.Plain(value)),
        Group = CalculatorGroup, Glyphs = [CalculatorGlyph], HistoryKind = "",
    };

    /// <summary>One IconHandle per image, so the icon doesn't "pop" again on every keystroke while the same action stays selected.</summary>
    private LauncherItem ToItem(LauncherAction a, string group)
    {
        var image = a.Icon();
        if (!_actionIcons.TryGetValue(image, out var icon)) _actionIcons[image] = icon = new IconHandle(image);
        return new(group, a.Badge, a.Title(), a.Detail(), icon, Action: a);
    }

    private static bool IsSuggested(LauncherAction a) =>
        a.Glyphs.Contains(SystemActions.Moon) && a.Group == SystemActions.WindowsGroup
        || a.Badge is "wt" or "explorer" or "taskmgr" or "services.msc"
        || a.Title() == "AltSpacy settings";

    private static bool IsCatalogCommand(string text, IReadOnlyList<LauncherAction> matched)
    {
        var (command, arguments) = RunTarget.Split(text);
        return arguments.Length == 0 && matched.Any(a => a.Keywords.Length > 0 && a.Keywords[0].Equals(command, StringComparison.OrdinalIgnoreCase));
    }

    private void UpdatePreview()
    {
        var text = Input.Text.Trim();
        var selected = Suggestions.SelectedItem as LauncherItem;
        ShowIcon(text.Length == 0 || selected is null ? _logo : selected.Icon, animate: true);

        var chip = text.Length == 0 ? null : selected?.Shortcut?.App ?? selected?.Group;
        AppChip.Visibility = chip is null ? Visibility.Collapsed : Visibility.Visible;
        AppChipText.Text = chip ?? "";

        var config = _config();
        Preview.Text = text switch
        {
            "" => "",
            _ when selected?.Action is { Available: false } => selected.Detail,
            _ when selected?.Action is not null => "",
            _ when text.StartsWith(':') => "AltSpacy command " + text,
            _ => ShortcutResolver.Resolve(text, config.Shortcuts, config.Projects)?.Url ?? selected?.Shortcut?.Url ?? "No match",
        };
        PreviewChip.Visibility = Preview.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Shows the selected app's icon next to the input (with a small pop), or the AltSpacy logo.</summary>
    private void ShowIcon(IconHandle handle, bool animate)
    {
        if (ReferenceEquals(_shownIcon, handle)) return;
        _shownIcon = handle;
        LogoPlate.DataContext = handle;
        LogoImage.SetBinding(Image.SourceProperty, new Binding(nameof(IconHandle.Image)) { Source = handle });
        if (!animate || !Animate) return;
        var pop = new DoubleAnimation(0.7, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.5 } };
        IconScale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
        IconScale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
    }

    internal void Submit(bool elevated)
    {
        var text = Input.Text.Trim();
        if (Suggestions.SelectedItem is LauncherItem { Action: { } action })
        {
            Run(action, elevated);
            return;
        }

        if (text.StartsWith(':'))
        {
            Input.Clear();
            Hide();
            _runCommand(text[1..].Trim().ToLowerInvariant());
            return;
        }

        var config = _config();
        if (ShortcutResolver.Resolve(text, config.Shortcuts, config.Projects) is { } resolution) Open(resolution.Url, resolution.Shortcut, text);
        else Complete();
    }

    private void Open(string url, Shortcut? shortcut, string title, bool hide = true)
    {
        try
        {
            _services.OpenUrl(url, _config().Browser);
            History.Record(History.Url, url, title, url);
            if (shortcut is not null) History.Record(History.ShortcutKey, shortcut.Key);
            if (!hide) return;
            Input.Clear();
            Hide();
        }
        catch (Exception ex)
        {
            Log.Write($"Could not open {url}", ex);
            Preview.Text = "Could not open: " + ex.Message;
            PreviewChip.Visibility = Visibility.Visible;
            if (!hide) throw;
        }
    }

    private void Run(LauncherAction action, bool elevated)
    {
        try
        {
            (elevated && action.RunElevated is not null ? action.RunElevated : action.Run)();
            if (action.HistoryKind.Length > 0) History.Record(action.HistoryKind, action.HistoryId ?? action.Key, action.Title(), action.Detail());
            Input.Clear();
            Hide();
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            Preview.Text = "Cancelled at the admin prompt";
            PreviewChip.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            if (ex is not InvalidOperationException) Log.Write($"Could not run {action.Title()}", ex);
            Preview.Text = ex is InvalidOperationException ? ex.Message : "Could not run: " + ex.Message;
            PreviewChip.Visibility = Visibility.Visible;
        }
    }

    private void Complete()
    {
        switch (Suggestions.SelectedItem)
        {
            case LauncherItem { Shortcut: { } s }:
                var value = ShortcutResolver.Parse(Input.Text) is { HasSeparator: true } parsed ? parsed.Value : "";
                Input.Text = $"{s.Key} {value}";
                break;
            case LauncherItem { Action: { Group: not (SystemActions.RunGroup or PeopleGroup or WebGroup or CalculatorGroup or RecentGroup or ClipboardGroup) } a }:
                Input.Text = a.Title();
                break;
            default:
                return;
        }
        Input.CaretIndex = Input.Text.Length;
    }

    private void Move(int delta)
    {
        if (Suggestions.Items.Count == 0) return;
        var index = Suggestions.SelectedIndex < 0 && delta < 0 ? 0 : Suggestions.SelectedIndex;
        Suggestions.SelectedIndex = (index + delta + Suggestions.Items.Count) % Suggestions.Items.Count;
        Suggestions.ScrollIntoView(Suggestions.SelectedItem);
    }

    /// <summary>
    /// Mouse click on a row: actions run, shortcuts that need no value (or already have one) open,
    /// and other shortcuts put "key " in the bar so you can type the value.
    /// </summary>
    private void OnItemClicked(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem { DataContext: LauncherItem item }) return;
        e.Handled = true;
        Suggestions.SelectedItem = item;
        var config = _config();
        switch (item)
        {
            case { Action: { } action }:
                Run(action, elevated: false);
                break;
            case { Shortcut: { HasPlaceholder: false } shortcut }:
                Open(shortcut.Url, shortcut, shortcut.Key);
                break;
            case { Shortcut: { } shortcut } when ShortcutResolver.Resolve(Input.Text, [shortcut], config.Projects) is { } resolution:
                Open(resolution.Url, shortcut, Input.Text.Trim());
                break;
            case { Shortcut: { } shortcut } when Input.Text.Trim().Length == 0 && ShortcutResolver.Resolve(shortcut.Key, [shortcut], config.Projects) is { } bare:
                Open(bare.Url, shortcut, shortcut.Key);
                break;
            default:
                Complete();
                break;
        }
        if (IsVisible) Input.Focus();
    }

    private void OnInputChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void OnSuggestionChanged(object sender, SelectionChangedEventArgs e) => UpdatePreview();

    private void OnDeactivated(object? sender, EventArgs e) => Hide();

    /// <summary>Footer chips run the same :commands you can type.</summary>
    private void OnFooterCommand(object sender, RoutedEventArgs e)
    {
        Input.Clear();
        Hide();
        _runCommand((string)((FrameworkElement)sender).Tag);
    }

    private void OnInputKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape when Input.Text.Length > 0: Input.Clear(); break;
            case Key.Escape: Hide(); break;
            case Key.Enter: Submit(elevated: Keyboard.Modifiers.HasFlag(ModifierKeys.Control | ModifierKeys.Shift)); break;
            case Key.Tab: Complete(); break;
            case Key.Down: Move(1); break;
            case Key.Up: Move(-1); break;
            default: return;
        }
        e.Handled = true;
    }
}

public sealed class UpperCaseConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value?.ToString()?.ToUpperInvariant() ?? "";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
