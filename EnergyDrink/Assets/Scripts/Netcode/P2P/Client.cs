using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Netcode.Rollback;
using Netcode.Rollback.Network;
using UnityEngine;

namespace Netcode.P2P
{
    public enum ClientState
    {
        Initialized,
        Matchmaking,
        MatchmakingReady,
        AttemptDirect,
        AttemptUPnP,
        AttemptPunchthrough,
        Connected,
        Disposed,
    }

    public enum InWsEventKind
    {
        JoinedRoom = 1,
        YouAre = 2,
        PeerJoined = 3,
        PeerLeft = 4,
        StartedGame = 5,
    }

    public enum OutWsEventKind
    {
        StartGame = 1,
    }

    public readonly struct InWsEvent
    {
        public readonly InWsEventKind Kind;
        public readonly ulong RoomId;
        public readonly uint Handle;
        public readonly EndPoint Endpoint;

        private InWsEvent(InWsEventKind type, ulong roomId, uint handle, EndPoint endPoint)
        {
            Kind = type;
            RoomId = roomId;
            Handle = handle;
            Endpoint = endPoint;
        }

        public static InWsEvent JoinedRoom(ulong roomId) => new InWsEvent(InWsEventKind.JoinedRoom, roomId, 0, null);
        public static InWsEvent YouAre(uint handle) => new InWsEvent(InWsEventKind.YouAre, 0, handle, null);
        public static InWsEvent PeerJoined(uint handle) => new InWsEvent(InWsEventKind.PeerJoined, 0, handle, null);
        public static InWsEvent PeerLeft(uint handle) => new InWsEvent(InWsEventKind.PeerLeft, 0, handle, null);
        public static InWsEvent GameStarted(EndPoint endpoint) => new InWsEvent(InWsEventKind.StartedGame, 0, 0, endpoint);
    }

    public sealed class SynapseClient : IDisposable, INonBlockingSocket<EndPoint>
    {
        private const byte UDP_FOUND_PEER = 0x1;
        private const byte UDP_WAITING = 0x2;

        private const byte UDP_BIND = 0x1;
        private const byte UDP_RELAY = 0x3;

        public const int RECV_BUFFER_SIZE = 4096;
        public const int IDEAL_MAX_UDP_PACKET_SIZE = 508;

        // identity
        public Guid ClientGuid { get; }
        public string ClientIdDecimal { get; }

        // server configuration
        private readonly Uri _baseHttpWs;
        private readonly IPEndPoint _relayEp;

        // matchmaking websocket
        private ClientWebSocket _ws;
        private CancellationTokenSource _wsCts;
        private Task<WebSocketReceiveResult> _wsRecvTask;
        private int _wsRecvCount;

        // pump buffers/state
        private readonly byte[] _wsBuf = new byte[2048];

        // udp
        private UdpClient _udp;

        // state
        public ClientState State { get; private set; }

        // rollback recv buffer
        private readonly byte[] _buffer;

        public SynapseClient(string host, int httpPort = 9000, int relayPort = 9001)
        {
            _baseHttpWs = new Uri($"ws://{host}:{httpPort}");
            _relayEp = new IPEndPoint(DnsSafeResolve(host), relayPort);

            _buffer = new byte[RECV_BUFFER_SIZE];

            ClientGuid = Guid.NewGuid();
            ClientIdDecimal = GuidToU128Decimal(ClientGuid);

            _udp = new UdpClient(0);
            _udp.Client.Blocking = false;

            State = ClientState.Initialized;
        }

        public void Dispose()
        {
            if (State == ClientState.Disposed) return;
            State = ClientState.Disposed;

            try { _wsCts?.Cancel(); } catch { }

            try { _ws?.Dispose(); } catch { }
            _ws = null;

            try { _wsCts?.Dispose(); } catch { }
            _wsCts = null;

            try { _udp?.Close(); } catch { }
            try { _udp?.Dispose(); } catch { }
            _udp = null;
        }

        #region Matchmaking

