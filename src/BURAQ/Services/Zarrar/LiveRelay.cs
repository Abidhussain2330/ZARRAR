using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace BURAQ.Services.Zarrar;

/// <summary>
/// A player's screen feed went live or dropped.
/// </summary>
public readonly record struct FeedChange(int GameId, int ParticipationId, string TeamName, bool Live,
    Guid? UserId = null, string? UserName = null);

/// <summary>
/// In-memory relay for ZARRAR player screen feeds.
/// </summary>
/// <remarks>
/// <para>
/// Each player's browser encodes its screen (WebCodecs, VP9 or VP8) and sends the encoded frames over one
/// WebSocket. This relay fans those frames out to every monitor/admin viewer of the same player,
/// unchanged. Everything travels over the existing HTTPS origin, so Caddy needs no new port.
/// </para>
/// <para>
/// Wire format, publisher → server → viewer:
///   text   {"type":"config","codec":"vp09.00.40.08","width":1920,"height":1080}
///   text   {"type":"ping"}                              (publisher heartbeat, not forwarded)
///   binary [1 byte: 1 = key frame, 0 = delta][8 bytes: timestamp µs, little endian][frame data]
/// Server → publisher: text {"type":"keyframe"} when a viewer needs a fresh key frame, and
/// {"type":"benched"} followed by close code 4001 when a teammate was tagged in instead.
/// Server → viewer: text {"type":"offline"} when the publisher drops, {"type":"ping"} every 4 s.
/// Viewer → server: any text message asks for a fresh key frame (the viewer skipped ahead after
/// falling behind or a decode error); requests are rate-limited per feed.
/// </para>
/// <para>
/// A viewer that falls behind is not allowed to buffer: its queue is dropped and it waits for the
/// next key frame, so a slow laptop on the Wi-Fi never delays the projector.
/// </para>
/// Single-instance by design: the venue runs one BURAQ server. Feeds do not cross instances.
/// </remarks>
public sealed class LiveRelay(ILogger<LiveRelay> logger, IConfiguration configuration) : IHostedService
{
    // A 1080p key frame of a busy screen is a few hundred KB; anything near this is garbage or abuse.
    private const int MaxMessageBytes = 2 * 1024 * 1024;
    private const int MaxTextBytes = 4 * 1024;

    // Recordings: one file is capped, and nothing new starts when the disk is nearly full, so a
    // runaway (or hostile) stream can never fill the server's volume.
    private const long MaxRecordingBytes = 4L * 1024 * 1024 * 1024;
    private const long MinFreeDiskBytes = 1L * 1024 * 1024 * 1024;
    private const int ViewerQueueCapacity = 90;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(6);
    private static readonly TimeSpan KeyframeRequestInterval = TimeSpan.FromMilliseconds(400);

    // A viewer whose socket accepts nothing for this long (laptop asleep, Wi-Fi gone without a
    // close) is dropped instead of holding a sender task open until TCP gives up.
    private static readonly TimeSpan ViewerSendTimeout = TimeSpan.FromSeconds(10);

    private static readonly byte[] KeyframeRequest = Encoding.UTF8.GetBytes("{\"type\":\"keyframe\"}");
    private static readonly byte[] OfflineNotice = Encoding.UTF8.GetBytes("{\"type\":\"offline\"}");
    private static readonly byte[] BenchedNotice = Encoding.UTF8.GetBytes("{\"type\":\"benched\"}");
    private static readonly byte[] ViewerPing = Encoding.UTF8.GetBytes("{\"type\":\"ping\"}");

    // Viewers get a keepalive this often, so a viewer can tell a quiet feed from a dead socket.
    private static readonly TimeSpan ViewerPingInterval = TimeSpan.FromSeconds(4);

    /// <summary>Close code telling a publisher it was tagged out (do not reconnect).</summary>
    private const WebSocketCloseStatus BenchedClose = (WebSocketCloseStatus)4001;

