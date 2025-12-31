using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using MemoryPack;
using Netcode.Rollback;
using Netcode.Rollback.Network;
using UnityEngine;

public sealed class SynapseClient : IDisposable, INonBlockingSocket<EndPoint>
{
    public Guid ClientGuid { get; }
    public string ClientIdDecimal { get; }          // u128 as decimal string, used in WS query.
    public ulong? CurrentRoomId => _roomId;
    public bool IsConnected => _ws != null && _ws.State == WebSocketState.Open;

    public event Action<ulong> OnRoomCreated;       // server echoes RoomCreated(room_id)
    public event Action<uint> OnYouAre;             // handle (0 host, 1 guest)
    public event Action<uint> OnPeerJoined;         // handle
    public event Action<uint> OnPeerLeft;           // handle
    public event Action<IPEndPoint> OnPeerFound;    // NAT punch succeeded

    // --- Configuration ---
    private readonly Uri _baseHttpWs;               // e.g. ws://your-host:9000
    private readonly IPEndPoint _punchEp;           // UDP punch coordinator, e.g. host:9001
    private readonly IPEndPoint _relayEp;           // UDP relay, e.g. host:9002

    // --- Internals ---
    private ClientWebSocket _ws;
    private CancellationTokenSource _wsCts;
    private Task _wsRecvLoop;

    private UdpClient _udp;
    private CancellationTokenSource _udpCts;
    private Task _udpRecvLoop;
    private Task _udpPunchLoop;
    private readonly byte[] _buffer;
    private ulong? _roomId;
    private uint _handle;
    private IPEndPoint _peerEp;

    // --- Protocol constants (must match Rust) ---
    private const byte WS_ROOM_CREATED = 1;
    private const byte WS_YOU_ARE = 2;
    private const byte WS_PEER_JOINED = 3;
    private const byte WS_PEER_LEFT = 4;

    private const byte UDP_FOUND_PEER = 0x1;
    private const byte UDP_WAITING = 0x2;

    public const int RECV_BUFFER_SIZE = 4096;
    public const int IDEAL_MAX_UDP_PACKET_SIZE = 508;

    public SynapseClient(
        string host,
        int httpPort = 9000,
        int punchPort = 9001,
        int relayPort = 9002)
    {
        _baseHttpWs = new Uri($"ws://{host}:{httpPort}");

        _punchEp = new IPEndPoint(DnsSafeResolve(host), punchPort);
        _relayEp = new IPEndPoint(DnsSafeResolve(host), relayPort);

        ClientGuid = Guid.NewGuid();
        ClientIdDecimal = GuidToU128Decimal(ClientGuid);
        _buffer = new byte[RECV_BUFFER_SIZE];
    }


    public async Task CreateRoomAsync(CancellationToken ct = default)
    {
        await ConnectWsAsync(new Uri(_baseHttpWs, $"/create_room?client_id={ClientIdDecimal}"), ct).ConfigureAwait(false);
    }

    public async Task JoinRoomAsync(ulong roomId, CancellationToken ct = default)
    {
        await ConnectWsAsync(new Uri(_baseHttpWs, $"/join_room/{roomId}?client_id={ClientIdDecimal}"), ct).ConfigureAwait(false);
        // server will send RoomCreated(room_id) and YouAre(handle) after connect.
    }

