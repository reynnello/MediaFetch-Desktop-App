using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
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
    private readonly ErrorPresentationService _errorPresentationService = new();
    private string? _downloadsDirectory;
    private string? _preferredQuality;
    private string _currentTheme = ThemeService.Dark;
    private bool _isRefreshingQualityOptions;
    private bool _isErrorOverlayClosing;
    private int _errorOverlayAnimationVersion;
    private CancellationTokenSource? _linkCheckCancellation;
    private CancellationTokenSource? _downloadCancellation;
    private MediaMetadata? _mediaMetadata;
    private string? _lastDownloadedFilePath;

    public MainWindow()
    {
        InitializeComponent();
        ConfigureInitialWindowSize();
        Loaded += MainWindow_Loaded;

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
        var maximumHeight = Math.Max(MinHeight, Math.Min(1350, workArea.Height - 40));

        Width = Math.Clamp(workArea.Width * 0.72, MinWidth, maximumWidth);
        Height = maximumHeight;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        UpdateLayout();
        FitWindowToContent();
        AnimateWindowContentIn();
    }

    private void FitWindowToContent()
    {
        var workArea = SystemParameters.WorkArea;
        var chromeHeight = Math.Max(0, ActualHeight - WindowContent.ActualHeight);
        var desiredHeight = 54
            + ResponsiveContent.DesiredSize.Height
            + chromeHeight
            + 8;
        var availableHeight = Math.Max(MinHeight, workArea.Height - 40);

        Height = Math.Clamp(desiredHeight, MinHeight, availableHeight);
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
        if (_downloadCancellation is not null)
        {
            return;
        }

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
        var selectedQuality = GetSelectedQualityValue();

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

        _lastDownloadedFilePath = null;
        CompletionActionsPanel.Visibility = Visibility.Collapsed;
        DownloadButton.Visibility = Visibility.Collapsed;
        CancelDownloadButton.Content = "Cancel";
        CancelDownloadButton.IsEnabled = true;
        CancelDownloadButton.Visibility = Visibility.Visible;
        UrlTextBox.IsEnabled = false;
        FormatComboBox.IsEnabled = false;
        QualityComboBox.IsEnabled = false;
        DownloadProgress.Value = 0;
        ProgressPercentText.Text = "0%";
        StatusText.Text = "Status: Preparing download...";

        var progress = new Progress<DownloadProgressUpdate>(UpdateDownloadProgress);
        using var cancellation = new CancellationTokenSource();
        _downloadCancellation = cancellation;

        try
        {
            _lastDownloadedFilePath = await _ytDlpService.DownloadAsync(
                request,
                progress,
                cancellation.Token);
            DownloadProgress.Value = 100;
            ProgressPercentText.Text = "100%";
            StatusText.Text = "Status: Download complete.";
            CompletionActionsPanel.Visibility = Visibility.Visible;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            DownloadProgress.Value = 0;
            ProgressPercentText.Text = "—";
            StatusText.Text = "Status: Download canceled.";
        }
        catch (YtDlpException exception)
        {
            DownloadProgress.Value = 0;
            ProgressPercentText.Text = "—";
            StatusText.Text = "Status: Download failed.";
            ShowOperationError(exception, url, "Download failed");
        }
        catch (Exception exception)
        {
            DownloadProgress.Value = 0;
            ProgressPercentText.Text = "—";
            StatusText.Text = "Status: Download failed.";
            ShowOperationError(exception, url, "Download failed");
        }
        finally
        {
            if (ReferenceEquals(_downloadCancellation, cancellation))
            {
                _downloadCancellation = null;
            }

            CancelDownloadButton.Visibility = Visibility.Collapsed;
            DownloadButton.Visibility = Visibility.Visible;
            UrlTextBox.IsEnabled = true;
            FormatComboBox.IsEnabled = true;
            RefreshQualityOptions();
        }
    }

    private void CancelDownloadButton_Click(object sender, RoutedEventArgs e)
    {
        if (_downloadCancellation is not { IsCancellationRequested: false } cancellation)
        {
            return;
        }

        CancelDownloadButton.Content = "Canceling...";
        CancelDownloadButton.IsEnabled = false;
        StatusText.Text = "Status: Canceling download...";
        cancellation.Cancel();
    }

    private void OpenDownloadedFileButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDownloadedFilePath(out var filePath))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = filePath,
                UseShellExecute = true
            });
        }
        catch (Exception exception)
        {
            ShowOperationError(exception, null, "Could not open file");
        }
    }

    private void ShowDownloadedFileInFolderButton_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetDownloadedFilePath(out var filePath))
        {
            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("/select,");
            startInfo.ArgumentList.Add(filePath);
            Process.Start(startInfo);
        }
        catch (Exception exception)
        {
            ShowOperationError(exception, null, "Could not open download folder");
        }
    }

    private bool TryGetDownloadedFilePath(out string filePath)
    {
        if (!string.IsNullOrWhiteSpace(_lastDownloadedFilePath)
            && File.Exists(_lastDownloadedFilePath))
        {
            filePath = _lastDownloadedFilePath;
            return true;
        }

        filePath = string.Empty;
        CompletionActionsPanel.Visibility = Visibility.Collapsed;
        StatusText.Text = "Status: The downloaded file is no longer available.";
        return false;
    }

    private void UpdateDownloadProgress(DownloadProgressUpdate update)
    {
        if (update.Percentage is { } percentage)
        {
            DownloadProgress.Value = Math.Clamp(percentage, 0, 100);
            ProgressPercentText.Text = $"{percentage:0}%";
        }

        StatusText.Text = $"Status: {update.Status}";
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
        _lastDownloadedFilePath = null;
        CompletionActionsPanel.Visibility = Visibility.Collapsed;
        DownloadProgress.Value = 0;
        ProgressPercentText.Text = "—";
        ResetMediaCard();
        RefreshQualityOptions();
        Uri? inspectedUrl = null;

        try
        {
            await Task.Delay(700, cancellation.Token);

            if (!TryGetUrl(out inspectedUrl) || inspectedUrl is null)
            {
                return;
            }

            StatusText.Text = "Status: Checking link...";
            var metadata = await _ytDlpService.InspectAsync(inspectedUrl, cancellation.Token);
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
            ShowOperationError(exception, inspectedUrl, "Could not inspect link");
        }
        catch (Exception exception)
        {
            StatusText.Text = "Status: Could not inspect link.";
            ShowOperationError(exception, inspectedUrl, "Could not inspect link");
        }
    }

    private void ShowOperationError(
        Exception exception,
        Uri? sourceUrl,
        string fallbackTitle)
    {
        var presentation = _errorPresentationService.Create(
            exception,
            sourceUrl,
            fallbackTitle);
        ErrorTitleText.Text = presentation.Title;
        ErrorMessageText.Text = presentation.Message;
        ErrorSuggestionText.Text = presentation.Suggestion;
        ErrorTechnicalDetailsTextBox.Text = presentation.TechnicalDetails;
        ErrorDetailsPanel.Visibility = Visibility.Collapsed;
        ErrorDetailsButton.Content = "Show technical details";
        ErrorCopyButton.Content = "Copy details";
        _isErrorOverlayClosing = false;
        _errorOverlayAnimationVersion++;

        AppHeader.IsEnabled = false;
        MainScrollViewer.IsEnabled = false;
        ErrorOverlay.Visibility = Visibility.Visible;

        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        var duration = TimeSpan.FromMilliseconds(170);
        ErrorOverlay.BeginAnimation(
            OpacityProperty,
            new DoubleAnimation(0, 1, duration) { EasingFunction = easing });
        ErrorCardScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(0.97, 1, duration) { EasingFunction = easing });
        ErrorCardScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(0.97, 1, duration) { EasingFunction = easing });

        ErrorGotItButton.Focus();
    }

    private void DismissErrorButton_Click(object sender, RoutedEventArgs e)
    {
        CloseErrorOverlay();
    }

    private void CloseErrorOverlay()
    {
        if (_isErrorOverlayClosing || ErrorOverlay.Visibility != Visibility.Visible)
        {
            return;
        }

        _isErrorOverlayClosing = true;
        var animationVersion = ++_errorOverlayAnimationVersion;
        var easing = new CubicEase { EasingMode = EasingMode.EaseIn };
        var duration = TimeSpan.FromMilliseconds(170);
        var fadeOut = new DoubleAnimation(ErrorOverlay.Opacity, 0, duration)
        {
            EasingFunction = easing,
            FillBehavior = FillBehavior.HoldEnd
        };

        fadeOut.Completed += (_, _) =>
        {
            if (animationVersion != _errorOverlayAnimationVersion)
            {
                return;
            }

            ErrorOverlay.Visibility = Visibility.Collapsed;
            ErrorOverlay.BeginAnimation(OpacityProperty, null);
            ErrorCardScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            ErrorCardScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            ErrorOverlay.Opacity = 1;
            ErrorCardScale.ScaleX = 1;
            ErrorCardScale.ScaleY = 1;
            AppHeader.IsEnabled = true;
            MainScrollViewer.IsEnabled = true;
            _isErrorOverlayClosing = false;
            UrlTextBox.Focus();
        };

        ErrorOverlay.BeginAnimation(OpacityProperty, fadeOut);
        ErrorCardScale.BeginAnimation(
            ScaleTransform.ScaleXProperty,
            new DoubleAnimation(ErrorCardScale.ScaleX, 0.97, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            });
        ErrorCardScale.BeginAnimation(
            ScaleTransform.ScaleYProperty,
            new DoubleAnimation(ErrorCardScale.ScaleY, 0.97, duration)
            {
                EasingFunction = easing,
                FillBehavior = FillBehavior.HoldEnd
            });
    }

    private void ErrorOverlay_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        CloseErrorOverlay();
    }

    private void ErrorCard_MouseLeftButtonDown(
        object sender,
        MouseButtonEventArgs e)
    {
        e.Handled = true;
    }

    private void ErrorOverlay_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        CloseErrorOverlay();
        e.Handled = true;
    }

    private void ErrorDetailsButton_Click(object sender, RoutedEventArgs e)
    {
        var shouldShow = ErrorDetailsPanel.Visibility != Visibility.Visible;
        ErrorDetailsPanel.Visibility = shouldShow
            ? Visibility.Visible
            : Visibility.Collapsed;
        ErrorDetailsButton.Content = shouldShow
            ? "Hide technical details"
            : "Show technical details";
    }

    private void ErrorCopyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(ErrorTechnicalDetailsTextBox.Text);
            ErrorCopyButton.Content = "Copied";
        }
        catch
        {
            ErrorCopyButton.Content = "Could not copy";
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

        _preferredQuality = GetSelectedQualityValue();
        SaveSettings();
    }

    private string? GetSelectedQualityValue()
    {
        return (QualityComboBox.SelectedItem as QualityOption)?.Value;
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
                AddQualityOption(
                    value: $"{height}p",
                    label: $"{height}p",
                    estimatedBytes: EstimateVideoSize(height, targetFormat));
            }

            if (QualityComboBox.Items.Count == 0)
            {
                QualityComboBox.Items.Add(new QualityOption(
                    Value: string.Empty,
                    Label: _mediaMetadata is null
                        ? "Waiting for link..."
                        : "No compatible video"));
                QualityComboBox.SelectedIndex = 0;
                QualityComboBox.IsEnabled = false;
                _isRefreshingQualityOptions = false;
                return;
            }
        }
        else if (formatTag == "audio:original")
        {
            AddQualityOption(
                value: "No conversion",
                label: "No conversion",
                estimatedBytes: _mediaMetadata?.SourceAudioEstimatedBytes);
        }
        else if (formatTag is "audio:mp3" or "audio:m4a")
        {
            AddBitrates(formatTag, 128, 192, 256, 320);
        }
        else if (formatTag == "audio:opus")
        {
            AddBitrates(formatTag, 96, 128, 160, 192, 256);
        }
        else if (formatTag is "audio:flac" or "audio:wav")
        {
            AddQualityOption(
                value: "No bitrate setting",
                label: "No bitrate setting",
                estimatedBytes: EstimateAudioSize(formatTag, bitrateKbps: null));
        }

        QualityComboBox.IsEnabled = QualityComboBox.Items.Count > 0;

        var preferredOption = QualityComboBox.Items
            .OfType<QualityOption>()
            .FirstOrDefault(option => option.Value == _preferredQuality);

        if (preferredOption is not null)
        {
            QualityComboBox.SelectedItem = preferredOption;
        }
        else if (QualityComboBox.Items.Count > 0)
        {
            QualityComboBox.SelectedIndex = QualityComboBox.Items.Count - 1;
        }

        _preferredQuality = QualityComboBox.IsEnabled
            ? GetSelectedQualityValue()
            : _preferredQuality;
        _isRefreshingQualityOptions = false;
    }

    private void AddBitrates(string formatTag, params int[] bitrates)
    {
        foreach (var bitrate in bitrates)
        {
            AddQualityOption(
                value: $"{bitrate} kbps",
                label: $"{bitrate} kbps",
                estimatedBytes: EstimateAudioSize(formatTag, bitrate));
        }
    }

    private void AddQualityOption(
        string value,
        string label,
        long? estimatedBytes)
    {
        QualityComboBox.Items.Add(new QualityOption(
            Value: value,
            Label: label,
            EstimatedSizeText: FormatEstimatedSize(estimatedBytes)));
    }

    private long? EstimateVideoSize(int height, string targetFormat)
    {
        if (_mediaMetadata is null)
        {
            return null;
        }

        var compatibleExtensions = targetFormat switch
        {
            "mp4" or "mov" => new[] { "mp4" },
            "webm" => new[] { "webm" },
            _ => Array.Empty<string>()
        };

        var candidates = _mediaMetadata.VideoFormatEstimates
            .Where(estimate => estimate.Height == height)
            .Where(estimate => compatibleExtensions.Length == 0
                || compatibleExtensions.Contains(
                    estimate.Extension,
                    StringComparer.OrdinalIgnoreCase));
        var selectedEstimate = candidates.LastOrDefault();

        if (selectedEstimate is null)
        {
            return null;
        }

        var estimatedBytes = selectedEstimate.EstimatedBytes;

        if (!selectedEstimate.IncludesAudio
            && _mediaMetadata.SourceAudioEstimatedBytes is { } audioBytes)
        {
            estimatedBytes += audioBytes;
        }

        const double containerOverheadReserve = 1.01;
        return (long)Math.Ceiling(estimatedBytes * containerOverheadReserve);
    }

    private long? EstimateAudioSize(string formatTag, int? bitrateKbps)
    {
        if (_mediaMetadata?.Duration is not { } duration)
        {
            return _mediaMetadata?.SourceAudioEstimatedBytes;
        }

        var effectiveBitrate = bitrateKbps ?? formatTag switch
        {
            "audio:flac" => 900,
            "audio:wav" => 1411,
            _ => _mediaMetadata.SourceAudioBitrateKbps
        };

        if (effectiveBitrate is not > 0)
        {
            return _mediaMetadata.SourceAudioEstimatedBytes;
        }

        return (long)Math.Round(duration.TotalSeconds * effectiveBitrate.Value * 1000 / 8);
    }

    private static string? FormatEstimatedSize(long? estimatedBytes)
    {
        if (estimatedBytes is not > 0)
        {
            return null;
        }

        const double bytesPerKilobyte = 1024;
        const double bytesPerMegabyte = bytesPerKilobyte * 1024;
        const double bytesPerGigabyte = bytesPerMegabyte * 1024;

        return estimatedBytes.Value switch
        {
            >= (long)bytesPerGigabyte =>
                $"≈ {estimatedBytes.Value / bytesPerGigabyte:0.##} GB",
            >= (long)bytesPerMegabyte =>
                $"≈ {estimatedBytes.Value / bytesPerMegabyte:0.#} MB",
            _ => $"≈ {estimatedBytes.Value / bytesPerKilobyte:0} KB"
        };
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

    private void Window_Closed(object? sender, EventArgs e)
    {
        _linkCheckCancellation?.Cancel();
        _downloadCancellation?.Cancel();
    }
}
