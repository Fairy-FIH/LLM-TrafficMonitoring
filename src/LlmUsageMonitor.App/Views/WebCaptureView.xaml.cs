using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using LlmUsageMonitor.App.ViewModels;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace LlmUsageMonitor.App.Views;

public partial class WebCaptureView : UserControl
{
    private WebCaptureViewModel? _vm;
    private bool _initialized;

    public WebCaptureView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.NavigateRequested -= OnNavigate;
            _vm.ReloadRequested -= OnReload;
            _vm.OpenExternalRequested -= OnOpenExternal;
        }

        _vm = e.NewValue as WebCaptureViewModel;

        if (_vm is not null)
        {
            _vm.NavigateRequested += OnNavigate;
            _vm.ReloadRequested += OnReload;
            _vm.OpenExternalRequested += OnOpenExternal;
        }
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "LlmUsageMonitor", "webview2");
            Directory.CreateDirectory(userDataFolder);

            Browser.CreationProperties = new CoreWebView2CreationProperties
            {
                UserDataFolder = userDataFolder
            };

            await Browser.EnsureCoreWebView2Async();
            Browser.CoreWebView2.WebResourceResponseReceived += OnResponseReceived;
            Browser.CoreWebView2.Settings.AreDevToolsEnabled = true;
            Browser.CoreWebView2.Settings.IsStatusBarEnabled = false;

            Overlay.Visibility = Visibility.Collapsed;
            if (_vm is not null)
            {
                _vm.IsReady = true;
                if (!string.IsNullOrWhiteSpace(_vm.NavigationUrl))
                    NavigateTo(_vm.NavigationUrl);
            }
        }
        catch (WebView2RuntimeNotFoundException)
        {
            SetOverlay("未检测到 WebView2 运行时。\n\n请安装 “Microsoft Edge WebView2 Runtime” 后重启本程序：\nhttps://developer.microsoft.com/microsoft-edge/webview2/");
        }
        catch (Exception ex)
        {
            SetOverlay("内置浏览器初始化失败：" + ex.Message);
        }
    }

    private void SetOverlay(string message)
    {
        Overlay.Visibility = Visibility.Visible;
        OverlayText.Text = message;
        if (_vm is not null) _vm.StatusText = message.Split('\n')[0];
    }

    private void OnNavigate(string url) => NavigateTo(url);

    private void NavigateTo(string url)
    {
        if (Browser.CoreWebView2 is null) return;
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            Browser.CoreWebView2.Navigate(uri.ToString());
    }

    private void OnReload()
    {
        if (Browser.CoreWebView2 is not null) Browser.Reload();
    }

    private void OnOpenExternal(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            if (_vm is not null) _vm.StatusText = "无法打开浏览器：" + ex.Message;
        }
    }

    private async void OnResponseReceived(object? sender, CoreWebView2WebResourceResponseReceivedEventArgs e)
    {
        try
        {
            var uri = e.Request.Uri;
            if (string.IsNullOrEmpty(uri) || !uri.StartsWith("http", StringComparison.OrdinalIgnoreCase)) return;

            var status = e.Response.StatusCode;
            if (status < 200 || status >= 300) return;

            var headers = e.Response.Headers;
            var contentType = headers.Contains("Content-Type") ? headers.GetHeader("Content-Type") : string.Empty;
            if (contentType.IndexOf("json", StringComparison.OrdinalIgnoreCase) < 0) return;

            using var stream = await e.Response.GetContentAsync();
            if (stream is null) return;

            string body;
            using (var reader = new StreamReader(stream))
            {
                body = await reader.ReadToEndAsync();
            }

            if (string.IsNullOrWhiteSpace(body) || body.Length > 1_000_000) return;

            var vm = _vm;
            if (vm is null) return;
            await vm.AddCandidateAsync(uri, e.Request.Method, status, body);
        }
        catch
        {
            // Response bodies are not always accessible (redirects, streams); ignore.
        }
    }
}
