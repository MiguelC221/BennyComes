using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using BenyComes.Services.Online;
using Microsoft.Web.WebView2.Core;

namespace BenyComes.Views;

public partial class LastFmWebLoginWindow : Window
{
    private string _apiKey = string.Empty;
    private string _apiSecret = string.Empty;
    private string _pendingToken = string.Empty;
    private bool _createFormSubmitted;
    private bool _authConfirmClicked;
    private bool _sessionCompleted;
    private CancellationTokenSource? _pollCts;

    public string AuthenticatedUsername { get; private set; } = string.Empty;
    public string StatusResultMessage { get; private set; } = string.Empty;

    public LastFmWebLoginWindow(string existingApiKey = "", string existingApiSecret = "")
    {
        InitializeComponent();
        _apiKey = existingApiKey?.Trim() ?? string.Empty;
        _apiSecret = existingApiSecret?.Trim() ?? string.Empty;
        Loaded += OnWindowLoaded;
        Closed += OnWindowClosed;
    }

    private void OnHeaderMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void OnCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        try
        {
            BrowserView?.Dispose();
        }
        catch { }
    }

    private async void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            string userDataDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MusicPlayer",
                "WebView2LastFm");
            Directory.CreateDirectory(userDataDir);

            var env = await CoreWebView2Environment.CreateAsync(null, userDataDir);
            await BrowserView.EnsureCoreWebView2Async(env);

            BrowserView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            BrowserView.CoreWebView2.Settings.AreDevToolsEnabled = false;
            BrowserView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            BrowserView.NavigationStarting += OnNavigationStarting;
            BrowserView.NavigationCompleted += OnNavigationCompleted;

            // Si ya existen API Key y Shared Secret válidas, pedimos token directo;
            // de lo contrario iniciamos el flujo con solo el usuario de Last.fm.
            if (!string.IsNullOrWhiteSpace(_apiKey) && !string.IsNullOrWhiteSpace(_apiSecret))
            {
                var (ok, token, _) = await LastFmService.Instance.RequestAuthTokenAsync(_apiKey, _apiSecret);
                if (ok && !string.IsNullOrWhiteSpace(token))
                {
                    _pendingToken = token;
                    StartSessionPolling();
                    string authUrl = $"https://www.last.fm/api/auth/?api_key={Uri.EscapeDataString(_apiKey)}&token={Uri.EscapeDataString(_pendingToken)}";
                    BrowserView.CoreWebView2.Navigate(authUrl);
                    return;
                }
            }

            // Flujo sin API Key manual: el usuario solo inicia sesión en Last.fm
            _apiKey = string.Empty;
            _apiSecret = string.Empty;
            SubtitleStatusText.Text = "Inicia sesión con tu cuenta de Last.fm";
            OverlayStatusText.Text = "Abriendo página de acceso de Last.fm...";
            BrowserView.CoreWebView2.Navigate("https://www.last.fm/login?next=/api/accounts");
        }
        catch (Exception ex)
        {
            StatusResultMessage = $"No se pudo inicializar WebView2: {ex.Message}";
            DialogResult = false;
            Close();
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (_sessionCompleted) return;
        string uri = e.Uri ?? string.Empty;

        if (uri.Contains("/api/accounts", StringComparison.OrdinalIgnoreCase) ||
            uri.Contains("/api/account/create", StringComparison.OrdinalIgnoreCase))
        {
            LoadingOverlay.Visibility = Visibility.Visible;
            OverlayStatusText.Text = "Configurando tu sesión de Scrobbling automáticamente...";
            OverlaySubStatusText.Text = "Esto tomará solo un segundo.";
        }
    }

    private async void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (_sessionCompleted || BrowserView?.CoreWebView2 == null) return;

        string url = BrowserView.Source?.ToString() ?? string.Empty;

        // 1. Si está en la pantalla de login / registro, mostrar el navegador para que el usuario ingrese su cuenta
        if (url.Contains("/login", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("/join", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("/password", StringComparison.OrdinalIgnoreCase))
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            SubtitleStatusText.Text = "Ingresa tu usuario y contraseña de Last.fm";
            return;
        }

        // 2. Si aún no tenemos API Key + Secret y estamos en /api/accounts o /api/account/create
        if (string.IsNullOrWhiteSpace(_apiKey) || string.IsNullOrWhiteSpace(_apiSecret))
        {
            if (url.Contains("/api/accounts", StringComparison.OrdinalIgnoreCase) ||
                url.Contains("/api/account/create", StringComparison.OrdinalIgnoreCase))
            {
                var (found, key, secret) = await TryExtractApiKeyAndSecretFromDomAsync();
                if (found && !string.IsNullOrWhiteSpace(key) && !string.IsNullOrWhiteSpace(secret))
                {
                    _apiKey = key;
                    _apiSecret = secret;
                    await ProceedToTokenAuthorizationAsync();
                    return;
                }

                if (url.Contains("/api/accounts", StringComparison.OrdinalIgnoreCase))
                {
                    // No tenía claves creadas en /api/accounts -> ir a /api/account/create
                    BrowserView.CoreWebView2.Navigate("https://www.last.fm/api/account/create");
                    return;
                }

                if (url.Contains("/api/account/create", StringComparison.OrdinalIgnoreCase))
                {
                    if (!_createFormSubmitted)
                    {
                        _createFormSubmitted = true;
                        bool autoSubmitted = await TryAutoFillAndSubmitApiAccountFormAsync();
                        if (!autoSubmitted)
                        {
                            LoadingOverlay.Visibility = Visibility.Collapsed;
                            SubtitleStatusText.Text = "Pulsa 'Submit' para vincular tu usuario con BenyComes";
                        }
                        else
                        {
                            // Si después de 2.5s sigue en la página (ej. requiere confirmación visual), mostrar el WebView
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(2500);
                                Dispatcher.Invoke(() =>
                                {
                                    if (!_sessionCompleted && string.IsNullOrWhiteSpace(_apiKey))
                                    {
                                        LoadingOverlay.Visibility = Visibility.Collapsed;
                                        SubtitleStatusText.Text = "Confirma la vinculación de tu cuenta en pantalla";
                                    }
                                });
                            });
                        }
                    }
                    else
                    {
                        LoadingOverlay.Visibility = Visibility.Collapsed;
                        SubtitleStatusText.Text = "Pulsa 'Submit' para completar la vinculación";
                    }
                    return;
                }
            }
            else if (url.Contains("last.fm", StringComparison.OrdinalIgnoreCase) &&
                     !url.Contains("/api/", StringComparison.OrdinalIgnoreCase))
            {
                // El usuario inició sesión y fue redirigido al home o perfil -> redirigir a /api/accounts
                bool isLoggedIn = await CheckIfUserLoggedInDomAsync();
                if (isLoggedIn)
                {
                    LoadingOverlay.Visibility = Visibility.Visible;
                    OverlayStatusText.Text = "Cuenta detectada, vinculando Scrobbling...";
                    BrowserView.CoreWebView2.Navigate("https://www.last.fm/api/accounts");
                    return;
                }
            }
        }

        // 3. Si estamos en /api/auth, intentar confirmar automáticamente o dejar que el usuario haga clic en "Yes, allow access"
        if (url.Contains("/api/auth", StringComparison.OrdinalIgnoreCase))
        {
            SubtitleStatusText.Text = "Autorizando Scrobbling para tu cuenta de Last.fm...";
            if (!_authConfirmClicked)
            {
                _authConfirmClicked = true;
                bool clicked = await TryAutoClickAllowAccessAsync();
                if (!clicked)
                {
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    SubtitleStatusText.Text = "Haz clic en 'Yes, allow access' para activar el Scrobbling";
                }
                else
                {
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(2000);
                        Dispatcher.Invoke(() =>
                        {
                            if (!_sessionCompleted)
                            {
                                LoadingOverlay.Visibility = Visibility.Collapsed;
                            }
                        });
                    });
                }
            }
            else
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
                await TryVerifySessionOnceAsync();
            }
            return;
        }

        LoadingOverlay.Visibility = Visibility.Collapsed;
    }

    private async Task ProceedToTokenAuthorizationAsync()
    {
        LoadingOverlay.Visibility = Visibility.Visible;
        OverlayStatusText.Text = "Autorizando sesión de Scrobbling en Last.fm...";
        SubtitleStatusText.Text = "Conectando cuenta de Last.fm...";

        var (ok, token, msg) = await LastFmService.Instance.RequestAuthTokenAsync(_apiKey, _apiSecret);
        if (!ok || string.IsNullOrWhiteSpace(token))
        {
            LoadingOverlay.Visibility = Visibility.Collapsed;
            SubtitleStatusText.Text = msg;
            return;
        }

        _pendingToken = token;
        _authConfirmClicked = false;
        StartSessionPolling();

        string authUrl = $"https://www.last.fm/api/auth/?api_key={Uri.EscapeDataString(_apiKey)}&token={Uri.EscapeDataString(_pendingToken)}";
        BrowserView.CoreWebView2.Navigate(authUrl);
    }

    private void StartSessionPolling()
    {
        _pollCts?.Cancel();
        _pollCts?.Dispose();
        _pollCts = new CancellationTokenSource();
        var ct = _pollCts.Token;

        _ = Task.Run(async () =>
        {
            while (!ct.IsCancellationRequested && !_sessionCompleted)
            {
                await Task.Delay(1800, ct);
                if (ct.IsCancellationRequested || _sessionCompleted) break;
                await TryVerifySessionOnceAsync();
            }
        }, ct);
    }

    private async Task TryVerifySessionOnceAsync()
    {
        if (_sessionCompleted ||
            string.IsNullOrWhiteSpace(_apiKey) ||
            string.IsNullOrWhiteSpace(_apiSecret) ||
            string.IsNullOrWhiteSpace(_pendingToken))
        {
            return;
        }

        var (sessOk, user, sk, sessMsg) = await LastFmService.Instance
            .CompleteAuthSessionAsync(_apiKey, _apiSecret, _pendingToken);

        if (sessOk && !string.IsNullOrWhiteSpace(sk))
        {
            _sessionCompleted = true;
            _pollCts?.Cancel();

            AuthenticatedUsername = user;
            StatusResultMessage = sessMsg;

            Dispatcher.Invoke(() =>
            {
                try
                {
                    DialogResult = true;
                    Close();
                }
                catch { }
            });
        }
    }

    private async Task<(bool Found, string Key, string Secret)> TryExtractApiKeyAndSecretFromDomAsync()
    {
        try
        {
            const string script = @"
(() => {
    const text = document.body ? document.body.innerText : '';
    const keyMatch = text.match(/API\s*key[\s:\r\n]+([a-f0-9]{32})/i);
    const secMatch = text.match(/Shared\s*secret[\s:\r\n]+([a-f0-9]{32})/i);
    if (keyMatch && secMatch) {
        return JSON.stringify({ found: true, key: keyMatch[1], secret: secMatch[1] });
    }
    return JSON.stringify({ found: false, key: '', secret: '' });
})();";
            string rawJson = await BrowserView.CoreWebView2.ExecuteScriptAsync(script);
            string? unescaped = JsonSerializer.Deserialize<string>(rawJson);
            if (!string.IsNullOrWhiteSpace(unescaped))
            {
                using var doc = JsonDocument.Parse(unescaped);
                bool found = doc.RootElement.TryGetProperty("found", out var fp) && fp.GetBoolean();
                string key = doc.RootElement.TryGetProperty("key", out var kp) ? (kp.GetString() ?? "") : "";
                string secret = doc.RootElement.TryGetProperty("secret", out var sp) ? (sp.GetString() ?? "") : "";
                return (found, key, secret);
            }
        }
        catch { }
        return (false, string.Empty, string.Empty);
    }

    private async Task<bool> TryAutoFillAndSubmitApiAccountFormAsync()
    {
        try
        {
            const string script = @"
(() => {
    const appName = document.querySelector('input[name=""application_name""], #id_application_name');
    if (!appName) return false;

    if (!appName.value || appName.value.trim() === '') {
        appName.value = 'BenyComes Music Player';
        appName.dispatchEvent(new Event('input', { bubbles: true }));
    }

    const appDesc = document.querySelector('textarea[name=""application_description""], #id_application_description');
    if (appDesc && (!appDesc.value || appDesc.value.trim() === '')) {
        appDesc.value = 'Desktop Hi-Fi Music Player & osu! Scrobbler';
        appDesc.dispatchEvent(new Event('input', { bubbles: true }));
    }

    const emailInput = document.querySelector('input[name=""email""], #id_email, input[type=""email""]');
    if (emailInput && (!emailInput.value || emailInput.value.trim() === '')) {
        const userLink = document.querySelector('a.auth-link, a[href^=""/user/""]');
        const uname = userLink ? (userLink.getAttribute('href') || '').replace('/user/', '').replace(/\//g, '') : 'user';
        emailInput.value = (uname || 'benycomes') + '@users.noreply.github.com';
        emailInput.dispatchEvent(new Event('input', { bubbles: true }));
    }

    const form = appName.closest('form');
    const submitBtn = form ? form.querySelector('button[type=""submit""], input[type=""submit""]') : null;
    if (submitBtn) {
        submitBtn.click();
        return true;
    }
    if (form) {
        form.submit();
        return true;
    }
    return false;
})();";
            string raw = await BrowserView.CoreWebView2.ExecuteScriptAsync(script);
            return string.Equals(raw?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryAutoClickAllowAccessAsync()
    {
        try
        {
            const string script = @"
(() => {
    const confirmBtn = document.querySelector('button[name=""confirm""], form[action*=""/api/auth""] button[type=""submit""], button.btn-primary[type=""submit""]');
    if (confirmBtn) {
        confirmBtn.click();
        return true;
    }
    return false;
})();";
            string raw = await BrowserView.CoreWebView2.ExecuteScriptAsync(script);
            return string.Equals(raw?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> CheckIfUserLoggedInDomAsync()
    {
        try
        {
            const string script = @"
(() => {
    const userLink = document.querySelector('a.auth-link, .site-auth--logged-in, a.header-avatar-inner, form[action*=""/logout""]');
    return !!userLink;
})();";
            string raw = await BrowserView.CoreWebView2.ExecuteScriptAsync(script);
            return string.Equals(raw?.Trim(), "true", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