    public async Task LeaveRoomAsync(CancellationToken ct = default)
    {
        _roomId = null;
        _peerEp = null;

        StopUdp();

        if (_ws != null)
        {
            try
            {
                _wsCts?.Cancel();
                if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
                {
                    await _ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "leave", ct).ConfigureAwait(false);
                }
            }
            catch { /* ignore */ }
            finally
            {
                _ws.Dispose();
                _ws = null;
            }
        }
    }

    public void SendTo(in Message message, EndPoint addr)
    {
        byte[] payload = MemoryPackSerializer.Serialize(message);
        if (payload.Length > UdpSocketConstants.IDEAL_MAX_UDP_PACKET_SIZE)
        {
            Debug.Log($"Sending UDP packet of size {payload.Length} bytes, which is larger than ideal ({UdpSocketConstants.IDEAL_MAX_UDP_PACKET_SIZE}).");
        }
        _udp.Client.SendTo(payload, SocketFlags.None, addr);
    }

    public IEnumerable<(EndPoint addr, Message message)> ReceiveAllMessages()
    {
        var received = new List<(EndPoint, Message)>();

        while (true)
        {
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            try
            {
                int bytes = _udp.Client.ReceiveFrom(_buffer, 0, _buffer.Length, SocketFlags.None, ref remote);
                if (bytes <= 0) continue;
                if (bytes > UdpSocketConstants.RECV_BUFFER_SIZE)
                    throw new InvalidOperationException("Received more bytes than buffer size.");

                Message? message = MemoryPackSerializer.Deserialize<Message>(_buffer.AsSpan().Slice(0, bytes));
                if (message != null) { received.Add((remote, message.Value)); }
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock) { return received; }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset) { continue; }
            catch (SocketException ex)
            {
                throw new InvalidOperationException(
                    $"{ex.SocketErrorCode}: {ex.Message} on {_udp.Client.LocalEndPoint}", ex);
            }
        }
    }


    // Call after CreateRoom/JoinRoom. Continues until peer is found or you StopUdp/LeaveRoom.
    public void StartPunching(int localUdpPort = 0, int sendIntervalMs = 200)
    {
        if (_udp != null) return;

        _udp = new UdpClient(localUdpPort);
        _udp.Client.Blocking = false;

        _udpCts = new CancellationTokenSource();
        _udpRecvLoop = Task.Run(() => UdpReceiveLoop(_udpCts.Token));
        _udpPunchLoop = Task.Run(() => UdpPunchLoop(sendIntervalMs, _udpCts.Token));
    }

    public void StopUdp()
    {
        try { _udpCts?.Cancel(); } catch { }
        try { _udp?.Dispose(); } catch { }

        _udp = null;
        _udpCts = null;
        _udpRecvLoop = null;
        _udpPunchLoop = null;
    }

    public async Task SendViaRelayAsync(byte[] payload, CancellationToken ct = default)
    {
        if (_udp == null) throw new InvalidOperationException("UDP not started. Call StartPunching() first.");
        await _udp.SendAsync(payload, payload.Length, _relayEp).ConfigureAwait(false);
    }


    private async Task ConnectWsAsync(Uri wsUri, CancellationToken ct)
    {
        // Close any prior connections.
        await LeaveRoomAsync(ct).ConfigureAwait(false);

        _ws = new ClientWebSocket();
        _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);

        _wsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await _ws.ConnectAsync(wsUri, ct).ConfigureAwait(false);

        _wsRecvLoop = Task.Run(() => WsReceiveLoop(_wsCts.Token));
    }

    private async Task WsReceiveLoop(CancellationToken ct)
    {
        var buf = new byte[2048];

        try
        {
            while (!ct.IsCancellationRequested && _ws != null && _ws.State == WebSocketState.Open)
            {
                var seg = new ArraySegment<byte>(buf);
                WebSocketReceiveResult res = await _ws.ReceiveAsync(seg, ct).ConfigureAwait(false);

                if (res.MessageType == WebSocketMessageType.Close) break;
                if (res.MessageType != WebSocketMessageType.Binary) continue;

                int n = res.Count;
                while (!res.EndOfMessage)
                {
                    // This client expects tiny messages; if yours grows, implement reassembly here.
                    throw new InvalidOperationException("WS message too large / fragmented; implement reassembly.");
                }

                HandleWsBinary(buf, n);
            }
        }
        catch { /* treat as disconnect */ }
        finally
        {
            // Server-side cleanup happens on disconnect.
        }
    }

    private void HandleWsBinary(byte[] data, int n)
    {
        if (n < 1) return;
        byte tag = data[0];

        switch (tag)
        {
            case WS_ROOM_CREATED:
                {
                    if (n < 1 + 8) return;
                    ulong room = ReadU64BE(data, 1);
                    _roomId = room;
                    OnRoomCreated?.Invoke(room);
                    break;
                }
            case WS_YOU_ARE:
                {
                    if (n < 1 + 4) return;
                    uint handle = ReadU32BE(data, 1);
                    _handle = handle;
                    OnYouAre?.Invoke(handle);
                    break;
                }
            case WS_PEER_JOINED:
                {
                    if (n < 1 + 4) return;
                    uint h = ReadU32BE(data, 1);
                    OnPeerJoined?.Invoke(h);
                    break;
                }
            case WS_PEER_LEFT:
                {
                    if (n < 1 + 4) return;
                    uint h = ReadU32BE(data, 1);
                    OnPeerLeft?.Invoke(h);
                    break;
                }
        }
    }

    private async Task UdpPunchLoop(int sendIntervalMs, CancellationToken ct)
    {
        byte[] bindPkt = BuildClientIdPacket();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                if (_peerEp != null) return; // done
                if (_udp != null)
                {
                    // Send to punch coordinator to get back peer endpoint when available.
                    await _udp.SendAsync(bindPkt, bindPkt.Length, _punchEp).ConfigureAwait(false);
                }
                await Task.Delay(sendIntervalMs, ct).ConfigureAwait(false);
            }
        }
        catch { /* stop */ }
    }

    private async Task UdpReceiveLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _udp != null)
            {
                UdpReceiveResult res = await _udp.ReceiveAsync().ConfigureAwait(false);
                HandleUdpPacket(res.Buffer, res.Buffer.Length);
            }
        }
        catch { /* stop */ }
    }

    private void HandleUdpPacket(byte[] data, int n)
    {
        if (n < 1) return;

        byte tag = data[0];
        if (tag == UDP_WAITING)
        {
            // Nothing to do.
            return;
        }

        if (tag != UDP_FOUND_PEER) return;
        if (n < 2) return;

        byte fam = data[1];
        if (fam == 4)
        {
            if (n < 8) return;
            var ip = new IPAddress(new byte[] { data[2], data[3], data[4], data[5] });
            int port = ReadU16BE(data, 6);
            SetPeer(new IPEndPoint(ip, port));
        }
        else if (fam == 6)
        {
            if (n < 20) return;
            var ipBytes = new byte[16];
            Buffer.BlockCopy(data, 2, ipBytes, 0, 16);
            var ip = new IPAddress(ipBytes);
            int port = ReadU16BE(data, 18);
            SetPeer(new IPEndPoint(ip, port));
        }
    }

    private void SetPeer(IPEndPoint ep)
    {
        if (_peerEp != null) return;
        _peerEp = ep;
        OnPeerFound?.Invoke(ep);
    }

    private static string GuidToU128Decimal(Guid g)
    {
        string hex = g.ToString("N"); // 32 hex chars
        byte[] be16 = new byte[16];
        for (int i = 0; i < 16; i++)
        {
            be16[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
        }

        // BigInteger expects little-endian by default. Convert explicitly.
        // Ensure non-negative by appending a 0 sign byte.
        byte[] le = new byte[17];
        for (int i = 0; i < 16; i++) le[i] = be16[15 - i];
        le[16] = 0;

        var bi = new BigInteger(le);
        return bi.ToString(); // decimal
    }

    private byte[] BuildClientIdPacket()
    {
        byte[] u128be = U128DecimalTo16BytesBE(ClientIdDecimal);
        return u128be;
    }

    private static byte[] U128DecimalTo16BytesBE(string dec)
    {
        // Parse decimal -> BigInteger -> 16-byte big-endian (unsigned).
        BigInteger bi = BigInteger.Parse(dec);

        // Convert to unsigned big-endian fixed width.
        // BigInteger.ToByteArray() gives little-endian two's complement.
        byte[] le = bi.ToByteArray(isUnsigned: true, isBigEndian: false);

        // le -> fixed 16 be
        var be16 = new byte[16];
        int copy = Math.Min(le.Length, 16);
        for (int i = 0; i < copy; i++)
            be16[15 - i] = le[i];

        return be16;
    }


    private static ushort ReadU16BE(byte[] b, int o)
        => (ushort)((b[o] << 8) | b[o + 1]);

    private static uint ReadU32BE(byte[] b, int o)
        => ((uint)b[o] << 24) | ((uint)b[o + 1] << 16) | ((uint)b[o + 2] << 8) | b[o + 3];

    private static ulong ReadU64BE(byte[] b, int o)
        => ((ulong)b[o] << 56) | ((ulong)b[o + 1] << 48) | ((ulong)b[o + 2] << 40) | ((ulong)b[o + 3] << 32)
         | ((ulong)b[o + 4] << 24) | ((ulong)b[o + 5] << 16) | ((ulong)b[o + 6] << 8) | b[o + 7];

    private static IPAddress DnsSafeResolve(string host)
    {
        if (IPAddress.TryParse(host, out var ip)) return ip;
        var addrs = Dns.GetHostAddresses(host);
        foreach (var a in addrs) if (a.AddressFamily == AddressFamily.InterNetwork) return a;
        return addrs.Length > 0 ? addrs[0] : IPAddress.Loopback;
    }

    public void Dispose()
    {
        StopUdp();
        _ = LeaveRoomAsync(CancellationToken.None);
    }
}