        public async Task StartGame(CancellationToken ct = default)
        {
            EnsureState(ClientState.MatchmakingReady);

            if (_ws == null)
                throw new InvalidOperationException("WebSocket is not connected.");
            if (_ws.State != WebSocketState.Open)
                throw new InvalidOperationException($"WebSocket is not open (state = {_ws.State}).");

            byte[] payload = { (byte)OutWsEventKind.StartGame };
            await _ws.SendAsync(
                new ArraySegment<byte>(payload),
                WebSocketMessageType.Binary,
                endOfMessage: true,
                cancellationToken: ct
            );
        }

        public async Task CreateRoomAsync(CancellationToken ct = default)
        {
            EnsureState(ClientState.Initialized);
            await ConnectWsAsync(new Uri(_baseHttpWs, $"/create_room?client_id={ClientIdDecimal}"), ct);
        }

        public async Task JoinRoomAsync(ulong roomId, CancellationToken ct = default)
        {
            EnsureState(ClientState.Initialized);
            await ConnectWsAsync(new Uri(_baseHttpWs, $"/join_room/{roomId}?client_id={ClientIdDecimal}"), ct);
        }

        public async Task LeaveRoomAsync(CancellationToken ct = default)
        {
            if (State == ClientState.Disposed) return;

            try { _wsCts?.Cancel(); } catch { }

            var ws = _ws;
            _ws = null;

            if (ws != null)
            {
                try
                {
                    if (ws.State == WebSocketState.Open || ws.State == WebSocketState.CloseReceived)
                        await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "leave", ct);
                }
                catch { }
                finally
                {
                    try { ws.Dispose(); } catch { }
                }
            }

            try { _wsCts?.Dispose(); } catch { }
            _wsCts = null;
            State = ClientState.Initialized;
        }

        private async Task ConnectWsAsync(Uri wsUri, CancellationToken ct)
        {
            _ws = new ClientWebSocket();
            _ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);

