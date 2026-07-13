using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ArtnetNode.Core;
using Microsoft.Extensions.Logging;

namespace ArtnetNode.Core
{
    public class ArtnetHttpServer : IDisposable
    {
        private HttpListener? _listener;
        private CancellationTokenSource? _cts;
        private readonly ArtnetNodeEngine _engine;
        private readonly ILogger _logger;
        private readonly ArtnetOptions _options;
        private readonly DateTime _startTime;
        private bool _isRunning;
        private string _htmlContent = "";
        private int _eventId;

        public int Port { get; private set; } = 8080;
        public bool IsRunning => _isRunning;

        public ArtnetHttpServer(ArtnetNodeEngine engine, ILogger logger, ArtnetOptions options)
        {
            _engine = engine;
            _logger = logger;
            _options = options;
            _startTime = DateTime.Now;
            LoadHtml();
        }

        private void LoadHtml()
        {
            try
            {
                string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "dashboard.html");
                if (File.Exists(htmlPath))
                {
                    _htmlContent = File.ReadAllText(htmlPath);
                    _logger.LogInformation("Dashboard HTML caricata da file esterno");
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Impossibile caricare dashboard.html, uso fallback");
            }

            _htmlContent = @"<!DOCTYPE html>
<html>
<head><title>Art-Net Node</title></head>
<body>
<h1>Art-Net Node</h1>
<p>Dashboard non disponibile. Assicurarsi che dashboard.html sia presente nella directory dell'applicazione.</p>
</body>
</html>";
        }

        public void Start(int preferredPort = 8080)
        {
            if (_isRunning) return;

            Port = preferredPort;
            _cts = new CancellationTokenSource();

            int attempts = 0;
            while (attempts < 5)
            {
                try
                {
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://*:{Port}/");
                    _listener.Start();
                    _isRunning = true;
                    break;
                }
                catch (HttpListenerException ex) when (ex.ErrorCode == 5)
                {
                    _listener?.Close();
                    _listener = new HttpListener();
                    _listener.Prefixes.Add($"http://localhost:{Port}/");
                    _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");

                    if (IPAddress.TryParse(_engine.BindIpAddress, out var ip) &&
                        !ip.Equals(IPAddress.Any) && !ip.Equals(IPAddress.IPv6Any))
                    {
                        try
                        {
                            _listener.Prefixes.Add($"http://{_engine.BindIpAddress}:{Port}/");
                        }
                        catch { }
                    }

                    try
                    {
                        _listener.Start();
                        _isRunning = true;
                        _logger.LogWarning("Server HTTP avviato su localhost:{Port} (Privilegi di rete limitati)", Port);
                        break;
                    }
                    catch (Exception innerEx)
                    {
                        _logger.LogError(innerEx, "Tentativo porta {Port} fallito", Port);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Impossibile avviare sulla porta {Port}", Port);
                }

                Port++;
                attempts++;
            }

            if (_isRunning && _listener != null)
            {
                _logger.LogInformation("Web Dashboard attiva su http://localhost:{Port}/", Port);
                Task.Run(() => ListenLoop(_cts.Token));
            }
            else
            {
                _logger.LogError("Impossibile avviare il server HTTP");
            }
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _isRunning = false;
            _cts?.Cancel();
            try
            {
                _listener?.Stop();
                _listener?.Close();
            }
            catch { }

            _cts?.Dispose();
            _cts = null;
            _listener = null;
            _logger.LogInformation("Server HTTP arrestato");
        }

        public void Dispose()
        {
            Stop();
        }

        private async Task ListenLoop(CancellationToken token)
        {
            while (!token.IsCancellationRequested && _isRunning && _listener != null)
            {
                try
                {
                    HttpListenerContext context = await _listener.GetContextAsync();
                    _ = Task.Run(() => HandleRequestAsync(context, token), token);
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!token.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Errore del server HTTP");
                    }
                }
            }
        }

        private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken token)
        {
            HttpListenerRequest request = context.Request;
            HttpListenerResponse response = context.Response;

            string corsOrigin = GetCorsOrigin(request);
            if (!string.IsNullOrEmpty(corsOrigin))
            {
                response.Headers.Add("Access-Control-Allow-Origin", corsOrigin);
            }
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type, Authorization");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = (int)HttpStatusCode.OK;
                response.Close();
                return;
            }

            if (!string.IsNullOrEmpty(_options.ApiToken) && !IsAuthenticated(request))
            {
                response.StatusCode = (int)HttpStatusCode.Unauthorized;
                byte[] buffer = Encoding.UTF8.GetBytes("{\"error\": \"Unauthorized\"}");
                response.ContentType = "application/json";
                response.ContentLength64 = buffer.Length;
                await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                response.OutputStream.Close();
                return;
            }

