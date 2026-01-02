using System;
using System.Buffers.Binary;
using Netcode.Rollback.Sessions;

namespace Netcode.Rollback.Network
{
    public struct ConnectionStatus : ISerializable
    {
        public bool Disconnected;
        public Frame LastFrame;

        public static readonly ConnectionStatus Default = new()
        {
            Disconnected = false,
            LastFrame = Frame.NullFrame
        };
        public int Deserialize(ReadOnlySpan<byte> inBytes)
        {
            int ptr = 0;
            Disconnected = inBytes[ptr] != 0;
            ptr++;
            ptr += LastFrame.Deserialize(inBytes[ptr..]);
            return ptr;
        }
        public int Serialize(Span<byte> outBytes)
        {
            int ptr = 0;
            outBytes[ptr] = Disconnected ? (byte)1 : (byte)0;
            ptr++;
            ptr += LastFrame.Serialize(outBytes[ptr..]);
            return ptr;
        }
        public int SerdeSize() { return sizeof(byte) + LastFrame.SerdeSize(); }
    }

    public enum MessageKind : int
    {
        SyncRequest,
        SyncReply,
        Input,
        InputAck,
        QualityReport,
        QualityReply,
        ChecksumReport,
        KeepAlive,
    }

    public struct MessageHeader : ISerializable
    {
        public ushort Magic;