    private readonly ConcurrentDictionary<(int GameId, int ParticipationId), Feed> _feeds = new();
    private readonly bool _recordingEnabled = configuration.GetValue("Zarrar:Record", true);

    private readonly string _recordingRoot = configuration["Zarrar:RecordingPath"] is { Length: > 0 } path
        ? path
        : Path.Combine(PathHelper.Base, "zarrar-recordings");

    /// <summary>Folder holding a game's recordings (may not exist yet).</summary>
    public string RecordingDirectory(int gameId) => Path.GetFullPath(Path.Combine(_recordingRoot, gameId.ToString()));

    /// <summary>Whether new feeds are recorded.</summary>
    public bool RecordingEnabled => _recordingEnabled;

    private long _sessionCounter;
    private Timer? _watchdog;

    /// <summary>
    /// Raised when a feed goes live or drops. Handlers run on the relay's thread pool work item and
    /// must not throw.
    /// </summary>
    public event Func<FeedChange, Task>? FeedChanged;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _watchdog = new Timer(_ => CheckStaleFeeds(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _watchdog?.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>
    /// Whether the server currently receives this player's screen.
    /// </summary>
    public bool IsLive(int gameId, int participationId) =>
        _feeds.TryGetValue((gameId, participationId), out var feed) && feed.Publisher is not null;

    /// <summary>
    /// Who is streaming this team's screen right now, or null when the feed is down.
    /// </summary>
    public (Guid UserId, string UserName)? PublisherOf(int gameId, int participationId)
    {
        if (!_feeds.TryGetValue((gameId, participationId), out var feed))
            return null;

        lock (feed)
        {
            return feed.Publisher is not null && feed.PublisherUserId is { } id
                ? (id, feed.PublisherName ?? string.Empty)
                : null;
        }
    }

    /// <summary>
    /// Tag-out: if someone other than <paramref name="keep" /> is streaming this team's screen,
    /// tell their browser it was benched and close the stream. The team's viewers stay attached
    /// and pick up the next player's stream on the same feed.
    /// </summary>
    public async Task BenchAsync(int gameId, int participationId, Guid keep)
    {
        if (!_feeds.TryGetValue((gameId, participationId), out var feed))
            return;

        WebSocket? publisher;
        lock (feed)
        {
            publisher = feed.Publisher;
            if (publisher is null || feed.PublisherUserId == keep)
                return;
        }

        await RejectBenchedAsync(publisher, feed.SendLock);
    }

    /// <summary>
    /// Refuses a publisher who is not on the keyboard: sends the benched notice, then closes with
    /// code 4001 so the browser stops instead of reconnecting.
    /// </summary>
    public static Task RejectBenchedAsync(WebSocket socket) => RejectBenchedAsync(socket, null);

    private static async Task RejectBenchedAsync(WebSocket socket, SemaphoreSlim? sendLock)
    {
        try
        {
            var acquired = sendLock is not null && await sendLock.WaitAsync(TimeSpan.FromSeconds(2));

            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    await socket.SendAsync(BenchedNotice, WebSocketMessageType.Text, true, CancellationToken.None);
                    await socket.CloseOutputAsync(BenchedClose, "tagged out", CancellationToken.None);
                }
            }
            finally
            {
                if (acquired)
                    sendLock!.Release();
            }
        }
        catch
        {
            // already gone
        }

        // A browser that ignores the close frame is cut off.
        _ = Task.Delay(TimeSpan.FromSeconds(3)).ContinueWith(_ =>
        {
            if (socket.State is WebSocketState.Open or WebSocketState.CloseSent)
                socket.Abort();
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Runs one publisher connection until it closes.
    /// </summary>
    public async Task PublishAsync(WebSocket socket, int gameId, int participationId, string teamName,
        Guid userId, string userName, CancellationToken token)
    {
        var feed = _feeds.GetOrAdd((gameId, participationId), key => new Feed(key.GameId, key.ParticipationId));
        var session = Interlocked.Increment(ref _sessionCounter);
        WebSocket? replaced;

        bool wasDown;
        lock (feed)
        {
            replaced = feed.Publisher;
            wasDown = replaced is null;
            feed.Publisher = socket;
            feed.PublisherSession = session;
            feed.TeamName = teamName;
            feed.PublisherUserId = userId;
            feed.PublisherName = userName;
            feed.LastSeenUtc = DateTimeOffset.UtcNow;
            feed.ConfigJson = null;
        }

        // A second tab (or a reconnect that beat the old socket's timeout) takes over the feed.
        replaced?.Abort();

        logger.SystemLog(
            $"ZARRAR feed live: game {gameId}, participation {participationId} ({teamName}, {userName})",
            TaskStatus.Success, LogLevel.Information);

        // A same-user takeover (second tab, fast reconnect) is not news; only a feed that was down
        // coming back is. This also stops a reconnect loop from flooding the stage and timeline.
        if (wasDown)
            await RaiseAsync(new FeedChange(gameId, participationId, teamName, true, userId, userName));

        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        Recorder? recorder = null;
        var recordingStopped = false;

        try
        {
            while (socket.State == WebSocketState.Open && !token.IsCancellationRequested)
            {
                message.SetLength(0);
                WebSocketReceiveResult result;

                do
                {
                    result = await socket.ReceiveAsync(buffer, token);

                    if (result.MessageType == WebSocketMessageType.Close)
                        return;

                    if (message.Length + result.Count > MaxMessageBytes)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "frame too large", token);
                        return;
                    }

                    message.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                feed.LastSeenUtc = DateTimeOffset.UtcNow;

                if (result.MessageType == WebSocketMessageType.Text)
                {
                    if (message.Length > MaxTextBytes)
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.MessageTooBig, "control message too large",
                            token);
                        return;
                    }

                    var config = ParseConfig(message.GetBuffer().AsSpan(0, (int)message.Length));
                    if (config is null)
                        continue; // heartbeat or unknown control message

                    // Same config again (the browser re-sends it on reconnect): nothing changes.
                    if (config.Value.Json == feed.ConfigJson)
                        continue;

                    // VP8/VP9 key frames carry their own size, so players handle a size change inside
                    // one file; only a codec change (VP9 → VP8 fallback) needs a new file.
                    var newCodec = config.Value.Codec != feed.Codec;
                    feed.ConfigJson = config.Value.Json;
                    feed.Width = config.Value.Width;
                    feed.Height = config.Value.Height;
                    feed.Codec = config.Value.Codec;

                    var configBytes = Encoding.UTF8.GetBytes(config.Value.Json);
                    foreach (var viewer in feed.Viewers.Values)
                    {
                        viewer.NeedsKey = true;
                        viewer.Offer(new Outgoing(configBytes, WebSocketMessageType.Text));
                    }

                    if (recorder is not null && newCodec)
                    {
                        await recorder.DisposeAsync();
                        recorder = null;
                    }

                    continue;
                }

                if (message.Length < 10 || feed.ConfigJson is null)
                    continue;

                var frame = message.ToArray();
                var isKey = frame[0] == 1;

                if (_recordingEnabled)
                {
                    if (recorder is null && isKey && !recordingStopped)
                        recorder = Recorder.TryCreate(_recordingRoot, gameId, participationId,
                            $"{teamName}-{userName}", session, feed.Width, feed.Height, feed.Codec, logger);

                    if (recorder is not null)
                    {
                        await recorder.WriteAsync(frame, token);

                        if (recorder.Bytes > MaxRecordingBytes)
                        {
                            logger.SystemLog(
                                $"ZARRAR recording for participation {participationId} reached its size cap and stopped",
                                TaskStatus.Exit, LogLevel.Warning);
                            await recorder.DisposeAsync();
                            recorder = null;
                            recordingStopped = true;
                        }
                    }
                }

                var needKey = false;
                foreach (var viewer in feed.Viewers.Values)
                {
                    if (viewer.NeedsKey && !isKey)
                    {
                        needKey = true;
                        continue;
                    }

                    if (isKey)
                        viewer.NeedsKey = false;

                    if (!viewer.Offer(new Outgoing(frame, WebSocketMessageType.Binary)))
                        needKey = true;
                }

                if (needKey)
                    await RequestKeyframeAsync(feed);
            }
        }
        catch (OperationCanceledException)
        {
            // server shutting down or the request was aborted
        }
        catch (WebSocketException)
        {
            // the browser went away; handled below like a normal close
        }
        catch (Exception e)
        {
            logger.LogErrorMessage(e, "ZARRAR feed relay failed");
        }
        finally
        {
            if (recorder is not null)
                await recorder.DisposeAsync();

            var wasCurrent = false;
            lock (feed)
            {
                if (feed.PublisherSession == session)
                {
                    feed.Publisher = null;
                    feed.PublisherUserId = null;
                    wasCurrent = true;
                }
            }

            if (wasCurrent)
            {
                foreach (var viewer in feed.Viewers.Values)
                {
                    viewer.NeedsKey = true;
                    viewer.Offer(new Outgoing(OfflineNotice, WebSocketMessageType.Text));
                }

                logger.SystemLog(
                    $"ZARRAR feed dropped: game {gameId}, participation {participationId} ({teamName}, {userName})",
                    TaskStatus.Exit, LogLevel.Warning);

                await RaiseAsync(new FeedChange(gameId, participationId, teamName, false, userId, userName));
            }
        }
    }

