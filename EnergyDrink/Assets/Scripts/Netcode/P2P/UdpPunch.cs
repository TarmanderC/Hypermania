using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Netcode.P2P
{
    public static class UdpPunch
    {
        // 1 byte kind + 8 byte nonce
        private const int MSG_SIZE = 1 + 8;

        private const byte PKT_PUNCH = 0x04;
        private const byte PKT_ACK = 0x05;
        private static Random _random = new Random();

        public static async Task<IPEndPoint> PunchAsync(
            UdpClient localUdp,
            IPEndPoint peerPublicEp,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            using var timeoutCts = new CancellationTokenSource(timeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            ulong myNonce = RandomNonce64();

            // Shared state across send/recv loops
            var state = new PunchState(myNonce, peerPublicEp);

            var recvTask = ReceiveLoopAsync(localUdp, state, linked.Token);
            var sendTask = SendLoopAsync(localUdp, state, linked.Token);

            // Wait until ack is accomplished or timeouts/cancel
            var winner = await Task.WhenAny(state.Acked.Task, recvTask, sendTask).ConfigureAwait(false);

            linked.Cancel();

            if (winner == state.Acked.Task && state.Acked.Task.Status == TaskStatus.RanToCompletion)
                return state.ConfirmedPeerEp; // set when ack is validated

            return null;
        }

        private sealed class PunchState
        {
            public readonly ulong MyNonce;
            public readonly IPEndPoint ExpectedPeerIp; // used for loose filtering (IP match)
            public readonly TaskCompletionSource<bool> Acked;

            // Set when we receive a valid ack for MyNonce
            public IPEndPoint ConfirmedPeerEp;

            // 0/1 flags (Interlocked)
            private int _gotAckForMine;
            private int _sawPeerPunch;

            public PunchState(ulong myNonce, IPEndPoint peerPublicEp)
            {
                MyNonce = myNonce;
                ExpectedPeerIp = peerPublicEp;
                Acked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            public bool GotAckForMine => Volatile.Read(ref _gotAckForMine) == 1;
            public bool SawPeerPunch => Volatile.Read(ref _sawPeerPunch) == 1;

            public void MarkGotAck(IPEndPoint from)
            {
                if (Interlocked.Exchange(ref _gotAckForMine, 1) == 0)
                {
                    ConfirmedPeerEp = from;
                    Acked.TrySetResult(true);
                }
            }

            public void MarkSawPeerPunch()
            {
                Interlocked.Exchange(ref _sawPeerPunch, 1);
            }
        }

        private static async Task ReceiveLoopAsync(UdpClient udp, PunchState state, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && !state.GotAckForMine)
            {
                UdpReceiveResult res;
                try
                {
                    var recv = udp.ReceiveAsync();
                    var done = await Task.WhenAny(recv, Task.Delay(50, ct)).ConfigureAwait(false);
                    if (done != recv) continue;

                    res = recv.Result;
                }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch { continue; }

                var from = res.RemoteEndPoint;

                // Loose filter: accept only from same IP as the peer we were told (port may differ on some NATs).
                if (!from.Address.Equals(state.ExpectedPeerIp.Address))
                    continue;

                if (!TryParse(res.Buffer, out byte kind, out ulong nonce))
                    continue;

                if (kind == PKT_PUNCH)
                {
                    // Peer is punching us. Ack back to the *actual* endpoint we observed.
                    state.MarkSawPeerPunch();

                    byte[] ack = new byte[MSG_SIZE];
                    WriteMsg(ack.AsSpan(), PKT_ACK, nonce);
                    try
                    {
                        await udp.SendAsync(ack, MSG_SIZE, from).ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException) { break; }
                    catch { /* ignore */ }

                    // Optional: continue listening; we only finish once we get an ACK for *our* nonce.
                    continue;
                }

                if (kind == PKT_ACK)
                {
                    // Only counts if it acks our nonce.
                    if (nonce == state.MyNonce)
                    {
                        state.MarkGotAck(from);
                        break;
                    }
                }
            }
        }

        private static async Task SendLoopAsync(UdpClient udp, PunchState state, CancellationToken ct)
        {
            int[] scheduleMs = new[] { 20, 20, 20, 20, 30, 30, 40, 50, 75, 100, 125, 150, 200 };

            byte[] punch = new byte[MSG_SIZE];
            WriteMsg(punch.AsSpan(), PKT_PUNCH, state.MyNonce);

            int i = 0;
            while (!ct.IsCancellationRequested && !state.GotAckForMine)
            {
                try
                {
                    await udp.SendAsync(punch, punch.Length, state.ExpectedPeerIp).ConfigureAwait(false);
                }
                catch (ObjectDisposedException) { break; }
                catch { /* ignore transient send errors */ }

                int delay = i < scheduleMs.Length ? scheduleMs[i++] : 250;
                try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        private static bool TryParse(byte[] buf, out byte kind, out ulong nonce)
        {
            kind = 0;
            nonce = 0;

            if (buf == null || buf.Length < MSG_SIZE) return false;

            kind = buf[0];
            nonce = BinaryPrimitives.ReadUInt64LittleEndian(buf[1..]);
            return true;
        }

        private static void WriteMsg(Span<byte> dst, byte kind, ulong nonce)
        {
            dst[0] = kind;
            BinaryPrimitives.WriteUInt64LittleEndian(dst[1..], nonce);
        }

        private static ulong RandomNonce64()
        {
            Span<byte> b = stackalloc byte[8];
            _random.NextBytes(b);
            return BinaryPrimitives.ReadUInt64LittleEndian(b);
        }
    }
}
