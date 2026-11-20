using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AltSpacy;

/// <summary>An icon that starts as a placeholder and updates in place once the real image has loaded.</summary>
public sealed class IconHandle(ImageSource image) : INotifyPropertyChanged
{
    private ImageSource _image = image;

    public ImageSource Image
    {
        get => _image;
        set
        {
            if (ReferenceEquals(_image, value)) return;
            _image = value;
            PropertyChanged?.Invoke(this, new(nameof(Image)));
        }
    }

    /// <summary>True for mostly-black icons (GitHub's octocat) that need a light backplate on dark backgrounds.</summary>
    public bool NeedsPlate
    {
        get;
        set
        {
            if (field == value) return;
            field = value;
            PropertyChanged?.Invoke(this, new(nameof(NeedsPlate)));
        }
    }

    public Task Loading { get; internal set; } = Task.CompletedTask;

    /// <summary>When the last load failed, so the service can retry later instead of never.</summary>
    internal DateTime? FailedAt { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Resolves an icon for each shortcut: the "icon" override if set, otherwise the site's /favicon.ico.
/// Downloads are cached in <c>icons\</c> next to the exe; until an icon loads (or if it can't), a monogram is shown.
/// </summary>
public sealed class IconService
{
    private const int MaxIconBytes = 1024 * 1024;
    private readonly string _cacheDir;
    private readonly HttpClient _http;
    private readonly Dictionary<string, IconHandle> _handles = new(StringComparer.OrdinalIgnoreCase);

    public IconService(string cacheDir, HttpMessageHandler? handler = null)
    {
        _cacheDir = cacheDir;
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(8) };
        // Some sites (Wikipedia, Stack Overflow) answer 403 to requests without a User-Agent.
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.Name}/{AppInfo.Version}");
    }

    public static string DefaultCacheDir => AppPaths.Icons;

    /// <summary>A failed favicon (offline, VPN down) is retried after this long instead of staying a monogram.</summary>
    public TimeSpan RetryAfter { get; set; } = TimeSpan.FromMinutes(5);

    public static string HostOf(Shortcut shortcut) =>
        Uri.TryCreate(ShortcutResolver.Sample(shortcut.Url), UriKind.Absolute, out var uri) ? uri.Host : shortcut.Key;

    public static string SourceFor(Shortcut shortcut) =>
        string.IsNullOrWhiteSpace(shortcut.Icon) ? $"https://{HostOf(shortcut)}/favicon.ico" : shortcut.Icon.Trim();

    public IconHandle For(Shortcut shortcut)
    {
        var source = SourceFor(shortcut);
        if (_handles.TryGetValue(source, out var handle))
        {
            if (handle.FailedAt is { } failed && DateTime.Now - failed >= RetryAfter && handle.Loading.IsCompleted)
            {
                handle.FailedAt = null;
                handle.Loading = LoadAsync(handle, source);
            }
            return handle;
        }

        handle = new IconHandle(IconArt.Monogram(HostOf(shortcut)));
        _handles[source] = handle;
        handle.Loading = LoadAsync(handle, source);
        return handle;
    }

    private async Task LoadAsync(IconHandle handle, string source)
    {
        try
        {
            var isRemote = source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || source.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
            var cacheFile = Path.Combine(_cacheDir, CacheName(source));

            byte[] bytes;
            if (!isRemote) bytes = await File.ReadAllBytesAsync(source);
            else if (File.Exists(cacheFile)) bytes = await File.ReadAllBytesAsync(cacheFile);
            else bytes = await DownloadAsync(source);

            var (image, dark) = await Task.Run(() =>
            {
                var decoded = IconArt.Decode(bytes);
                return (decoded, IconArt.IsDark(decoded));
            });
            if (isRemote && !File.Exists(cacheFile))
            {
                Directory.CreateDirectory(_cacheDir);
                await File.WriteAllBytesAsync(cacheFile, bytes);
            }
            handle.NeedsPlate = dark;
            handle.Image = image;
        }
        catch (Exception ex)
        {
            // Icons are cosmetic: keep the monogram, note why, and retry later.
            handle.FailedAt = DateTime.Now;
            Log.Write($"Icon not loaded: {source}", ex);
        }
    }

