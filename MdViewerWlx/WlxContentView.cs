using MdViewer.Shared;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace MdViewerWlx;

/// <summary>
/// WPF-Benutzeroberfläche für das WLX-Plugin:
/// Enthält eine WebView2 zur Markdown-Darstellung und
/// eine Text-Fallback-Anzeige bei Fehlern.
/// </summary>
internal sealed class WlxContentView : Grid
{
    private static readonly Lazy<Task<CoreWebView2Environment>> SharedEnvironment = new(
        () => CoreWebView2Environment.CreateAsync());

    private readonly string _filePath;
    private readonly WebView2 _webView;
    private readonly TextBox _fallbackText;
    private readonly TextBlock _statusText;

    public WlxContentView(string filePath)
    {
        _filePath = filePath;
        _ = SharedEnvironment.Value;

        Background = new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x5F));

        _webView = new WebView2
        {
            Visibility = Visibility.Collapsed
        };

        _fallbackText = new TextBox
        {
            Text = LoadFallbackText(filePath),
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(12),
            FontFamily = new FontFamily("Consolas"),
            FontSize = 14,
            Background = Brushes.White,
            Foreground = Brushes.Black,
            BorderThickness = new Thickness(0)
        };

        _statusText = new TextBlock
        {
            Text = $"MdViewerWlx aktiv: HTML-Ansicht wird initialisiert ...",
            FontSize = 13,
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromArgb(0xCC, 0x10, 0x1F, 0x33)),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12),
            Padding = new Thickness(8)
        };

        Children.Add(_fallbackText);
        Children.Add(_webView);
        Children.Add(_statusText);
    }

    /// <summary>
    /// Initialisiert WebView2 async auf dem STA-Thread.
    /// </summary>
    public async Task<bool> InitializeAsync()
    {
        try
        {
            await EnsureWebViewReadyAsync();

            // WebView2-Settings
            _webView.CoreWebView2.Settings.IsScriptEnabled = true;
            _webView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webView.CoreWebView2.Settings.IsWebMessageEnabled = false;

            // Markdown rendern
            string html;
            if (!string.IsNullOrEmpty(_filePath) && File.Exists(_filePath))
            {
                html = await MarkdownRenderService.RenderFileAsync(_filePath);
            }
            else
            {
                html = MarkdownRenderService.Render("# MdViewer — WLX Plugin\n\nKeine Datei geladen.");
            }

            _webView.NavigateToString(html);

            // Umschalten: Fallback aus, WebView an
            _fallbackText.Visibility = Visibility.Collapsed;
            _statusText.Visibility = Visibility.Collapsed;
            _webView.Visibility = Visibility.Visible;
            return true;
        }
        catch (Exception ex)
        {
            _statusText.Text = "Text-Fallback aktiv: HTML-Ansicht konnte nicht initialisiert werden.";
            _webView.Visibility = Visibility.Collapsed;
            FileLogger.Error("WLX Content: WebView2-Initialisierung fehlgeschlagen", ex);
            return false;
        }
    }

    private async Task EnsureWebViewReadyAsync()
    {
        const string eventLoopError = "EnsureCoreWebView2Async cannot be used before the application's event loop has started running.";

        for (int attempt = 1; attempt <= 20; attempt++)
        {
            try
            {
                _statusText.Text = $"MdViewerWlx aktiv: HTML-Ansicht wird initialisiert ({attempt}/20) ...";
                var environment = await SharedEnvironment.Value;
                await _webView.EnsureCoreWebView2Async(environment);
                return;
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains(eventLoopError, StringComparison.Ordinal))
            {
                await Task.Delay(250);
            }
        }

        await _webView.EnsureCoreWebView2Async();
    }

    private static string LoadFallbackText(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "Keine Datei geladen.";

        if (!File.Exists(filePath))
            return $"Datei nicht gefunden:\n{filePath}";

        try
        {
            using var reader = new StreamReader(filePath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var text = reader.ReadToEnd();

            if (string.IsNullOrEmpty(text))
                return "(Leere Datei)";

            const int maxChars = 200_000;
            if (text.Length > maxChars)
            {
                text = text[..maxChars] + "\n\n---\nText-Fallback gekürzt. Die vollständige HTML-Ansicht folgt nach der WebView2-Initialisierung.\n";
            }

            return text;
        }
        catch (Exception ex)
        {
            return $"Fehler beim Laden des Text-Fallbacks:\n{ex.Message}";
        }
    }
}
