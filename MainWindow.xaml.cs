using System.Windows;
using System.Windows.Forms;
using MediaFetch.Desktop.Models;
using MediaFetch.Desktop.Services;

namespace MediaFetch.Desktop;

public partial class MainWindow : Window
{
    private readonly YtDlpService _ytDlpService = new();
    private string? _downloadsDirectory;
    private CancellationTokenSource? _linkCheckCancellation;
    private MediaMetadata? _mediaMetadata;

    public MainWindow()
    {
        InitializeComponent();
        FormatComboBox.SelectionChanged += FormatComboBox_SelectionChanged;
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
        MediaInfoText.Text = "Source audio: waiting for link";
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
            UpdateMediaInfo(metadata);
            StatusText.Text = $"Status: Found {metadata.VideoHeights.Count} video qualities.";
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
    }

    private string? GetSelectedFormatTag()
    {
        return (FormatComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)
            ?.Tag
            ?.ToString();
    }

    private static bool RequiresAudioBitrate(string formatTag)
    {
        return formatTag is "audio:mp3" or "audio:m4a" or "audio:opus";
    }

    private void RefreshQualityOptions()
    {
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

        if (QualityComboBox.Items.Count > 0)
        {
            QualityComboBox.SelectedIndex = QualityComboBox.Items.Count - 1;
        }
    }

    private void AddBitrates(params int[] bitrates)
    {
        foreach (var bitrate in bitrates)
        {
            QualityComboBox.Items.Add($"{bitrate} kbps");
        }
    }

    private void UpdateMediaInfo(MediaMetadata metadata)
    {
        var source = metadata.Source ?? "Unknown source";

        if (metadata.SourceAudioCodec is null)
        {
            MediaInfoText.Text = $"{source} · Source audio: not detected";
            return;
        }

        var bitrate = metadata.SourceAudioBitrateKbps is null
            ? "unknown bitrate"
            : $"{metadata.SourceAudioBitrateKbps.Value:0} kbps";
        var extension = metadata.SourceAudioExtension?.ToUpperInvariant() ?? "unknown container";

        MediaInfoText.Text =
            $"{source} · Source audio: {metadata.SourceAudioCodec}, {extension}, {bitrate}";
    }
}