    /// <summary>
    /// Runs one viewer connection until it closes.
    /// </summary>
    public async Task WatchAsync(WebSocket socket, int gameId, int participationId, CancellationToken token)
    {
        var feed = _feeds.GetOrAdd((gameId, participationId), key => new Feed(key.GameId, key.ParticipationId));
        var viewer = new Viewer();
        var id = Guid.NewGuid();
        feed.Viewers[id] = viewer;

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);

        try
        {
            if (feed.ConfigJson is { } config)
                viewer.Offer(new Outgoing(Encoding.UTF8.GetBytes(config), WebSocketMessageType.Text));
            else
                viewer.Offer(new Outgoing(OfflineNotice, WebSocketMessageType.Text));

            await RequestKeyframeAsync(feed);

            var sender = Task.Run(async () =>
            {
                await foreach (var item in viewer.Queue.Reader.ReadAllAsync(cts.Token))
                {
                    using var sendTimeout = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
                    sendTimeout.CancelAfter(ViewerSendTimeout);
                    await socket.SendAsync(item.Data, item.Type, true, sendTimeout.Token);
                }
            }, cts.Token);

            var receiver = Task.Run(async () =>
            {
                var buffer = new byte[1024];
                while (socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(buffer, cts.Token);
                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    // The viewer skipped ahead and wants a fresh picture.
                    if (result.MessageType == WebSocketMessageType.Text && result.EndOfMessage)
                    {
                        viewer.NeedsKey = true;
                        await RequestKeyframeAsync(feed);
                    }
                }
            }, cts.Token);

            var keepalive = Task.Run(async () =>
            {
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(ViewerPingInterval, cts.Token);
                    viewer.Offer(new Outgoing(ViewerPing, WebSocketMessageType.Text));
                }
            }, cts.Token);