        public int Deserialize(ReadOnlySpan<byte> inBytes)
        {
            Magic = BinaryPrimitives.ReadUInt16LittleEndian(inBytes);
            return sizeof(ushort);
        }
        public int Serialize(Span<byte> outBytes)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(outBytes, Magic);
            return sizeof(ushort);
        }
        public int SerdeSize() { return sizeof(ushort); }
    }

    public struct MessageBody : ISerializable
    {
        public struct SyncRequest : ISerializable
        {
            public uint RandomRequest;

            public int Deserialize(ReadOnlySpan<byte> inBytes)
            {
                RandomRequest = BinaryPrimitives.ReadUInt32LittleEndian(inBytes);
                return sizeof(uint);
            }
            public int Serialize(Span<byte> outBytes)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(outBytes, RandomRequest);
                return sizeof(uint);
            }
            public int SerdeSize() { return sizeof(uint); }
        }

        public struct SyncReply : ISerializable
        {
            public uint RandomReply;

            public int Deserialize(ReadOnlySpan<byte> inBytes)
            {
                RandomReply = BinaryPrimitives.ReadUInt32LittleEndian(inBytes);
                return sizeof(uint);
            }
            public int Serialize(Span<byte> outBytes)
            {
                BinaryPrimitives.WriteUInt32LittleEndian(outBytes, RandomReply);
                return sizeof(uint);
            }
            public int SerdeSize() { return sizeof(uint); }
        }

        public struct Input : ISerializable
        {
            public ConnectionStatus[] PeerConnectStatus;
            public bool DisconnectRequested;
            public Frame StartFrame;
            public Frame AckFrame;
            public byte[] Bytes;

            public static readonly Input Default = new()
            {
                PeerConnectStatus = Array.Empty<ConnectionStatus>(),
                DisconnectRequested = false,
                StartFrame = Frame.NullFrame,
                AckFrame = Frame.NullFrame,
                Bytes = Array.Empty<byte>(),
            };

            public int Deserialize(ReadOnlySpan<byte> inBytes)
            {
                int ptr = 0;
                int numConnStat = BinaryPrimitives.ReadInt32LittleEndian(inBytes[ptr..]);
                ptr += sizeof(int);
                if (numConnStat > SessionConstants.MAX_NUM_PLAYERS) { throw new InvalidOperationException("too many connStats"); }
                PeerConnectStatus = new ConnectionStatus[numConnStat];

                for (int i = 0; i < numConnStat; i++)
                {
                    ptr += PeerConnectStatus[i].Deserialize(inBytes[ptr..]);
                }

                DisconnectRequested = inBytes[ptr] != 0;
                ptr++;

                ptr += StartFrame.Deserialize(inBytes[ptr..]);
                ptr += AckFrame.Deserialize(inBytes[ptr..]);

                int numBytes = BinaryPrimitives.ReadInt32LittleEndian(inBytes[ptr..]);
                ptr += sizeof(int);
                if (numBytes > SessionConstants.MAX_INPUT_PAYLOAD) { throw new InvalidOperationException("input payload too big"); }
                Bytes = new byte[numBytes];

                inBytes.Slice(ptr, numBytes).CopyTo(Bytes);
                ptr += numBytes;

                return ptr;
            }
            public int Serialize(Span<byte> outBytes)
            {
                int ptr = 0;
                if (PeerConnectStatus != null)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(outBytes[ptr..], PeerConnectStatus.Length);
                    ptr += sizeof(int);
                    for (int i = 0; i < PeerConnectStatus.Length; i++)
                    {
                        ptr += PeerConnectStatus[i].Serialize(outBytes[ptr..]);
                    }
                }
                else
                {
                    BinaryPrimitives.WriteInt32LittleEndian(outBytes[ptr..], 0);
                    ptr += sizeof(int);
                }


                outBytes[ptr] = DisconnectRequested ? (byte)1 : (byte)0;
                ptr++;

                ptr += StartFrame.Serialize(outBytes[ptr..]);
                ptr += AckFrame.Serialize(outBytes[ptr..]);

                if (Bytes != null)
                {
                    BinaryPrimitives.WriteInt32LittleEndian(outBytes[ptr..], Bytes.Length);
                    ptr += sizeof(int);

                    Bytes.AsSpan().CopyTo(outBytes[ptr..]);
                    ptr += Bytes.Length;
                }
                else
                {
                    BinaryPrimitives.WriteInt32LittleEndian(outBytes[ptr..], 0);
                    ptr += sizeof(int);
                }
                return ptr;
            }
            public int SerdeSize()
            {
                int cnt = 0;
                cnt += sizeof(int); // peer conn stat len
                for (int i = 0; i < PeerConnectStatus.Length; i++)
                {
                    cnt += PeerConnectStatus[i].SerdeSize();
                }
                cnt++; // disconnect requested
                cnt += StartFrame.SerdeSize();
                cnt += AckFrame.SerdeSize();
                cnt += sizeof(int); // num bytes
                cnt += Bytes.Length; // byte array
                return cnt;
            }
        }

        public struct InputAck : ISerializable
        {
            public Frame AckFrame;

            public static readonly InputAck Default = new()
            {
                AckFrame = Frame.NullFrame
            };

            public int Deserialize(ReadOnlySpan<byte> inBytes)
            {
                int ptr = 0;
                ptr += AckFrame.Deserialize(inBytes[ptr..]);
                return ptr;
            }
            public int Serialize(Span<byte> outBytes)
            {
                int ptr = 0;
                ptr += AckFrame.Serialize(outBytes[ptr..]);
                return ptr;
            }
            public int SerdeSize()
            {
                return AckFrame.SerdeSize();
            }
        }

        public struct QualityReport : ISerializable
        {
            public short FrameAdvantage;
            public ulong Ping;

            public int Deserialize(ReadOnlySpan<byte> inBytes)
            {
                int ptr = 0;
                FrameAdvantage = BinaryPrimitives.ReadInt16LittleEndian(inBytes[ptr..]);
                ptr += sizeof(short);
                Ping = BinaryPrimitives.ReadUInt64LittleEndian(inBytes[ptr..]);
                ptr += sizeof(ulong);
                return ptr;
            }
            public int Serialize(Span<byte> outBytes)
            {
                int ptr = 0;
                BinaryPrimitives.WriteInt16LittleEndian(outBytes[ptr..], FrameAdvantage);
                ptr += sizeof(short);
                BinaryPrimitives.WriteUInt64LittleEndian(outBytes[ptr..], Ping);
                ptr += sizeof(ulong);
                return ptr;
            }
            public int SerdeSize() { return sizeof(short) + sizeof(ulong); }
        }

        public struct QualityReply : ISerializable
        {
            public ulong Pong;

            public int Deserialize(ReadOnlySpan<byte> inBytes)
            {
                Pong = BinaryPrimitives.ReadUInt64LittleEndian(inBytes);
                return sizeof(ulong);
            }
            public int Serialize(Span<byte> outBytes)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(outBytes, Pong);
                return sizeof(ulong);
            }
            public int SerdeSize() { return sizeof(ulong); }
        }

        public struct ChecksumReport : ISerializable
        {
            public ulong Checksum;
            public Frame Frame;

            public int Deserialize(ReadOnlySpan<byte> inBytes)
            {
                int ptr = 0;
                Checksum = BinaryPrimitives.ReadUInt64LittleEndian(inBytes[ptr..]);
                ptr += sizeof(ulong);
                ptr += Frame.Deserialize(inBytes[ptr..]);
                return ptr;
            }
            public int Serialize(Span<byte> outBytes)
            {
                int ptr = 0;
                BinaryPrimitives.WriteUInt64LittleEndian(outBytes[ptr..], Checksum);
                ptr += sizeof(ulong);
                ptr += Frame.Serialize(outBytes[ptr..]);
                return ptr;
            }
            public int SerdeSize() { return sizeof(ulong) + Frame.SerdeSize(); }
        }

        public struct KeepAlive : ISerializable
        {
            public int Deserialize(ReadOnlySpan<byte> inBytes) { return 0; }
            public int Serialize(Span<byte> outBytes) { return 0; }
            public int SerdeSize() { return 0; }
        }

        public MessageKind Kind;

        private SyncRequest _syncRequest;
        private SyncReply _syncReply;
        private Input _input;
        private InputAck _inputAck;
        private QualityReport _qualityReport;
        private QualityReply _qualityReply;
        private ChecksumReport _checksumReport;
        private KeepAlive _keepAlive;

        public static MessageBody From(in SyncRequest body) =>
            new() { Kind = MessageKind.SyncRequest, _syncRequest = body };

        public static MessageBody From(in SyncReply body) =>
            new() { Kind = MessageKind.SyncReply, _syncReply = body };

        public static MessageBody From(in Input body) =>
            new() { Kind = MessageKind.Input, _input = body };

        public static MessageBody From(in InputAck body) =>
            new() { Kind = MessageKind.InputAck, _inputAck = body };

        public static MessageBody From(in QualityReport body) =>
            new() { Kind = MessageKind.QualityReport, _qualityReport = body };

        public static MessageBody From(in QualityReply body) =>
            new() { Kind = MessageKind.QualityReply, _qualityReply = body };

        public static MessageBody From(in ChecksumReport body) =>
            new() { Kind = MessageKind.ChecksumReport, _checksumReport = body };

        public static MessageBody From(in KeepAlive body) =>
            new() { Kind = MessageKind.KeepAlive, _keepAlive = body };

        public SyncRequest GetSyncRequest() =>
            Kind == MessageKind.SyncRequest ? _syncRequest : throw new InvalidOperationException("body type mismatch");

        public SyncReply GetSyncReply() =>
            Kind == MessageKind.SyncReply ? _syncReply : throw new InvalidOperationException("body type mismatch");

        public Input GetInput() =>
            Kind == MessageKind.Input ? _input : throw new InvalidOperationException("body type mismatch");

        public InputAck GetInputAck() =>
            Kind == MessageKind.InputAck ? _inputAck : throw new InvalidOperationException("body type mismatch");

        public QualityReport GetQualityReport() =>
            Kind == MessageKind.QualityReport ? _qualityReport : throw new InvalidOperationException("body type mismatch");

        public QualityReply GetQualityReply() =>
            Kind == MessageKind.QualityReply ? _qualityReply : throw new InvalidOperationException("body type mismatch");

        public ChecksumReport GetChecksumReport() =>
            Kind == MessageKind.ChecksumReport ? _checksumReport : throw new InvalidOperationException("body type mismatch");

        public KeepAlive GetKeepAlive() =>
            Kind == MessageKind.KeepAlive ? _keepAlive : throw new InvalidOperationException("body type mismatch");

        public int Deserialize(ReadOnlySpan<byte> inBytes)
        {
            int ptr = 0;
            Kind = (MessageKind)BinaryPrimitives.ReadInt32LittleEndian(inBytes[ptr..]);
            ptr += sizeof(int);
            switch (Kind)
            {
                case MessageKind.SyncRequest:
                    ptr += _syncRequest.Deserialize(inBytes[ptr..]);
                    break;
                case MessageKind.SyncReply:
                    ptr += _syncReply.Deserialize(inBytes[ptr..]);
                    break;
                case MessageKind.Input:
                    ptr += _input.Deserialize(inBytes[ptr..]);
                    break;
                case MessageKind.InputAck:
                    ptr += _inputAck.Deserialize(inBytes[ptr..]);
                    break;
                case MessageKind.QualityReport:
                    ptr += _qualityReport.Deserialize(inBytes[ptr..]);
                    break;
                case MessageKind.QualityReply:
                    ptr += _qualityReply.Deserialize(inBytes[ptr..]);
                    break;
                case MessageKind.ChecksumReport:
                    ptr += _checksumReport.Deserialize(inBytes[ptr..]);
                    break;
                case MessageKind.KeepAlive:
                    ptr += _keepAlive.Deserialize(inBytes[ptr..]);
                    break;
            }
            return ptr;
        }
        public int Serialize(Span<byte> outBytes)
        {
            int ptr = 0;
            BinaryPrimitives.WriteInt32LittleEndian(outBytes[ptr..], (int)Kind);
            ptr += sizeof(int);
            switch (Kind)
            {
                case MessageKind.SyncRequest:
                    ptr += _syncRequest.Serialize(outBytes[ptr..]);
                    break;
                case MessageKind.SyncReply:
                    ptr += _syncReply.Serialize(outBytes[ptr..]);
                    break;
                case MessageKind.Input:
                    ptr += _input.Serialize(outBytes[ptr..]);
                    break;
                case MessageKind.InputAck:
                    ptr += _inputAck.Serialize(outBytes[ptr..]);
                    break;
                case MessageKind.QualityReport:
                    ptr += _qualityReport.Serialize(outBytes[ptr..]);
                    break;
                case MessageKind.QualityReply:
                    ptr += _qualityReply.Serialize(outBytes[ptr..]);
                    break;
                case MessageKind.ChecksumReport:
                    ptr += _checksumReport.Serialize(outBytes[ptr..]);
                    break;
                case MessageKind.KeepAlive:
                    ptr += _keepAlive.Serialize(outBytes[ptr..]);
                    break;
            }
            return ptr;
        }
        public int SerdeSize()
        {
            int cnt = 0;
            cnt += sizeof(int);
            switch (Kind)
            {
                case MessageKind.SyncRequest:
                    cnt += _syncRequest.SerdeSize();
                    break;
                case MessageKind.SyncReply:
                    cnt += _syncReply.SerdeSize();
                    break;
                case MessageKind.Input:
                    cnt += _input.SerdeSize();
                    break;
                case MessageKind.InputAck:
                    cnt += _inputAck.SerdeSize();
                    break;
                case MessageKind.QualityReport:
                    cnt += _qualityReport.SerdeSize();
                    break;
                case MessageKind.QualityReply:
                    cnt += _qualityReply.SerdeSize();
                    break;
                case MessageKind.ChecksumReport:
                    cnt += _checksumReport.SerdeSize();
                    break;
                case MessageKind.KeepAlive:
                    cnt += _keepAlive.SerdeSize();
                    break;
            }
            return cnt;
        }
    }

    public struct Message : ISerializable
    {
        public MessageHeader Header;
        public MessageBody Body;

        public int Deserialize(ReadOnlySpan<byte> inBytes)
        {
            int ptr = 0;
            ptr += Header.Deserialize(inBytes[ptr..]);
            ptr += Body.Deserialize(inBytes[ptr..]);
            return ptr;
        }
        public int Serialize(Span<byte> outBytes)
        {
            int ptr = 0;
            ptr += Header.Serialize(outBytes[ptr..]);
            ptr += Body.Serialize(outBytes[ptr..]);
            return ptr;
        }
        public int SerdeSize()
        {
            return Header.SerdeSize() + Body.SerdeSize();
        }
    }
}
