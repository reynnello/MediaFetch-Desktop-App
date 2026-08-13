using System.Text;
using System.Windows;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace MediaFetch.Desktop;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    // Download button click event handler
    private async void DownloadButton_Click(object sender, RoutedEventArgs e)
    {
        var urlText = UrlTextBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(urlText))
        {
            StatusText.Text = "Status: Paste a URL first.";
            return;
        }

        if (!Uri.TryCreate(urlText, UriKind.Absolute, out var url)
                || url is null
                || (url.Scheme != Uri.UriSchemeHttp
                && url.Scheme != Uri.UriSchemeHttps))
        {
            StatusText.Text = "Status: Enter a valid HTTP or HTTPS URL.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_downloadsDirectory))
        {
            StatusText.Text = "Status: Choose a download folder first.";
            return;
        }

        Directory.CreateDirectory(_downloadsDirectory);

        var startInfo = new ProcessStartInfo
        {
            FileName = "yt-dlp",
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Add the URL and output directory as arguments
        startInfo.ArgumentList.Add("--no-playlist");
        startInfo.ArgumentList.Add("--merge-output-format");
        startInfo.ArgumentList.Add("mp4");
        startInfo.ArgumentList.Add("--output");
        startInfo.ArgumentList.Add(Path.Combine(_downloadsDirectory, "%(title)s.%(ext)s"));
        startInfo.ArgumentList.Add(url.AbsoluteUri);

        DownloadButton.IsEnabled = false;
        StatusText.Text = "Status: Downloading...";

        // Start the process and wait for it to exit
        try
        {
            using var process = Process.Start(startInfo);

            if (process is null)
            {
                StatusText.Text = "Status: Could not start yt-dlp.";
                return;
            }

            await process.WaitForExitAsync();

            StatusText.Text = process.ExitCode == 0
                ? "Status: Download complete."
                : "Status: Download failed.";
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

    private string? _downloadsDirectory;

    // Choose folder button click event handler
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

}