            _wsCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            await _ws.ConnectAsync(wsUri, _wsCts.Token);
        }

        public async Task<List<InWsEvent>> PumpWebSocket()
        {
            var events = new List<InWsEvent>(8);

            if (State == ClientState.Disposed) return events;

            var ws = _ws;
            if (ws == null) return events;

            if (ws.State != WebSocketState.Open && ws.State != WebSocketState.CloseReceived)
                return events;

            if (_wsRecvTask == null)
            {
                if (_wsCts == null) return events;
                _wsRecvTask = ws.ReceiveAsync(new ArraySegment<byte>(_wsBuf), _wsCts.Token);
            }

            if (!_wsRecvTask.IsCompleted)
                return events;

            if (_wsRecvTask.IsCanceled || _wsRecvTask.IsFaulted)
            {
                _wsRecvTask = null;
                return events;
            }

            WebSocketReceiveResult res = _wsRecvTask.Result;
            _wsRecvTask = null;

            if (res.MessageType == WebSocketMessageType.Close)
                return events;

            if (res.MessageType != WebSocketMessageType.Binary)
                return events;

            if (!res.EndOfMessage)
                throw new InvalidOperationException("WS message fragmented; implement reassembly.");

            await TryHandleWsBinaryToEvents(_wsBuf, res.Count, events);
            return events;
        }

        private async Task<bool> TryHandleWsBinaryToEvents(byte[] data, int n, List<InWsEvent> outEvents)
        {
            if (n < 1) return false;

            byte tag = data[0];
            switch (tag)
            {
                case (int)InWsEventKind.JoinedRoom:
                    if (n < 1 + 8) return false;
                    {
                        ulong room = ReadU64BE(data, 1);
                        State = ClientState.Matchmaking;
                        outEvents.Add(InWsEvent.JoinedRoom(room));
                        return true;
                    }

                case (int)InWsEventKind.YouAre:
                    if (n < 1 + 4) return false;
                    {
                        uint handle = ReadU32BE(data, 1);
                        outEvents.Add(InWsEvent.YouAre(handle));
                        return true;
                    }

                case (int)InWsEventKind.PeerJoined:
                    if (n < 1 + 4) return false;
                    {
                        uint h = ReadU32BE(data, 1);
                        EnsureState(ClientState.Matchmaking);
                        State = ClientState.MatchmakingReady;
                        outEvents.Add(InWsEvent.PeerJoined(h));
                        return true;
                    }

                case (int)InWsEventKind.PeerLeft:
                    if (n < 1 + 4) return false;
                    {
                        uint h = ReadU32BE(data, 1);
                        if (State == ClientState.MatchmakingReady)
                            State = ClientState.Matchmaking;
                        outEvents.Add(InWsEvent.PeerLeft(h));
                        return true;
                    }
                case (int)InWsEventKind.StartedGame:
                    {
                        EndPoint ep = await ConnectAsync();
                        outEvents.Add(InWsEvent.GameStarted(ep));
                        return true;
                    }

                default:
                    return false;
            }
        }

        #endregion

        #region Connecting

        public async Task<IPEndPoint> ConnectAsync(CancellationToken ct = default)
        {
            EnsureState(ClientState.MatchmakingReady);
            var peerEp = await GetPeerEpAsync(ct);
            try
            {
                Debug.Log("[Connect] Attempting to connect directly...");
                State = ClientState.AttemptDirect;
                var ep = await TryDirectConnect(peerEp, ct);
                State = ClientState.Connected;
                Debug.Log("[Connect] Connected directly");
                return ep;
            }
            catch (Exception e)
            {
                Debug.Log($"[Connect] Failed to connect directly: {e.Message}");
            }

            try
            {
                Debug.Log("[Connect] Attempting to connect using UPnP...");
                State = ClientState.AttemptUPnP;
                var ep = await TryUPnP(peerEp, ct);
                State = ClientState.Connected;
                Debug.Log("[Connect] Connected directly (using UPnP)");
                return ep;
            }
            catch (Exception e)
            {
                Debug.Log($"[Connect] Failed to connect using UPnP: {e.Message}");
            }

            try
            {
                Debug.Log("[Connect] Attempting to punchthrough...");
                State = ClientState.AttemptPunchthrough;
                var ep = await TryPunch(peerEp, TimeSpan.FromSeconds(10), ct);
                State = ClientState.Connected;
                Debug.Log("[Connect] Connected using punchthrough");
                return ep;
            }
            catch (Exception e)
            {
                Debug.Log($"[Connect] Failed to punchthrough: {e.Message}");
            }

            Debug.Log("[Connect] Connected using relay");
            State = ClientState.Connected;
            return _relayEp;
        }

        private async Task<IPEndPoint> GetPeerEpAsync(CancellationToken ct)
        {
            // New format: [BindByte][clientId(16 bytes BE)]
            byte[] bindPkt = BuildBindPacket();

            const int SEND_INTERVAL_MS = 100;
            const int RECV_TIMEOUT_MS = 300;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (State == ClientState.Disposed)
                    throw new ObjectDisposedException(nameof(SynapseClient));

                await _udp.SendAsync(bindPkt, bindPkt.Length, _relayEp);

                var recvTask = _udp.ReceiveAsync();
                var delayTask = Task.Delay(RECV_TIMEOUT_MS, ct);

                var done = await Task.WhenAny(recvTask, delayTask);
                if (done != recvTask)
                {
                    await Task.Delay(SEND_INTERVAL_MS, ct);
                    continue;
                }

                UdpReceiveResult recv = recvTask.Result;

                if (!EndPointMatches(recv.RemoteEndPoint, _relayEp))
                    continue;

                byte[] data = recv.Buffer;
                int n = data?.Length ?? 0;
                if (n < 1) continue;

                byte tag = data[0];

                if (tag == UDP_WAITING)
                {
                    await Task.Delay(SEND_INTERVAL_MS, ct);
                    continue;
                }

                if (tag != UDP_FOUND_PEER) continue;
                if (n < 2) continue;

                byte ipVer = data[1];

                if (ipVer == 4)
                {
                    if (n < 8) continue;

                    var ipBytes = new byte[4];
                    Buffer.BlockCopy(data, 2, ipBytes, 0, 4);
                    ushort port = ReadU16BE(data, 6);

                    return new IPEndPoint(new IPAddress(ipBytes), port);
                }

                if (ipVer == 6)
                {
                    if (n < 20) continue;

                    var ipBytes = new byte[16];
                    Buffer.BlockCopy(data, 2, ipBytes, 0, 16);
                    ushort port = ReadU16BE(data, 18);

                    return new IPEndPoint(new IPAddress(ipBytes), port);
                }
            }
        }

        private async Task<IPEndPoint> TryDirectConnect(IPEndPoint peer, CancellationToken ct)
        {
            throw new NotImplementedException("Not implemented yet");
        }

        private async Task<IPEndPoint> TryUPnP(IPEndPoint peer, CancellationToken ct)
        {
            throw new NotImplementedException("Not implemented yet");
        }

        private async Task<IPEndPoint> TryPunch(IPEndPoint peer, TimeSpan timeout, CancellationToken ct)
        {
            return await UdpPunch.PunchAsync(_udp, peer, timeout, ct);
        }

        #endregion

        #region Rollback

        public void SendTo(in Message message, EndPoint addr)
        {
            byte[] pkt = new byte[message.SerdeSize() + 1];
            pkt[0] = UDP_RELAY;
            message.Serialize(pkt.AsSpan()[1..]);
            if (pkt.Length > IDEAL_MAX_UDP_PACKET_SIZE)
            {
                Debug.Log($"Sending UDP packet of size {pkt.Length} bytes, which is larger than ideal ({IDEAL_MAX_UDP_PACKET_SIZE}).");
            }
            _udp.Client.SendTo(pkt, SocketFlags.None, addr);
        }

        public List<(EndPoint addr, Message message)> ReceiveAllMessages()
        {
            var received = new List<(EndPoint, Message)>();

            while (true)
            {
                EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                try
                {
                    int bytes = _udp.Client.ReceiveFrom(_buffer, 0, _buffer.Length, SocketFlags.None, ref remote);
                    if (bytes <= 0) continue;
                    if (bytes > RECV_BUFFER_SIZE)
                        throw new InvalidOperationException("Received more bytes than buffer size.");

                    // Incoming relay from server is: [RelayByte][payload...]
                    if (bytes < 2) continue;

                    int offset = 0;
                    int len = bytes;

                    if (_buffer[0] == UDP_RELAY)
                    {
                        offset = 1;
                        len = bytes - 1;
                    }
                    else
                    {
                        continue;
                    }

                    Message message = default;
                    int parsed = message.Deserialize(_buffer.AsSpan().Slice(offset, len));
                    if (parsed != len)
                    {
                        throw new InvalidOperationException("Received more bytes than message required");
                    }
                    received.Add((remote, message));
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.WouldBlock)
                {
                    return received;
                }
                catch (SocketException ex) when (ex.SocketErrorCode == SocketError.ConnectionReset)
                {
                    continue;
                }
                catch (SocketException ex)
                {
                    throw new InvalidOperationException(
                        $"{ex.SocketErrorCode}: {ex.Message} on {_udp.Client.LocalEndPoint}", ex);
                }
            }
        }

        #endregion

        #region Utils

        private static bool EndPointMatches(IPEndPoint a, IPEndPoint b)
            => a.Port == b.Port && a.Address.Equals(b.Address);

        private void EnsureState(ClientState expected)
        {
            if (State != expected)
                throw new InvalidOperationException($"expected state {expected} but was {State}.");
        }

        private static string GuidToU128Decimal(Guid g)
        {
            string hex = g.ToString("N");
            byte[] be16 = new byte[16];
            for (int i = 0; i < 16; i++)
                be16[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);

            byte[] le = new byte[17];
            for (int i = 0; i < 16; i++) le[i] = be16[15 - i];
            le[16] = 0;

            var bi = new BigInteger(le);
            return bi.ToString();
        }

        private byte[] BuildClientIdBytesBE()
            => U128DecimalTo16BytesBE(ClientIdDecimal);

        private byte[] BuildBindPacket()
        {
            byte[] id = BuildClientIdBytesBE(); // 16
            byte[] pkt = new byte[1 + 16];
            pkt[0] = UDP_BIND;
            Buffer.BlockCopy(id, 0, pkt, 1, 16);
            return pkt;
        }

        private static byte[] U128DecimalTo16BytesBE(string dec)
        {
            BigInteger bi = BigInteger.Parse(dec);

            byte[] le = bi.ToByteArray(isUnsigned: true, isBigEndian: false);

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
            foreach (var a in addrs)
                if (a.AddressFamily == AddressFamily.InterNetwork)
                    return a;

            return addrs.Length > 0 ? addrs[0] : IPAddress.Loopback;
        }

        #endregion
    }
}