    private async Task<byte[]> DownloadAsync(string url)
    {
        var uri = new Uri(url);
        for (var hop = 0; hop < 5; hop++)
        {
            using var response = await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                var next = location.IsAbsoluteUri ? location : new Uri(uri, location);
                // Servers behind a TLS proxy (e.g. Splunk) redirect https -> http on the same host. Stay on https.
                if (uri.Scheme == Uri.UriSchemeHttps && next.Scheme == Uri.UriSchemeHttp && next.Host == uri.Host)
                    next = new UriBuilder(next) { Scheme = Uri.UriSchemeHttps, Port = -1 }.Uri;
                uri = next;
                continue;
            }

            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength > MaxIconBytes) throw new InvalidDataException("Icon too large.");
            var bytes = await response.Content.ReadAsByteArrayAsync();
            return bytes.Length <= MaxIconBytes ? bytes : throw new InvalidDataException("Icon too large.");
        }
        throw new HttpRequestException("Too many redirects.");
    }

    internal static string CacheName(string source)
    {
        var name = Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.AbsolutePath == "/favicon.ico" ? uri.Host : $"{Fnv(source):x16}";
        return name + ".icon";
    }

    internal static ulong Fnv(string text)
    {
        var hash = 14695981039346656037UL;
        foreach (var c in text.ToLowerInvariant()) hash = (hash ^ c) * 1099511628211UL;
        return hash;
    }
}

public static class IconArt
{
    private static readonly Color[] Palette =
    [
        Color.FromRgb(0xE1, 0x4D, 0x2A), Color.FromRgb(0x2F, 0x85, 0x5A), Color.FromRgb(0x2B, 0x6C, 0xB0), Color.FromRgb(0x80, 0x5A, 0xD5),
        Color.FromRgb(0xD5, 0x3F, 0x8C), Color.FromRgb(0x0F, 0x76, 0x6E), Color.FromRgb(0xB7, 0x79, 0x1F), Color.FromRgb(0x4A, 0x55, 0x68),
    ];

    private static readonly FontFamily IconFont = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private static readonly Dictionary<string, ImageSource> Cache = [];

    /// <summary>Decodes PNG/ICO/JPEG/GIF/BMP bytes, picking the largest frame. Throws if the bytes aren't an image.</summary>
    public static BitmapSource Decode(byte[] bytes)
    {
        var decoder = BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
        frame.Freeze();
        return frame;
    }

    /// <summary>True when the visible pixels are mostly very dark, so the icon would vanish on a dark background.</summary>
    public static bool IsDark(BitmapSource image)
    {
        var bgra = new FormatConvertedBitmap(image, PixelFormats.Bgra32, null, 0);
        var stride = bgra.PixelWidth * 4;
        var pixels = new byte[stride * bgra.PixelHeight];
        bgra.CopyPixels(pixels, stride, 0);

        double luminance = 0, weight = 0;
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3] / 255.0;
            if (alpha < 0.25) continue;
            luminance += alpha * (0.0722 * pixels[i] + 0.7152 * pixels[i + 1] + 0.2126 * pixels[i + 2]) / 255.0;
            weight += alpha;
        }
        return weight > 0 && luminance / weight < 0.22;
    }

    /// <summary>A colored circle with the first letter of the host, e.g. "J" for jira.example.com.</summary>
    public static ImageSource Monogram(string host)
    {
        var letter = host.FirstOrDefault(char.IsLetterOrDigit);
        var color = Palette[IconService.Fnv(host) % (ulong)Palette.Length];
        return Cached($"mono:{host}", () => Tile(color, circle: true, Text(char.ToUpperInvariant(letter == default ? '?' : letter).ToString(),
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal), 150)));
    }

    /// <summary>A rounded tile with a Segoe Fluent Icons glyph.</summary>
    public static ImageSource Glyph(string glyph, Color background) =>
        Cached($"glyph:{glyph}:{background}", () => Tile(background, circle: false, Text(glyph, new Typeface(IconFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), 136)));

    private static Geometry Text(string text, Typeface typeface, double size)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, Brushes.White, 1.0);
        var geometry = formatted.BuildGeometry(new Point(0, 0));
        var bounds = geometry.Bounds;
        geometry.Transform = new TranslateTransform(128 - bounds.X - bounds.Width / 2, 128 - bounds.Y - bounds.Height / 2);
        return geometry;
    }

    private static ImageSource Tile(Color background, bool circle, Geometry foreground)
    {
        var group = new DrawingGroup();
        Geometry shape = circle ? new EllipseGeometry(new Point(128, 128), 128, 128) : new RectangleGeometry(new Rect(0, 0, 256, 256), 60, 60);
        group.Children.Add(new GeometryDrawing(new SolidColorBrush(background), null, shape));
        group.Children.Add(new GeometryDrawing(Brushes.White, null, foreground));
        group.Freeze();
        var image = new DrawingImage(group);
        image.Freeze();
        return image;
    }

    private static ImageSource Cached(string key, Func<ImageSource> create)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue(key, out var image)) Cache[key] = image = create();
            return image;
        }
    }
}
