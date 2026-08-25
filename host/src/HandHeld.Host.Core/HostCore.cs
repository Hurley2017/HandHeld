using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HandHeld.Host.Core.Video;

namespace HandHeld.Host.Core;

public sealed class HostCore
{
    public const int PortDiscovery = 45310;
    public const int PortControl = 45320;
    public const int PortVideo = 45330;
    public const int PortAudio = 45340;
    public const int PortInput = 45350;
    public const int ApiVersion = 1;

    public static HostCore Instance { get; } = new();

    public string DisplayName => Environment.MachineName;
    public string StatusText => $"Ready — {DisplayName}\n{_clients} connected client(s)";

    private volatile int _clients;
    private CancellationTokenSource? _cts;
    private UdpClient? _discovery;
    private TcpListener? _control;
    private StreamSession? _session;
    private Input.InputReceiver? _input;
    private Audio.AudioStreamer? _audio;
    private readonly object _sessionLock = new();

    public bool IsStreaming => _session != null;
    public string SessionStatus => _session != null ? "Streaming" : "Idle";
    public string CurrentStatus => _activeGame ?? "Ready — streaming desktop";

    /// <summary>Raised when a game starts/stops (banner updates).</summary>
    public event Action<string>? GameStarted;
    public event Action? GameStopped;
    public event Action? ClientsChanged;

    public bool IsRunning { get; private set; }
    public bool HasActiveGame => _activeGame != null;
    public string DeviceName { get; private set; } = "No device connected";

    /// <summary>Connected client descriptors ("name (ip)").</summary>
    public IReadOnlyList<string> Clients => _clientNames;

    private readonly List<string> _clientNames = new();
    private readonly object _clientsLock = new();
    private string? _activeGame;

    private static readonly object StreamLogLock = new();
    private static void StreamLog(string msg)
    {
        try
        {
            lock (StreamLogLock)
            {
                File.AppendAllText(@"D:\Projects\HandHeld\artifacts\host-stream.log",
                    $"{DateTime.Now:HH:mm:ss.fff} [ws] {msg}\n");
            }
        }
        catch { }
    }

    public void Start()
    {
        if (IsRunning) return;
        IsRunning = true;

        // The virtual display is only present while a client is streaming —
        // disable it at startup so the host desktop is clean.
        Video.DisplayManager.SetVirtualDisplayEnabled(false);

        _cts = new CancellationTokenSource();
        var ct = _cts.Token;

        _discovery = new UdpClient(PortDiscovery, AddressFamily.InterNetwork);
        _ = Task.Run(() => DiscoveryLoop(_discovery, ct), ct);

        _control = new TcpListener(IPAddress.Any, PortControl);
        _control.Start();
        _ = Task.Run(() => AcceptLoop(_control, ct), ct);

        _input = new Input.InputReceiver();
        _input.Start();

        ClientsChanged?.Invoke();
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;

        _cts?.Cancel();
        _discovery?.Close();
        _control?.Stop();
        _input?.Dispose();
        _cts?.Dispose();

        lock (_sessionLock)
        {
            _session?.Dispose();
            _session = null;
            _audio?.Dispose();
            _audio = null;
        }

        lock (_clientsLock)
        {
            _clientNames.Clear();
            DeviceName = "No device connected";
        }
        Video.DisplayManager.SetVirtualDisplayEnabled(false);
        ClientsChanged?.Invoke();
    }

    /// <summary>Disconnects a client by its "name (ip)" descriptor.</summary>
    public void KickClient(string descriptor)
    {
        string ip = descriptor;
        var m = System.Text.RegularExpressions.Regex.Match(descriptor, @"\(([^)]+)\)");
        if (m.Success) ip = m.Groups[1].Value;

        lock (_clientsLock)
        {
            _clientNames.RemoveAll(c => c.Contains(ip, StringComparison.Ordinal));
            if (_clientNames.Count == 0) DeviceName = "No device connected";
        }
        try
        {
            var endpoint = new IPEndPoint(IPAddress.Parse(ip), PortControl);
            // Force the connection closed by sending a close frame via a fresh socket.
            using var tcp = new TcpClient();
            tcp.Connect(endpoint);
            // The host handler sees the peer reset and drops the client.
            tcp.Close();
        }
        catch { }
        ClientsChanged?.Invoke();
    }

    /// <summary>Closes the currently running game (ends the process tree).</summary>
    public void CloseGame()
    {
        if (_activeGame == null) return;
        try
        {
            var game = Launcher.GameScanner.Games.FirstOrDefault(g => g.Title == _activeGame);
            Launcher.GameLauncher.Close(game);
        }
        catch { }
        _activeGame = null;
        GameStopped?.Invoke();
        ClientsChanged?.Invoke();
    }