            await Task.WhenAny(sender, receiver, keepalive);
        }
        catch (OperationCanceledException)
        {
        }
        catch (WebSocketException)
        {
        }
        finally
        {
            await cts.CancelAsync();
            feed.Viewers.TryRemove(id, out _);
            viewer.Queue.Writer.TryComplete();
        }
    }

    private void CheckStaleFeeds()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var feed in _feeds.Values)
        {
            var publisher = feed.Publisher;
            if (publisher is not null && now - feed.LastSeenUtc > StaleAfter)
            {
                // Aborting ends the publisher's receive loop, which reports the feed as lost.
                publisher.Abort();
            }
        }
    }

    private static async Task RequestKeyframeAsync(Feed feed)
    {
        var publisher = feed.Publisher;
        if (publisher is null || publisher.State != WebSocketState.Open)
            return;

        var now = DateTimeOffset.UtcNow;
        if (now - feed.LastKeyframeRequestUtc < KeyframeRequestInterval)
            return;

        if (!await feed.SendLock.WaitAsync(0))
            return;

        try
        {
            feed.LastKeyframeRequestUtc = now;
            await publisher.SendAsync(KeyframeRequest, WebSocketMessageType.Text, true, CancellationToken.None);
        }
        catch
        {
            // the publisher loop will notice the broken socket
        }
        finally
        {
            feed.SendLock.Release();
        }
    }

    private async Task RaiseAsync(FeedChange change)
    {
        var handlers = FeedChanged;
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<Func<FeedChange, Task>>())
        {
            try
            {
                await handler(change);
            }
            catch (Exception e)
            {
                logger.LogErrorMessage(e, "ZARRAR feed change handler failed");
            }
        }
    }

    private static (string Json, int Width, int Height, string Codec)? ParseConfig(ReadOnlySpan<byte> utf8)
    {
        try
        {
            var reader = new Utf8JsonReader(utf8);
            using var doc = JsonDocument.ParseValue(ref reader);
            var root = doc.RootElement;

            if (!root.TryGetProperty("type", out var type) || type.GetString() != "config")
                return null;

            var codec = root.TryGetProperty("codec", out var c) ? c.GetString() ?? "vp8" : "vp8";
            var width = root.TryGetProperty("width", out var w) && w.TryGetInt32(out var wv) ? wv : 1280;
            var height = root.TryGetProperty("height", out var h) && h.TryGetInt32(out var hv) ? hv : 720;

            // Only a short allow-list of codec strings is forwarded; nothing else from the
            // publisher reaches the viewers' pages.
            if (codec is not ("vp8" or "vp09.00.10.08" or "vp09.00.40.08" or "avc1.42E01F"))
                codec = "vp8";

            width = Math.Clamp(width, 16, 3840);
            height = Math.Clamp(height, 16, 2160);

            return ($"{{\"type\":\"config\",\"codec\":\"{codec}\",\"width\":{width},\"height\":{height}}}", width,
                height, codec);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private readonly record struct Outgoing(byte[] Data, WebSocketMessageType Type);

    private sealed class Feed(int gameId, int participationId)
    {
        public int GameId { get; } = gameId;
        public int ParticipationId { get; } = participationId;
        public string TeamName { get; set; } = string.Empty;
        public Guid? PublisherUserId;
        public string? PublisherName;
        public volatile WebSocket? Publisher;
        public long PublisherSession;
        public volatile string? ConfigJson;
        public int Width = 1280;
        public int Height = 720;
        public string Codec = "vp8";
        public DateTimeOffset LastSeenUtc = DateTimeOffset.UtcNow;
        public DateTimeOffset LastKeyframeRequestUtc = DateTimeOffset.MinValue;
        public readonly SemaphoreSlim SendLock = new(1, 1);
        public readonly ConcurrentDictionary<Guid, Viewer> Viewers = new();
    }

    private sealed class Viewer
    {
        public volatile bool NeedsKey = true;

        public readonly Channel<Outgoing> Queue = Channel.CreateBounded<Outgoing>(
            new BoundedChannelOptions(ViewerQueueCapacity)
            {
                // Offer() drains the queue from the publisher's thread while the sender loop reads.
                SingleReader = false,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.Wait
            });

        /// <summary>
        /// Queues an item without ever blocking the publisher. When the viewer is too far behind,
        /// its backlog is thrown away and it resumes at the next key frame.
        /// </summary>
        public bool Offer(Outgoing item)
        {
            if (Queue.Writer.TryWrite(item))
                return true;

            NeedsKey = true;
            while (Queue.Reader.TryRead(out _))
            {
            }

            // Control messages (config / offline) must still arrive after a drop.
            if (item.Type == WebSocketMessageType.Text)
                Queue.Writer.TryWrite(item);

            return false;
        }
    }

    /// <summary>
    /// Writes one player's feed to an IVF file (playable in VLC / ffmpeg).
    /// </summary>
    private sealed class Recorder : IAsyncDisposable
    {
        private readonly FileStream _stream;
        private long _firstTimestampUs = -1;
        private readonly byte[] _frameHeader = new byte[12];

        private Recorder(FileStream stream) => _stream = stream;

        /// <summary>Bytes written so far.</summary>
        public long Bytes { get; private set; } = 32;

        public static Recorder? TryCreate(string root, int gameId, int participationId, string teamName,
            long session, int width, int height, string codec, ILogger<LiveRelay> logger)
        {
            try
            {
                var dir = Path.Combine(root, gameId.ToString());
                Directory.CreateDirectory(dir);

                try
                {
                    var free = new DriveInfo(Path.GetFullPath(dir)).AvailableFreeSpace;
                    if (free < MinFreeDiskBytes)
                    {
                        logger.SystemLog($"ZARRAR recording skipped: only {free / (1024 * 1024)} MB free on disk",
                            TaskStatus.Failed, LogLevel.Warning);
                        return null;
                    }
                }
                catch (Exception)
                {
                    // Free space unknown (unusual mount): record anyway; the size cap still applies.
                }

                var safeTeam = new string(teamName.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
                if (safeTeam.Length > 60)
                    safeTeam = safeTeam[..60];

                // The session number keeps two recordings started in the same second apart.
                var file = Path.Combine(dir,
                    $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-p{participationId}-{safeTeam}-s{session}.ivf");

                var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.Read, 64 * 1024,
                    useAsync: true);

                // 32-byte IVF header. Time base 1/1000 so frame timestamps are milliseconds.
                var header = new byte[32];
                "DKIF"u8.CopyTo(header);
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(4), 0);
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(6), 32);
                (codec.StartsWith("vp09", StringComparison.Ordinal) ? "VP90"u8
                    : codec.StartsWith("avc1", StringComparison.Ordinal) ? "H264"u8
                    : "VP80"u8).CopyTo(header.AsSpan(8));
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(12), (ushort)width);
                BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(14), (ushort)height);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(16), 1000);
                BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(20), 1);
                stream.Write(header);

                logger.SystemLog($"ZARRAR recording to {file}", TaskStatus.Success, LogLevel.Debug);
                return new Recorder(stream);
            }
            catch (Exception e)
            {
                logger.LogErrorMessage(e, "ZARRAR could not start a recording");
                return null;
            }
        }

        public async Task WriteAsync(byte[] frame, CancellationToken token)
        {
            var timestampUs = BinaryPrimitives.ReadInt64LittleEndian(frame.AsSpan(1, 8));
            if (_firstTimestampUs < 0)
                _firstTimestampUs = timestampUs;

            var payload = frame.AsMemory(9);
            BinaryPrimitives.WriteUInt32LittleEndian(_frameHeader, (uint)payload.Length);
            BinaryPrimitives.WriteInt64LittleEndian(_frameHeader.AsSpan(4),
                Math.Max(0, (timestampUs - _firstTimestampUs) / 1000));

            await _stream.WriteAsync(_frameHeader, token);
            await _stream.WriteAsync(payload, token);
            Bytes += _frameHeader.Length + payload.Length;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _stream.FlushAsync();
            }
            finally
            {
                await _stream.DisposeAsync();
            }
        }
    }
}
