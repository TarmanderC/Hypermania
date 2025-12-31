using System;
using MemoryPack;

namespace Netcode.Rollback.Network
{
    [Serializable]
    public struct ConnectionStatus
    {
        public bool Disconnected;
        public Frame LastFrame;

        public static readonly ConnectionStatus Default = new()
        {
            Disconnected = false,
            LastFrame = Frame.NullFrame
        };
    }

    [Serializable]
    public enum MessageKind : byte
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

    [Serializable]
    public struct MessageHeader
    {
        public ushort Magic;
    }

    [MemoryPackable]
    public partial struct MessageBody
    {
        [Serializable]
        public struct SyncRequest
        {
            public uint RandomRequest;
        }

        [Serializable]
        public struct SyncReply
        {
            public uint RandomReply;
        }

        [Serializable]
        public struct Input
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
        }

        [Serializable]
        public struct InputAck
        {
            public Frame AckFrame;

            public static readonly InputAck Default = new()
            {
                AckFrame = Frame.NullFrame
            };
        }

        [Serializable]
        public struct QualityReport
        {
            public short FrameAdvantage;
            public ulong Ping;
        }

        [Serializable]
        public struct QualityReply
        {
            public ulong Pong;
        }

        [Serializable]
        public struct ChecksumReport
        {
            public ulong Checksum;
            public Frame Frame;
        }

        [Serializable]
        public struct KeepAlive {}

        public MessageKind Kind;

        private SyncRequest _syncRequest;
        private SyncReply _syncReply;
        private Input _input;
        private InputAck _inputAck;
        private QualityReport _qualityReport;
        private QualityReply _qualityReply;
        private ChecksumReport _checksumReport;

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

        public static MessageBody From(in KeepAlive _) =>
            new() { Kind = MessageKind.KeepAlive };

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
    }

    [MemoryPackable]
    public partial struct Message
    {
        public MessageHeader Header;
        public MessageBody Body;
    }
}