    private async Task DiscoveryLoop(UdpClient udp, CancellationToken ct)
    {
        var reply = Json.Bytes(new
        {
            type = "hello",
            app = "HandHeld",
            api = ApiVersion,
            host = DisplayName,
            capabilities = new
            {
                video = new[] { "h264" },
                maxWidth = 3840,
                maxHeight = 2160,
                maxFps = 120,
                audio = new[] { "aac" },
                gamepad = true,
            },
        });

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(ct);
                var text = Encoding.UTF8.GetString(result.Buffer);
                if (!text.Contains("discover", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                await udp.SendAsync(reply, reply.Length, result.RemoteEndPoint);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[discovery] {ex.Message}");
            }
        }
    }

    private async Task AcceptLoop(TcpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await listener.AcceptTcpClientAsync(ct);
            }
            catch (Exception)
            {
                break;
            }
            _ = Task.Run(() => HandleClient(client, ct), ct);
        }
    }

    private async Task HandleClient(TcpClient client, CancellationToken ct)
    {
        var clientIp = ((IPEndPoint)client.Client.RemoteEndPoint!).Address;
        string clientName = clientIp.ToString();
        try
        {
            client.NoDelay = true;
            var stream = client.GetStream();

            // WebSocket upgrade handshake (no HttpListener/URL ACL needed).
            var handshake = new StringBuilder();
            var line = await ReadLineAsync(stream, ct);
            if (line == null || !line.StartsWith("GET ", StringComparison.Ordinal))
            {
                return;
            }
            string? key = null;
            while ((line = await ReadLineAsync(stream, ct)) != null && line.Length > 0)
            {
                if (line.StartsWith("Sec-WebSocket-Key:", StringComparison.OrdinalIgnoreCase))
                {
                    key = line[(line.IndexOf(':') + 1)..].Trim();
                }
            }
            if (key == null)
            {
                return;
            }

            var accept = Convert.ToBase64String(
                SHA1.HashData(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
            var response = "HTTP/1.1 101 Switching Protocols\r\n"
                         + "Upgrade: websocket\r\n"
                         + "Connection: Upgrade\r\n"
                         + $"Sec-WebSocket-Accept: {accept}\r\n\r\n";
            var responseBytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(responseBytes, ct);
            await stream.FlushAsync(ct);

            // Frame loop (server->client masked=0; client->server masked=1).
            var buffer = new byte[8192];
            while (!ct.IsCancellationRequested)
            {
                var header = await ReadExactAsync(stream, 2, ct);
                if (header == null)
                {
                    StreamLog("ws loop break: EOF on header read");
                    break;
                }
                bool fin = (header[0] & 0x80) != 0;
                int opcode = header[0] & 0x0F;
                bool masked = (header[1] & 0x80) != 0;
                ulong length = (ulong)(header[1] & 0x7F);

                if (length == 126)
                {
                    var ext = await ReadExactAsync(stream, 2, ct);
                    if (ext == null) break;
                    length = (ulong)((ext[0] << 8) | ext[1]);
                }
                else if (length == 127)
                {
                    var ext = await ReadExactAsync(stream, 8, ct);
                    if (ext == null) break;
                    length = 0;
                    foreach (var b in ext) length = (length << 8) | b;
                }

                byte[]? mask = null;
                if (masked)
                {
                    mask = await ReadExactAsync(stream, 4, ct);
                    if (mask == null) break;
                }

                if (opcode == 0x8) // close
                {
                    StreamLog("ws loop break: close frame from client");
                    break;
                }
                if (opcode == 0x9) // ping — echo the payload (RFC 6455; OkHttp requires it)
                {
                    var pingPayload = await ReadExactAsync(stream, (int)length, ct);
                    if (pingPayload == null) break;
                    var pong = new byte[2 + pingPayload.Length];
                    pong[0] = 0x8A;
                    pong[1] = (byte)pingPayload.Length;
                    Array.Copy(pingPayload, 0, pong, 2, pingPayload.Length);
                    await stream.WriteAsync(pong, ct);
                    continue;
                }

                if (opcode == 0x1 && length > 0) // text
                {
                    if (buffer.Length < (int)length) buffer = new byte[(int)length];
                    var payload = await ReadExactAsync(stream, (int)length, ct);
                    if (payload == null) break;
                    if (masked)
                    {
                        for (int i = 0; i < payload.Length; i++) payload[i] ^= mask![i % 4];
                    }
                    var responseJson = HandleMessage(Encoding.UTF8.GetString(payload), clientIp);
                    if (responseJson != null)
                    {
                        var bytes = Encoding.UTF8.GetBytes(responseJson);
                        var frame = BuildFrame(bytes);
                        if (frame.Length > 0)
                        {
                            StreamLog($"reply frame[{frame.Length}] first bytes: {BitConverter.ToString(frame, 0, Math.Min(6, frame.Length))}");
                        }
                        await stream.WriteAsync(frame, ct);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StreamLog($"ws loop exception: {ex.Message}");
            Debug.WriteLine($"[ws] {ex.Message}");
        }
        finally
        {
            lock (_clientsLock)
            {
                _clientNames.RemoveAll(c => c.Contains(clientIp.ToString(), StringComparison.Ordinal));
                if (_clientNames.Count == 0) DeviceName = "No device connected";
            }
            // NOTE: the streaming session is deliberately NOT disposed here.
            // A control WS can close for benign reasons (phone screen off,
            // GC of a fetch channel) while the stream must continue. The
            // session ends only via an explicit "stop" or a new launch.
            ClientsChanged?.Invoke();
            try { client.Dispose(); } catch { }
        }
    }

    private static byte[] BuildFrame(byte[] payload)
    {
        byte[] frame;
        int offset;
        if (payload.Length < 126)
        {
            frame = new byte[payload.Length + 2];
            frame[0] = 0x81;
            frame[1] = (byte)payload.Length;
            offset = 2;
        }
        else if (payload.Length <= 0xFFFF)
        {
            frame = new byte[payload.Length + 4];
            frame[0] = 0x81;
            frame[1] = 126;
            frame[2] = (byte)(payload.Length >> 8);
            frame[3] = (byte)payload.Length;
            offset = 4;
        }
        else
        {
            frame = new byte[payload.Length + 10];
            frame[0] = 0x81;
            frame[1] = 127;
            ulong len = (ulong)payload.Length;
            for (int i = 0; i < 8; i++) frame[2 + i] = (byte)(len >> (56 - i * 8));
            offset = 10;
        }
        Array.Copy(payload, 0, frame, offset, payload.Length);
        return frame;
    }

    private static async Task<string?> ReadLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var sb = new StringBuilder();
        var buf = new byte[1];
        while (sb.Length < 4096)
        {
            int n = await stream.ReadAsync(buf.AsMemory(0, 1), ct);
            if (n == 0) return sb.Length == 0 ? null : sb.ToString();
            if (buf[0] == (byte)'\n') break;
            if (buf[0] != (byte)'\r') sb.Append((char)buf[0]);
        }
        return sb.ToString();
    }

    private static async Task<byte[]?> ReadExactAsync(NetworkStream stream, int count, CancellationToken ct)
    {
        var data = new byte[count];
        int read = 0;
        while (read < count)
        {
            int n = await stream.ReadAsync(data.AsMemory(read, count - read), ct);
            if (n == 0) return null;
            read += n;
        }
        return data;
    }

    private string? HandleMessage(string json, IPAddress clientIp)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var type = doc.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;

            // Handshake: client announces its device name.
            if (type == "hello" && doc.RootElement.TryGetProperty("name", out var n))
            {
                var name = n.GetString();
                if (!string.IsNullOrEmpty(name))
                {
                    lock (_clientsLock)
                    {
                        _clientNames.RemoveAll(c => c.Contains(clientIp.ToString(), StringComparison.Ordinal));
                        _clientNames.Add($"{name} ({clientIp})");
                        DeviceName = name;
                    }
                    ClientsChanged?.Invoke();
                }
                return Json.String(new { type = "hello_ack", app = "HandHeld", api = ApiVersion, host = DisplayName });
            }

            return type switch
            {
                "list_games" => HandleListGames(),
                "launch" => HandleLaunch(doc.RootElement, clientIp),
                "launch_desktop" => HandleLaunchDesktop(doc.RootElement, clientIp),
                "stop" => HandleStop(),
                "keyframe" => RequestKeyframe(),
                _ => null,
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private string? _gamesCacheJson;
    private DateTime _gamesCacheTime = DateTime.MinValue;

    private string HandleListGames()
    {
        // Cache the (expensive) scan+icon encoding for 30s — the client's
        // first request must reply fast, not after a multi-second Steam scan.
        lock (_sessionLock)
        {
            if (_gamesCacheJson != null && (DateTime.UtcNow - _gamesCacheTime).TotalSeconds < 30)
            {
                return _gamesCacheJson;
            }
        }
        var games = Launcher.GameScanner.Scan()
            .Select(g => new
            {
                id = g.Id,
                title = g.Title,
                source = g.Source,
                icon = Launcher.GameIconProvider.GetIconPng(g),   // base64 PNG or null
            });
        var json = Json.String(new { type = "games", games });
        lock (_sessionLock)
        {
            _gamesCacheJson = json;
            _gamesCacheTime = DateTime.UtcNow;
        }
        return json;
    }
    private string HandleLaunch(JsonElement root, IPAddress clientIp)
    {
        var gameId = root.TryGetProperty("game", out var g) ? g.GetString() : null;
        if (string.IsNullOrEmpty(gameId))
        {
            return Json.String(new { type = "error", message = "Missing game id." });
        }

        int width = root.TryGetProperty("width", out var w) ? w.GetInt32() : 2560;
        int height = root.TryGetProperty("height", out var h) ? h.GetInt32() : 1440;
        // Only 60 or 30 fps are supported — clamp anything else.
        int fps = root.TryGetProperty("fps", out var f) ? f.GetInt32() : 60;
        fps = fps >= 45 ? 60 : 30;
        int bitrate = root.TryGetProperty("bitrate", out var b) ? b.GetInt32() : 30000;

        var error = Launcher.GameLauncher.Launch(gameId, width, height);
        if (error != null)
        {
            return Json.String(new { type = "error", message = error });
        }

        lock (_sessionLock)
        {
            _session?.Dispose();
            _audio?.Dispose();

            // Bring up the virtual display only for this client session.
            // (async — the session starts immediately; capture falls back to
            // primary until the virtual monitor attaches)
            Video.DisplayManager.SetVirtualDisplayEnabled(true);

            // The client's resolution drives the virtual display (second display),
            // and the virtual display becomes primary so games open there.
            var vd = Video.DisplayManager.FindVirtualDisplayDeviceName();
            if (vd != null)
            {
                Video.DisplayManager.SetVirtualDisplayResolution(width, height);
                Video.DisplayManager.SetPrimaryDisplay(vd);
            }

            _session = new StreamSession(
                width, height, fps, bitrate,
                new IPEndPoint(clientIp, PortVideo),
                stats => { });
            _session.Start();
            _audio = new Audio.AudioStreamer(new IPEndPoint(clientIp, Audio.AudioStreamer.Port));
            _audio.Start();
        }

        // Banner: show the launched game.
        _activeGame = Launcher.GameScanner.Games.FirstOrDefault(g => g.Id == gameId)?.Title ?? gameId;
        GameStarted?.Invoke(_activeGame);

        // Move the game window onto the virtual display so it renders there
        // (invisible on physical screens), then report started.
        var vdName = Video.DisplayManager.FindVirtualDisplayDeviceName();
        if (vdName != null)
        {
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(3000); // let the game spawn its window
                Launcher.GameLauncher.MoveGameWindowToDisplay(_activeGame, vdName, width, height);
            });
        }

        return Json.String(new { type = "started", width, height, fps, codec = "h264" });
    }

    private string HandleLaunchDesktop(JsonElement root, IPAddress clientIp)
    {
        int width = root.TryGetProperty("width", out var w) ? w.GetInt32() : 2560;
        int height = root.TryGetProperty("height", out var h) ? h.GetInt32() : 1440;
        // Only 60 or 30 fps are supported — clamp anything else.
        int fps = root.TryGetProperty("fps", out var f) ? f.GetInt32() : 60;
        fps = fps >= 45 ? 60 : 30;
        int bitrate = root.TryGetProperty("bitrate", out var b) ? b.GetInt32() : 30000;

        lock (_sessionLock)
        {
            _session?.Dispose();
            _audio?.Dispose();

            // Bring up the virtual display only for this client session.
            // (async — the session starts immediately; capture falls back to
            // primary until the virtual monitor attaches)
            Video.DisplayManager.SetVirtualDisplayEnabled(true);

            // The client's resolution drives the virtual display (second display),
            // and the virtual display becomes primary so games open there.
            var vd = Video.DisplayManager.FindVirtualDisplayDeviceName();
            if (vd != null)
            {
                Video.DisplayManager.SetVirtualDisplayResolution(width, height);
                Video.DisplayManager.SetPrimaryDisplay(vd);
            }

            _session = new StreamSession(
                width, height, fps, bitrate,
                new IPEndPoint(clientIp, PortVideo),
                stats => { });
            _session.Start();
            _audio = new Audio.AudioStreamer(new IPEndPoint(clientIp, Audio.AudioStreamer.Port));
            _audio.Start();
        }

        return Json.String(new { type = "started", width, height, fps, codec = "h264" });
    }

    private string HandleStop()
    {
        lock (_sessionLock)
        {
            _session?.Dispose();
            _session = null;
            _audio?.Dispose();
            _audio = null;
        }
        // The virtual display goes away when the session ends.
        Video.DisplayManager.SetVirtualDisplayEnabled(false);
        return Json.String(new { type = "stopped" });
    }

    private string RequestKeyframe()
    {
        _session?.ForceKeyframe();
        return Json.String(new { type = "keyframe_ack" });
    }
}
