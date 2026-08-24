using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MediaFetch.Desktop.Models;
using MediaFetch.Desktop.Services;

namespace MediaFetch.Desktop;

public partial class MainWindow : Window
{
    private readonly YtDlpService _ytDlpService = new();
    private readonly ThumbnailService _thumbnailService = new();
    private readonly SettingsService _settingsService = new();
    private readonly ThemeService _themeService = new();
    private string? _downloadsDirectory;
    private string? _preferredQuality;
    private string _currentTheme = ThemeService.Dark;
    private bool _isRefreshingQualityOptions;
    private CancellationTokenSource? _linkCheckCancellation;
    private MediaMetadata? _mediaMetadata;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureInitialWindowSize();
        Loaded += (_, _) => AnimateWindowContentIn();

        var settings = _settingsService.Load();
        _currentTheme = _themeService.Apply(settings.Theme);
        UpdateThemeButton();
        _downloadsDirectory = settings.DownloadDirectory;
        _preferredQuality = settings.LastQuality;

        FolderText.Text = string.IsNullOrWhiteSpace(_downloadsDirectory)
            ? "No folder selected"
            : _downloadsDirectory;

        SelectFormat(settings.LastFormatTag);
        RefreshQualityOptions();
        FormatComboBox.SelectionChanged += FormatComboBox_SelectionChanged;
        QualityComboBox.SelectionChanged += QualityComboBox_SelectionChanged;
    }

    private void ConfigureInitialWindowSize()
    {
        var workArea = SystemParameters.WorkArea;
        var maximumWidth = Math.Max(MinWidth, Math.Min(1500, workArea.Width - 64));
        var maximumHeight = Math.Max(MinHeight, Math.Min(1050, workArea.Height - 40));

        Width = Math.Clamp(workArea.Width * 0.72, MinWidth, maximumWidth);
        Height = Math.Clamp(workArea.Height * 0.86, MinHeight, maximumHeight);
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var scale = Math.Clamp(e.NewSize.Width / 1400, 0.74, 1.18);
        ResponsiveScale.ScaleX = scale;
        ResponsiveScale.ScaleY = scale;
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        var nextTheme = _currentTheme == ThemeService.Dark
            ? ThemeService.Light
            : ThemeService.Dark;

        _currentTheme = _themeService.Apply(nextTheme);
        UpdateThemeButton();
        SaveSettings();
    }

    private void UpdateThemeButton()
    {
        var isDark = _currentTheme == ThemeService.Dark;
        SunIcon.Visibility = isDark ? Visibility.Visible : Visibility.Collapsed;
        MoonIcon.Visibility = isDark ? Visibility.Collapsed : Visibility.Visible;
        ThemeButton.ToolTip = isDark ? "Switch to light theme" : "Switch to dark theme";
    }

    private void AnimateWindowContentIn()
    {
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(180);

        WindowContent.Opacity = 1;
        WindowContentScale.ScaleX = 1;
        WindowContentScale.ScaleY = 1;
        WindowContent.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0.72, 1, duration) { EasingFunction = easing });
        WindowContentScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.985, 1, duration) { EasingFunction = easing });
        WindowContentScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.985, 1, duration) { EasingFunction = easing });
    }

    private void MediaCard_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        const double aspectRatio = 16d / 9d;
        var targetHeight = Math.Clamp(e.NewSize.Width / aspectRatio, 340, 500);

        if (Math.Abs(MediaCard.Height - targetHeight) > 0.5)
        {
            MediaCard.Height = targetHeight;
        }
    }

    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetUrl(out var url) || url is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_downloadsDirectory))
        {
            StatusText.Text = "Status: Choose a download folder first.";
            return;
        }

        var formatTag = GetSelectedFormatTag();

        if (formatTag is null)
        {
            StatusText.Text = "Status: Choose an output format.";
            return;
        }

        int? videoHeight = null;
        int? audioBitrate = null;
        var selectedQuality = QualityComboBox.SelectedItem?.ToString();

        if (formatTag.StartsWith("video:", StringComparison.Ordinal))
        {
            if (selectedQuality is null
                || !int.TryParse(selectedQuality.TrimEnd('p'), out var parsedHeight))
            {
                StatusText.Text = "Status: Choose a valid video quality.";
                return;
            }

            videoHeight = parsedHeight;
        }
        else if (RequiresAudioBitrate(formatTag))
        {
            if (selectedQuality is null
                || !int.TryParse(selectedQuality.Split(' ')[0], out var parsedBitrate))
            {
                StatusText.Text = "Status: Choose a valid audio bitrate.";
                return;
            }

            audioBitrate = parsedBitrate;
        }

        var request = new DownloadRequest(
            Url: url,
            OutputDirectory: _downloadsDirectory,
            FormatTag: formatTag,
            VideoHeight: videoHeight,
            AudioBitrateKbps: audioBitrate,
            SourceAudioBitrateKbps: _mediaMetadata?.SourceAudioBitrateKbps);

        DownloadButton.IsEnabled = false;
        StatusText.Text = "Status: Downloading...";

        try
        {
            await _ytDlpService.DownloadAsync(request);
            StatusText.Text = "Status: Download complete.";
        }
        catch (YtDlpException exception)
        {
            StatusText.Text = "Status: Download failed.";
            System.Windows.MessageBox.Show(exception.Message, "yt-dlp error");
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Status: {exception.Message}";
        }
        finally
        {
            DownloadButton.IsEnabled = true;
        }
    }

    private void ChooseFolderButton_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Choose where MediaFetch saves downloaded files."
        };

        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK)
        {
            return;
        }

        _downloadsDirectory = dialog.SelectedPath;
        FolderText.Text = _downloadsDirectory;
        StatusText.Text = "Status: Download folder selected.";
        SaveSettings();
    }

    private async void UrlTextBox_TextChanged(
        object sender,
        System.Windows.Controls.TextChangedEventArgs e)
    {
        _linkCheckCancellation?.Cancel();
        _linkCheckCancellation?.Dispose();

        var cancellation = new CancellationTokenSource();
        _linkCheckCancellation = cancellation;
        _mediaMetadata = null;
        ResetMediaCard();
        RefreshQualityOptions();

        try
        {
            await Task.Delay(700, cancellation.Token);

            if (!TryGetUrl(out var url) || url is null)
            {
                return;
            }

            StatusText.Text = "Status: Checking link...";
            var metadata = await _ytDlpService.InspectAsync(url, cancellation.Token);
            cancellation.Token.ThrowIfCancellationRequested();

            _mediaMetadata = metadata;
            RefreshQualityOptions();
            ShowMediaMetadata(metadata);
            StatusText.Text = $"Status: Found {metadata.VideoHeights.Count} video qualities.";
            await LoadThumbnailAsync(metadata.ThumbnailUrl, cancellation.Token);
        }
        catch (OperationCanceledException)
        {
            // A newer URL replaced this inspection request.
        }
        catch (YtDlpException exception)
        {
            StatusText.Text = "Status: Could not inspect link.";
            System.Windows.MessageBox.Show(exception.Message, "yt-dlp error");
        }
        catch (Exception exception)
        {
            StatusText.Text = $"Status: {exception.Message}";
        }
    }

    private bool TryGetUrl(out Uri? url)
    {
        var urlText = UrlTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(urlText))
        {
            url = null;
            StatusText.Text = "Status: Paste a URL first.";
            return false;
        }

        if (!Uri.TryCreate(urlText, UriKind.Absolute, out url)
            || url is null
            || (url.Scheme != Uri.UriSchemeHttp
                && url.Scheme != Uri.UriSchemeHttps))
        {
            url = null;
            StatusText.Text = "Status: Enter a valid HTTP or HTTPS URL.";
            return false;
        }

        return true;
    }

    private void FormatComboBox_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        RefreshQualityOptions();
        SaveSettings();
    }

    private void QualityComboBox_SelectionChanged(
        object sender,
        System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_isRefreshingQualityOptions || !QualityComboBox.IsEnabled)
        {
            return;
        }

        _preferredQuality = QualityComboBox.SelectedItem?.ToString();
        SaveSettings();
    }

    private string? GetSelectedFormatTag()
    {
        return (FormatComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
            ?.Tag
            ?.ToString();
    }

    private void SelectFormat(string? formatTag)
    {
        if (string.IsNullOrWhiteSpace(formatTag))
        {
            return;
        }

        foreach (var item in FormatComboBox.Items)
        {
            if (item is System.Windows.Controls.ComboBoxItem comboBoxItem
                && string.Equals(
                    comboBoxItem.Tag?.ToString(),
                    formatTag,
                    StringComparison.Ordinal))
            {
                FormatComboBox.SelectedItem = comboBoxItem;
                return;
            }
        }
    }

    private void SaveSettings()
    {
        try
        {
            _settingsService.Save(new AppSettings(
                DownloadDirectory: _downloadsDirectory,
                LastFormatTag: GetSelectedFormatTag(),
                LastQuality: _preferredQuality,
                Theme: _currentTheme));
        }
        catch (IOException)
        {
            StatusText.Text = "Status: Could not save settings.";
        }
        catch (UnauthorizedAccessException)
        {
            StatusText.Text = "Status: Could not save settings.";
        }
    }

    private static bool RequiresAudioBitrate(string formatTag)
    {
        return formatTag is "audio:mp3" or "audio:m4a" or "audio:opus";
    }

    private void RefreshQualityOptions()
    {
        _isRefreshingQualityOptions = true;
        QualityComboBox.Items.Clear();
        var formatTag = GetSelectedFormatTag();

        if (formatTag?.StartsWith("video:", StringComparison.Ordinal) == true)
        {
            var targetFormat = formatTag["video:".Length..];
            var compatibleHeights = targetFormat switch
            {
                "mp4" or "mov" => _mediaMetadata?.Mp4VideoHeights,
                "webm" => _mediaMetadata?.WebMVideoHeights,
                _ => _mediaMetadata?.VideoHeights
            };

            foreach (var height in compatibleHeights ?? Array.Empty<int>())
            {
                QualityComboBox.Items.Add($"{height}p");
            }

            if (QualityComboBox.Items.Count == 0)
            {
                QualityComboBox.Items.Add(
                    _mediaMetadata is null ? "Waiting for link..." : "No compatible video");
                QualityComboBox.SelectedIndex = 0;
                QualityComboBox.IsEnabled = false;
                _isRefreshingQualityOptions = false;
                return;
            }
        }
        else if (formatTag == "audio:original")
        {
            QualityComboBox.Items.Add("No conversion");
        }
        else if (formatTag is "audio:mp3" or "audio:m4a")
        {
            AddBitrates(128, 192, 256, 320);
        }
        else if (formatTag == "audio:opus")
        {
            AddBitrates(96, 128, 160, 192, 256);
        }
        else if (formatTag is "audio:flac" or "audio:wav")
        {
            QualityComboBox.Items.Add("No bitrate setting");
        }

        QualityComboBox.IsEnabled = QualityComboBox.Items.Count > 0;

        if (_preferredQuality is not null
            && QualityComboBox.Items.Contains(_preferredQuality))
        {
            QualityComboBox.SelectedItem = _preferredQuality;
        }
        else if (QualityComboBox.Items.Count > 0)
        {
            QualityComboBox.SelectedIndex = QualityComboBox.Items.Count - 1;
        }

        _preferredQuality = QualityComboBox.IsEnabled
            ? QualityComboBox.SelectedItem?.ToString()
            : _preferredQuality;
        _isRefreshingQualityOptions = false;
    }

    private void AddBitrates(params int[] bitrates)
    {
        foreach (var bitrate in bitrates)
        {
            QualityComboBox.Items.Add($"{bitrate} kbps");
        }
    }

    private void ShowMediaMetadata(MediaMetadata metadata)
    {
        MediaOverlay.Visibility = Visibility.Visible;
        PreviewGradient.Visibility = Visibility.Visible;
        PlatformText.Text = GetPlatformDisplayName(metadata.Source);
        PlatformIcon.Data = (Geometry)FindResource(GetPlatformIconResourceKey(metadata.Source));
        MediaTitleText.Text = metadata.Title ?? "Untitled media";
        MediaAuthorText.Text = metadata.Author ?? "Unknown author";
        MediaDurationText.Text = FormatDuration(metadata.Duration);
        ThumbnailSurface.SetResourceReference(
            System.Windows.Controls.Border.BackgroundProperty,
            "PreviewPlaceholderBrush");
        ThumbnailPlaceholder.Visibility = Visibility.Visible;

        AnimateMediaCard();

        if (metadata.SourceAudioCodec is null)
        {
            MediaInfoText.Text = "Source audio: not detected";
            return;
        }

        var bitrate = metadata.SourceAudioBitrateKbps is null
            ? "unknown bitrate"
            : $"{metadata.SourceAudioBitrateKbps.Value:0} kbps";
        var extension = metadata.SourceAudioExtension?.ToUpperInvariant() ?? "unknown container";

        MediaInfoText.Text =
            $"Source audio: {metadata.SourceAudioCodec}, {extension}, {bitrate}";
    }

    private void ResetMediaCard()
    {
        MediaCard.BeginAnimation(OpacityProperty, null);
        MediaCardTranslate.BeginAnimation(TranslateTransform.YProperty, null);
        MediaCard.Opacity = 1;
        MediaCardTranslate.Y = 0;
        MediaOverlay.Visibility = Visibility.Collapsed;
        PreviewGradient.Visibility = Visibility.Collapsed;
        ThumbnailSurface.SetResourceReference(
            System.Windows.Controls.Border.BackgroundProperty,
            "PreviewPlaceholderBrush");
        ThumbnailPlaceholder.Visibility = Visibility.Visible;
    }

    private async Task LoadThumbnailAsync(
        string? thumbnailUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            var thumbnail = await _thumbnailService.LoadAsync(thumbnailUrl, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (thumbnail is null)
            {
                return;
            }

            ThumbnailSurface.Background = new ImageBrush(thumbnail)
            {
                Stretch = Stretch.UniformToFill
            };
            ThumbnailPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Metadata remains useful even when the source blocks its thumbnail.
        }
    }

    private void AnimateMediaCard()
    {
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        MediaCard.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(230))
            {
                EasingFunction = easing
            });

        MediaCardTranslate.BeginAnimation(
            TranslateTransform.YProperty,
            new DoubleAnimation(12, 0, TimeSpan.FromMilliseconds(230))
            {
                EasingFunction = easing
            });
    }

    private static string GetPlatformIconResourceKey(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "LinkIconGeometry";
        }

        if (source.Contains("youtube", StringComparison.OrdinalIgnoreCase))
        {
            return "YouTubeIconGeometry";
        }

        if (source.Contains("tiktok", StringComparison.OrdinalIgnoreCase))
        {
            return "TikTokIconGeometry";
        }

        if (source.Contains("instagram", StringComparison.OrdinalIgnoreCase))
        {
            return "InstagramIconGeometry";
        }

        if (source.Contains("pinterest", StringComparison.OrdinalIgnoreCase))
        {
            return "PinterestIconGeometry";
        }

        if (source.Contains("twitch", StringComparison.OrdinalIgnoreCase))
        {
            return "TwitchIconGeometry";
        }

        if (source.Contains("soundcloud", StringComparison.OrdinalIgnoreCase))
        {
            return "SoundCloudIconGeometry";
        }

        return "LinkIconGeometry";
    }

    private static string GetPlatformDisplayName(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return "Unknown source";
        }

        if (source.Contains("youtube", StringComparison.OrdinalIgnoreCase))
        {
            return "YouTube";
        }

        if (source.Contains("tiktok", StringComparison.OrdinalIgnoreCase))
        {
            return "TikTok";
        }

        if (source.Contains("instagram", StringComparison.OrdinalIgnoreCase))
        {
            return "Instagram";
        }

        if (source.Contains("pinterest", StringComparison.OrdinalIgnoreCase))
        {
            return "Pinterest";
        }

        if (source.Contains("twitch", StringComparison.OrdinalIgnoreCase))
        {
            return "Twitch";
        }

        if (source.Contains("soundcloud", StringComparison.OrdinalIgnoreCase))
        {
            return "SoundCloud";
        }

        return source;
    }

    private static string FormatDuration(TimeSpan? duration)
    {
        if (duration is null)
        {
            return "Duration unavailable";
        }

        return duration.Value.TotalHours >= 1
            ? $"Duration: {(int)duration.Value.TotalHours}:{duration.Value.Minutes:00}:{duration.Value.Seconds:00}"
            : $"Duration: {duration.Value.Minutes}:{duration.Value.Seconds:00}";
    }
}
