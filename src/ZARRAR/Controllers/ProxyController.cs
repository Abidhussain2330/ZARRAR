using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using ZARRAR.Middlewares;
using ZARRAR.Models.Internal;
using ZARRAR.Repositories.Interface;
using ZARRAR.Services.Cache;
using ZARRAR.Services.Traffic;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace ZARRAR.Controllers;

/// <summary>
/// Container TCP traffic proxy and logging APIs
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class ProxyController(
    ILogger<ProxyController> logger,
    IDistributedCache cache,
    IOptions<ContainerProvider> provider,
    IContainerRepository containerRepository,
    TrafficRecorderRegistry trafficRegistry,
    IConfiguration configuration,
    IStringLocalizer<Program> localizer) : ControllerBase
{
    private const int BufferSize = 4096;
    private const int ConnectionLimit = 32;

    /// <summary>
    /// Serialises the read-modify-write of a container's connection counter within this process.
    /// </summary>
    /// <remarks>
    /// <see cref="IDistributedCache" /> offers no atomic increment, so the original get-then-set
    /// allowed concurrent connections to both observe the same count and overshoot the limit
    /// (finding F-010). Guarding the sequence per container makes the accounting exact for a
    /// single instance and materially narrows the window for a multi-instance deployment; the
    /// multi-instance operators must still treat the residual cross-instance race as an
    /// operational risk.
    /// </remarks>
    private static readonly ConcurrentDictionary<Guid, SemaphoreSlim> ConnectionLocks = new();

    private static readonly JsonSerializerOptions JsonOptions =
        new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
            TypeInfoResolver = new AppJsonSerializerContext()
        };

    private static readonly DistributedCacheEntryOptions StoreOption =
        new() { SlidingExpiration = TimeSpan.FromHours(10) };

    private static readonly DistributedCacheEntryOptions ValidOption =
        new() { SlidingExpiration = TimeSpan.FromMinutes(10) };

    private readonly bool _enablePlatformProxy =
        provider.Value.PortMappingType == ContainerPortMappingType.PlatformProxy;

    private readonly bool _enableTrafficCapture = provider.Value.EnableTrafficCapture;

    /// <summary>
    /// Platform secret used to fingerprint flag material embedded in capture metadata.
    /// </summary>
    private readonly byte[] _fingerprintKey = configuration["XorKey"]?.ToUTF8Bytes() ?? [];

    /// <summary>
    /// Proxy TCP over websocket
    /// </summary>
    /// <param name="id">Container ID</param>
    /// <param name="token"></param>
    /// <returns></returns>
    [RequireUser]
    [Route("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status418ImATeapot)]
    public async Task<IActionResult> ProxyForInstance(Guid id, CancellationToken token = default)
    {
        if (!_enablePlatformProxy)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Proxy_TcpDisabled)]));

        if (!await ValidateContainer(id, token))
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Container_NotFound)],
                StatusCodes.Status404NotFound));

        // The container GUID is an identifier, not a capability. Bind the connection to the
        // authenticated caller and verify they belong to the team that owns this instance,
        // otherwise a leaked proxy URL grants cross-team challenge access (finding F-002).
        if (!TryGetUserId(out var userId))
            return RequestResponse.Result(localizer[nameof(Resources.Program.Auth_LoginRequired)],
                StatusCodes.Status401Unauthorized);

        if (!await containerRepository.ValidateContainerAccess(id, userId, token))
        {
            logger.SystemLog(
                StaticLocalizer[nameof(Resources.Program.Proxy_ContainerNotOwned)],
                TaskStatus.Denied, LogLevel.Warning);

            return RequestResponse.Result(localizer[nameof(Resources.Program.Proxy_AccessDenied)],
                StatusCodes.Status403Forbidden);
        }

        if (!HttpContext.WebSockets.IsWebSocketRequest)
            return NoContent();

        var container = await containerRepository.GetContainerWithInstanceById(id, token);

        if (container is null || !container.IsProxy)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Container_NotFound)],
                StatusCodes.Status404NotFound));

        var ipAddress = (await Dns.GetHostAddressesAsync(container.IP, token)).FirstOrDefault();

        if (ipAddress is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Container_AddressResolveFailed)]));

        // Acquire the connection slot only once every validation has passed, so that a rejected
        // request can never leak a permit that the shared cleanup path would later release.
        if (!await IncreaseConnectionCount(id))
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Container_ConnectionLimitExceeded)]));

        var clientIp = HttpContext.Connection.RemoteIpAddress ?? IPAddress.Loopback;

        var enable = _enableTrafficCapture && container.EnableTrafficCapture;

        TrafficWriter? writer = null;
        if (enable)
        {
            var descriptor = new TrafficRecorderDescriptor(
                ContainerId: id,
                ChallengeId: container.GameInstance!.ChallengeId,
                ParticipationId: container.GameInstance!.ParticipationId,
                Metadata: container.GenerateMetadata(JsonOptions, _fingerprintKey),
                ConnectionId: HttpContext.Connection.Id,
                RemoteIpAddress: HttpContext.Connection.RemoteIpAddress);
            writer = trafficRegistry.AcquireWriter(descriptor);
        }

        var realRemotePort = HttpContext.Connection.RemotePort;
        var clientPort = writer?.Sequence ?? realRemotePort;

        IPEndPoint client = new(clientIp, clientPort);
        IPEndPoint target = new(ipAddress, container.Port);

        return await DoContainerProxy(id, client, target, writer, realRemotePort, token);
    }

    /// <summary>
    /// Proxy TCP over websocket for admins
    /// </summary>
    /// <param name="id">Test container ID</param>
    /// <param name="token"></param>
    /// <returns></returns>
    [RequireAdmin]
    [Route("NoInst/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(RequestResponse), StatusCodes.Status418ImATeapot)]
    [SuppressMessage("ReSharper", "RouteTemplates.ParameterTypeCanBeMadeStricter")]
    public async Task<IActionResult> ProxyForNoInstance(Guid id, CancellationToken token = default)
    {
        if (!_enablePlatformProxy)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Proxy_TcpDisabled)]));

        if (!await ValidateContainer(id, token))
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Container_NotFound)],
                StatusCodes.Status404NotFound));

        if (!HttpContext.WebSockets.IsWebSocketRequest)
            return NoContent();

        var container = await containerRepository.GetContainerById(id, token);

        if (container is null || container.GameInstanceId is not null || !container.IsProxy)
            return NotFound(new RequestResponse(localizer[nameof(Resources.Program.Container_NotFound)],
                StatusCodes.Status404NotFound));

        var ipAddress = (await Dns.GetHostAddressesAsync(container.IP, token)).FirstOrDefault();

        if (ipAddress is null)
            return BadRequest(new RequestResponse(localizer[nameof(Resources.Program.Container_AddressResolveFailed)]));

        var clientIp = HttpContext.Connection.RemoteIpAddress ?? IPAddress.Loopback;
        var clientPort = HttpContext.Connection.RemotePort;

        // Previously this path never acquired a permit, yet the shared cleanup in
        // DoContainerProxy decremented one regardless, driving the counter below zero and
        // corrupting the limit for the instance path (finding F-010).
        if (!await IncreaseConnectionCount(id))
            return BadRequest(
                new RequestResponse(localizer[nameof(Resources.Program.Container_ConnectionLimitExceeded)]));

        IPEndPoint client = new(clientIp, clientPort);
        IPEndPoint target = new(ipAddress, container.Port);

        return await DoContainerProxy(id, client, target, null, client.Port, token);
    }

    /// <summary>
    /// Read the authenticated user ID from the current principal.
    /// </summary>
    private bool TryGetUserId(out Guid userId)
    {
        userId = Guid.Empty;

        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);

        return claim is not null && Guid.TryParse(claim, out userId);
    }

    private async Task<IActionResult> DoContainerProxy(Guid id, IPEndPoint client, IPEndPoint target,
        TrafficWriter? writer, int realClientPort, CancellationToken token = default)
    {
        using var socket = new Socket(target.AddressFamily, SocketType.Stream, ProtocolType.Tcp);

        CaptureNetworkStream? stream = null;

        try
        {
            try
            {
                await socket.ConnectAsync(target, token);

                if (!socket.Connected)
                    throw new SocketException((int)SocketError.NotConnected);

                stream = new CaptureNetworkStream(socket, writer, client, target);
            }
            catch (SocketException e)
            {
                logger.SystemLog(
                    StaticLocalizer[nameof(Resources.Program.Proxy_ContainerConnectionFailedLog),
                        e.SocketErrorCode,
                        $"{target.Address}:{target.Port}"],
                    TaskStatus.Failed, LogLevel.Debug);

                return RequestResponse.Result(
                    localizer[nameof(Resources.Program.Proxy_ContainerConnectionFailed), e.SocketErrorCode],
                    StatusCodes.Status418ImATeapot);
            }

            using var ws = await HttpContext.WebSockets.AcceptWebSocketAsync();

            try
            {
                var (tx, rx) = await RunProxy(stream, ws, token);
                LogProxyResult(id, new IPEndPoint(client.Address, realClientPort), target, tx, rx);
            }
            catch (Exception e)
            {
                logger.LogErrorMessage(e, StaticLocalizer[nameof(Resources.Program.Proxy_Error)]);
            }
        }
        finally
        {
            if (stream is not null)
                await stream.DisposeAsync();
            else
                writer?.Dispose();

            await DecreaseConnectionCount(id);
        }

        return new EmptyResult();
    }

    private void LogProxyResult(Guid id, IPEndPoint client, IPEndPoint target, ulong tx, ulong rx)
    {
        var shortId = id.ToString("N")[..12];
        var clientAddress = client.Address.IsIPv4MappedToIPv6 ? client.Address.MapToIPv4() : client.Address;
        var targetAddress = target.Address.IsIPv4MappedToIPv6 ? target.Address.MapToIPv4() : target.Address;

        logger.SystemLog($"[{shortId}] {clientAddress} -> {targetAddress}:{target.Port}, tx {tx}, rx {rx}",
            TaskStatus.Success, LogLevel.Debug);
    }

    /// <summary>
    /// Proxy TCP traffic using websocket
    /// </summary>
    /// <param name="stream"></param>
    /// <param name="ws"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    private static async Task<(ulong, ulong)> RunProxy(CaptureNetworkStream stream, WebSocket ws,
        CancellationToken token = default)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);

        cts.CancelAfter(TimeSpan.FromMinutes(30));

        var ct = cts.Token;
        ulong tx = 0, rx = 0;

        var sender = Task.Run(async () =>
        {
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                while (true)
                {
                    var status = await ws.ReceiveAsync(buffer, ct);
                    if (status.CloseStatus.HasValue)
                        break;
                    if (status.Count <= 0)
                        continue;

                    tx += (ulong)status.Count;
                    var memory = buffer.AsMemory(0, status.Count);
                    await stream.WriteAsync(memory, ct);
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }, ct);

        var receiver = Task.Run(async () =>
        {
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            try
            {
                while (true)
                {
                    var count = await stream.ReadAsync(buffer, ct);
                    if (count == 0)
                    {
                        await ws.CloseAsync(WebSocketCloseStatus.Empty, null, ct);
                        break;
                    }

                    rx += (ulong)count;
                    var memory = buffer.AsMemory(0, count);
                    await ws.SendAsync(memory, WebSocketMessageType.Binary, true, ct);
                }
            }
            catch
            {
                // ignore
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }, ct);

        await Task.WhenAny(sender, receiver);
        await cts.CancelAsync();
        await Task.WhenAll(sender, receiver);

        return (tx, rx);
    }

    /// <summary>
    /// Validate container existence
    /// </summary>
    /// <param name="id">Container ID</param>
    /// <param name="token"></param>
    /// <returns></returns>
    private async Task<bool> ValidateContainer(Guid id, CancellationToken token = default)
    {
        var key = CacheKey.ConnectionCount(id);
        var bytes = await cache.GetAsync(key, token);

        // avoid DoS attack with cache -1
        if (bytes is not null)
            return BitConverter.ToInt32(bytes) >= 0;

        var valid = await containerRepository.ValidateContainer(id, token);

        await cache.SetAsync(key, BitConverter.GetBytes(valid ? 0 : -1), ValidOption, token);

        return valid;
    }

    /// <summary>
    /// Increase Fetch-Add operation for container TCP connection count
    /// </summary>
    /// <param name="id">Container id</param>
    /// <returns></returns>
    private async Task<bool> IncreaseConnectionCount(Guid id)
    {
        var key = CacheKey.ConnectionCount(id);
        var gate = ConnectionLocks.GetOrAdd(id, static _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync();
        try
        {
            var bytes = await cache.GetAsync(key);

            if (bytes is null)
                return false;

            var count = BitConverter.ToInt32(bytes);

            // A negative value is the sentinel written by ValidateContainer for an unknown
            // container; never treat it as spare capacity.
            if (count < 0)
                return false;

            // Was `>`, which permitted ConnectionLimit + 1 concurrent connections (finding F-010).
            if (count >= ConnectionLimit)
                return false;

            await cache.SetAsync(key, BitConverter.GetBytes(count + 1), StoreOption);

            return true;
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Implement decrease operation for container TCP connection count
    /// </summary>
    /// <param name="id">Container id</param>
    /// <returns></returns>
    private async Task DecreaseConnectionCount(Guid id)
    {
        var key = CacheKey.ConnectionCount(id);
        var gate = ConnectionLocks.GetOrAdd(id, static _ => new SemaphoreSlim(1, 1));

        await gate.WaitAsync();
        try
        {
            var bytes = await cache.GetAsync(key);

            if (bytes is null)
                return;

            var count = BitConverter.ToInt32(bytes);

            // Idempotent and clamped: an abrupt disconnect or a repeated cleanup must never drive
            // the counter negative, which would silently raise the effective connection ceiling.
            if (count <= 0)
                return;

            if (count > 1)
                await cache.SetAsync(key, BitConverter.GetBytes(count - 1), StoreOption);
            else
                await cache.SetAsync(key, BitConverter.GetBytes(0), ValidOption);
        }
        finally
        {
            gate.Release();
        }
    }
}