            try
            {
                string urlPath = request.Url?.AbsolutePath ?? "/";

                if (request.HttpMethod == "GET" && urlPath == "/")
                {
                    await ServeHtmlAsync(response);
                    return;
                }

                if (request.HttpMethod == "GET" && urlPath == "/api/status")
                {
                    await ServeStatusAsync(response);
                    return;
                }

                if (request.HttpMethod == "GET" && urlPath == "/api/dmx")
                {
                    await ServeDmxAsync(request, response);
                    return;
                }

                if (request.HttpMethod == "GET" && urlPath == "/api/events")
                {
                    await ServeSseAsync(response, token);
                    return;
                }

                if (request.HttpMethod == "GET" && urlPath == "/api/universes")
                {
                    await ServeUniversesAsync(response);
                    return;
                }

                if (request.HttpMethod == "POST" && urlPath == "/api/blackout")
                {
                    await HandleBlackoutAsync(request, response);
                    return;
                }

                if (request.HttpMethod == "POST" && urlPath == "/api/override/set")
                {
                    await HandleOverrideSetAsync(request, response);
                    return;
                }

                if (request.HttpMethod == "POST" && urlPath == "/api/override/clear-channel")
                {
                    await HandleOverrideClearChannelAsync(request, response);
                    return;
                }

                if (request.HttpMethod == "POST" && urlPath == "/api/override/clear")
                {
                    await HandleOverrideClearAllAsync(response);
                    return;
                }

                response.StatusCode = (int)HttpStatusCode.NotFound;
                byte[] notFoundBuffer = Encoding.UTF8.GetBytes("{\"error\": \"Not Found\"}");
                response.ContentType = "application/json";
                response.ContentLength64 = notFoundBuffer.Length;
                await response.OutputStream.WriteAsync(notFoundBuffer, 0, notFoundBuffer.Length);
                response.OutputStream.Close();
            }
            catch (Exception ex)
            {
                try
                {
                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    byte[] buffer = Encoding.UTF8.GetBytes($"{{\"error\": \"{ex.Message}\"}}");
                    response.ContentType = "application/json";
                    response.ContentLength64 = buffer.Length;
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    response.OutputStream.Close();
                }
                catch { }
            }
        }

        private bool IsAuthenticated(HttpListenerRequest request)
        {
            string? authHeader = request.Headers["Authorization"];
            return ApiKeyAuthHandler.ValidateToken(authHeader, _options.ApiToken);
        }

        private string GetCorsOrigin(HttpListenerRequest request)
        {
            string origin = request.Headers["Origin"] ?? "";
            if (string.IsNullOrEmpty(origin)) return "";

            var origins = _options.CorsOrigins.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var pattern in origins)
            {
                if (pattern == "*") return "*";
                if (pattern.Contains("*"))
                {
                    string regexPattern = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
                    if (System.Text.RegularExpressions.Regex.IsMatch(origin, regexPattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                    {
                        return origin;
                    }
                }
                else if (origin.Equals(pattern, StringComparison.OrdinalIgnoreCase))
                {
                    return origin;
                }
            }
            return "";
        }

        private async Task ServeHtmlAsync(HttpListenerResponse response)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(_htmlContent);
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "text/html; charset=utf-8";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }

        private async Task ServeStatusAsync(HttpListenerResponse response)
        {
            var interfacesList = _engine.ActiveInterfaces.Select(i => new {
                universe = i.Config.Universe,
                driverType = i.Config.DriverType,
                comPort = i.Config.ComPort,
                isConnected = i.Interface.IsConnected,
                status = i.ConnectionStatus,
                isReconnecting = i.IsReconnecting,
                reconnectAttempt = i.ReconnectAttempt
            }).ToList();

            var statusData = new {
                isRunning = _engine.IsRunning,
                connectionStatus = _engine.ConnectionStatus,
                totalPackets = _engine.TotalPacketsReceived,
                lastSenderIp = _engine.LastSenderIpAddress,
                uptimeSeconds = (int)(DateTime.Now - _startTime).TotalSeconds,
                blackoutActive = _engine.BlackoutActive,
                manualOverrideActive = _engine.ManualOverrideActive,
                interfaces = interfacesList,
                httpPort = Port
            };

            await WriteJsonResponse(response, statusData);
        }

        private async Task ServeDmxAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            string uniStr = request.QueryString["universe"] ?? "";
            int universe = 0;
            if (!string.IsNullOrEmpty(uniStr) && int.TryParse(uniStr, out int parsedUni))
            {
                universe = parsedUni;
            }
            else if (_engine.ActiveInterfaces.Count > 0)
            {
                universe = _engine.ActiveInterfaces[0].Config.Universe;
            }

            byte[] dmxData = _engine.GetCurrentMergedDmx(universe);

            var responseData = new {
                universe = universe,
                dmx = dmxData,
                overridden = _engine.ManualOverrideFlags
            };

            await WriteJsonResponse(response, responseData);
        }

        private async Task ServeSseAsync(HttpListenerResponse response, CancellationToken requestToken)
        {
            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "text/event-stream";
            response.Headers.Add("Cache-Control", "no-cache");
            response.Headers.Add("Connection", "keep-alive");

            _engine.StatusChanged += OnEngineStatusChanged;
            _engine.DmxReceived += OnEngineDmxReceived;
            _engine.ErrorOccurred += OnEngineError;
            _engine.LogMessage += OnEngineLog;

            try
            {
                await SendSseEvent(response, "connected", new { time = DateTime.Now });

                while (!requestToken.IsCancellationRequested && _isRunning)
                {
                    await Task.Delay(1000, requestToken);
                    try
                    {
                        await SendSseEvent(response, "heartbeat", new { time = DateTime.Now });
                    }
                    catch
                    {
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Errore SSE");
            }
            finally
            {
                _engine.StatusChanged -= OnEngineStatusChanged;
                _engine.DmxReceived -= OnEngineDmxReceived;
                _engine.ErrorOccurred -= OnEngineError;
                _engine.LogMessage -= OnEngineLog;
                try { response.OutputStream.Close(); } catch { }
            }
        }

        private async void OnEngineStatusChanged(object? sender, EventArgs e)
        {
            if (!_isRunning) return;
            try
            {
                await SendSseEventLocal("status", new { time = DateTime.Now });
            }
            catch { }
        }

        private async void OnEngineDmxReceived(object? sender, DmxEventArgs e)
        {
            if (!_isRunning) return;
            try
            {
                await SendSseEventLocal("dmx", new { universe = e.Universe, senderIp = e.SenderIp });
            }
            catch { }
        }

        private async void OnEngineError(object? sender, string error)
        {
            if (!_isRunning) return;
            try
            {
                await SendSseEventLocal("error", new { message = error, time = DateTime.Now });
            }
            catch { }
        }

        private async void OnEngineLog(object? sender, string message)
        {
            if (!_isRunning) return;
            try
            {
                await SendSseEventLocal("log", new { message, time = DateTime.Now });
            }
            catch { }
        }

        private async Task SendSseEvent(HttpListenerResponse response, string eventName, object data)
        {
            string json = JsonSerializer.Serialize(data);
            string sse = $"event: {eventName}\ndata: {json}\n\n";
            byte[] buffer = Encoding.UTF8.GetBytes(sse);
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            await response.OutputStream.FlushAsync();
        }

        private async Task SendSseEventLocal(string eventName, object data)
        {
        }

        private async Task ServeUniversesAsync(HttpListenerResponse response)
        {
            var universes = _engine.ActiveInterfaces
                .Select(i => new {
                    universe = i.Config.Universe,
                    driverType = i.Config.DriverType,
                    isConnected = i.Interface.IsConnected,
                    status = i.ConnectionStatus
                })
                .ToList();

            await WriteJsonResponse(response, new { universes });
        }

        private async Task HandleBlackoutAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                string body = await reader.ReadToEndAsync();
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    bool active = doc.RootElement.GetProperty("active").GetBoolean();
                    _engine.BlackoutActive = active;
                }
            }

            await WriteJsonResponse(response, new { success = true });
        }

        private async Task HandleOverrideSetAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                string body = await reader.ReadToEndAsync();
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    int channel = doc.RootElement.GetProperty("channel").GetInt32();
                    byte value = (byte)doc.RootElement.GetProperty("value").GetInt16();

                    int universe = 0;
                    if (doc.RootElement.TryGetProperty("universe", out var uniProp))
                    {
                        universe = uniProp.GetInt32();
                    }
                    else if (_engine.ActiveInterfaces.Count > 0)
                    {
                        universe = _engine.ActiveInterfaces[0].Config.Universe;
                    }

                    _engine.SetManualOverride(universe, channel - 1, value);
                }
            }

            await WriteJsonResponse(response, new { success = true });
        }

        private async Task HandleOverrideClearChannelAsync(HttpListenerRequest request, HttpListenerResponse response)
        {
            using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            {
                string body = await reader.ReadToEndAsync();
                using (JsonDocument doc = JsonDocument.Parse(body))
                {
                    int channel = doc.RootElement.GetProperty("channel").GetInt32();
                    int universe = 0;
                    if (doc.RootElement.TryGetProperty("universe", out var uniProp))
                    {
                        universe = uniProp.GetInt32();
                    }
                    else if (_engine.ActiveInterfaces.Count > 0)
                    {
                        universe = _engine.ActiveInterfaces[0].Config.Universe;
                    }

                    _engine.ClearManualOverrideChannel(universe, channel - 1);
                }
            }

            await WriteJsonResponse(response, new { success = true });
        }

        private async Task HandleOverrideClearAllAsync(HttpListenerResponse response)
        {
            _engine.ClearManualOverrides();
            await WriteJsonResponse(response, new { success = true });
        }

        private async Task WriteJsonResponse(HttpListenerResponse response, object data)
        {
            string jsonString = JsonSerializer.Serialize(data);
            byte[] buffer = Encoding.UTF8.GetBytes(jsonString);

            response.StatusCode = (int)HttpStatusCode.OK;
            response.ContentType = "application/json";
            response.ContentLength64 = buffer.Length;
            await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
            response.OutputStream.Close();
        }
    }
}
