using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using Netcode.Rollback.Network;

namespace Netcode.Rollback
{
    public struct Frame : IComparable<Frame>, IEquatable<Frame>, IFormattable, ISerializable
    {
        public int No;
        public static readonly Frame NullFrame = new Frame { No = -1 };
        public static readonly Frame FirstFrame = new Frame { No = 0 };

        public int CompareTo(Frame other) =>
            No.CompareTo(other.No);

        public bool Equals(Frame other) =>
            No == other.No;

        public override bool Equals(object obj) =>
            obj is Frame other && Equals(other);

        public readonly override int GetHashCode() =>
            No.GetHashCode();

        public static Frame operator +(Frame left, int right) => new Frame { No = left.No + right };
        public static Frame operator -(Frame left, int right) => new Frame { No = left.No - right };
        public static int operator -(Frame left, Frame right) => left.No - right.No;

        public static bool operator ==(Frame left, Frame right) =>
            left.Equals(right);

        public static bool operator !=(Frame left, Frame right) =>
            !left.Equals(right);

        public static bool operator <(Frame a, Frame b) =>
            a.CompareTo(b) < 0;

        public static bool operator >(Frame a, Frame b) =>
            a.CompareTo(b) > 0;

        public static bool operator <=(Frame a, Frame b) =>
            a.CompareTo(b) <= 0;

        public static bool operator >=(Frame a, Frame b) =>
            a.CompareTo(b) >= 0;

        public static Frame Max(Frame a, Frame b) => a.No < b.No ? b : a;
        public static Frame Min(Frame a, Frame b) => a.No > b.No ? b : a;
        public override string ToString() => No.ToString();

        public string ToString(string format, IFormatProvider formatProvider) => No.ToString(format, formatProvider);

        public int Deserialize(ReadOnlySpan<byte> inBytes)
        {
            No = BinaryPrimitives.ReadInt32LittleEndian(inBytes);
            return sizeof(int);
        }
        public int Serialize(Span<byte> outBytes)
        {
            BinaryPrimitives.WriteInt32LittleEndian(outBytes, No);
            return sizeof(int);
        }
        public int SerdeSize() { return sizeof(int); }
    }


    public readonly struct PlayerHandle : IFormattable
    {
        public readonly int Id;

        public PlayerHandle(int id)
        {
            if (id < 0) throw new ArgumentOutOfRangeException("id cannot be neg");
            Id = id;
        }

        public override string ToString() => Id.ToString();
        public string ToString(string format, IFormatProvider formatProvider) => Id.ToString(format, formatProvider);
    }

    public struct DesyncDetection
    {
        public bool On;
        public uint Interval;
    }

    public enum PlayerKind
    {
        Local,
        Remote,
        Spectator,
    }

    public struct PlayerType<TAddress>
    {
        public PlayerKind Kind;
        public TAddress Address;
    }

    public enum SessionState
    {
        Synchronizing,
        Running
    }

    public enum InputStatus
    {
        Confirmed,
        Predicted,
        Disconnected,
    }

    public enum RollbackEventKind
    {
        Synchronizing,
        Synchronized,
        Disconnected,
        NetworkInterrupted,
        NetworkResumed,
        WaitRecommendation,
        DesyncDetected,
    }

    public struct RollbackEvent<TInput, TAddress> where TInput : IInput<TInput>
    {
        public struct Synchronizing
        {
            public TAddress Addr;
            public uint Total;
            public uint Count;
        }

        public struct Synchronized
        {
            public TAddress Addr;
        }

        public struct Disconnected
        {
            public TAddress Addr;
        }

        public struct NetworkInterrupted
        {
            public TAddress Addr;
            public ulong DisconnectTimeout;
        }

        public struct NetworkResumed
        {
            public TAddress Addr;
        }
        public struct WaitRecommendation
        {
            public uint SkipFrames;
        }

        public struct DesyncDetected
        {
            public Frame Frame;
            public ulong LocalChecksum;
            public ulong RemoteChecksum;
            public TAddress Addr;
        }

        public RollbackEventKind Kind;

        private Synchronizing _synchronizing;
        private Synchronized _synchronized;
        private Disconnected _disconnected;
        private NetworkInterrupted _networkInterrupted;
        private NetworkResumed _networkResumed;
        private WaitRecommendation _waitRecommendation;
        private DesyncDetected _desyncDetected;

        public static RollbackEvent<TInput, TAddress> From(in Synchronizing e) =>
            new() { Kind = RollbackEventKind.Synchronizing, _synchronizing = e };

        public static RollbackEvent<TInput, TAddress> From(in Synchronized e) =>
            new() { Kind = RollbackEventKind.Synchronized, _synchronized = e };

        public static RollbackEvent<TInput, TAddress> From(in Disconnected e) =>
            new() { Kind = RollbackEventKind.Disconnected, _disconnected = e };

        public static RollbackEvent<TInput, TAddress> From(in NetworkInterrupted e) =>
            new() { Kind = RollbackEventKind.NetworkInterrupted, _networkInterrupted = e };

        public static RollbackEvent<TInput, TAddress> From(in NetworkResumed e) =>
            new() { Kind = RollbackEventKind.NetworkResumed, _networkResumed = e };

        public static RollbackEvent<TInput, TAddress> From(in WaitRecommendation e) =>
            new() { Kind = RollbackEventKind.WaitRecommendation, _waitRecommendation = e };

        public static RollbackEvent<TInput, TAddress> From(in DesyncDetected e) =>
            new() { Kind = RollbackEventKind.DesyncDetected, _desyncDetected = e };

        public Synchronizing GetSynchronizing() =>
            Kind == RollbackEventKind.Synchronizing ? _synchronizing : throw new System.InvalidOperationException("event type mismatch");

        public Synchronized GetSynchronized() =>
            Kind == RollbackEventKind.Synchronized ? _synchronized : throw new System.InvalidOperationException("event type mismatch");

        public Disconnected GetDisconnected() =>
            Kind == RollbackEventKind.Disconnected ? _disconnected : throw new System.InvalidOperationException("event type mismatch");

        public NetworkInterrupted GetNetworkInterrupted() =>
            Kind == RollbackEventKind.NetworkInterrupted ? _networkInterrupted : throw new System.InvalidOperationException("event type mismatch");

        public NetworkResumed GetNetworkResumed() =>
            Kind == RollbackEventKind.NetworkResumed ? _networkResumed : throw new System.InvalidOperationException("event type mismatch");

        public WaitRecommendation GetWaitRecommendation() =>
            Kind == RollbackEventKind.WaitRecommendation ? _waitRecommendation : throw new System.InvalidOperationException("event type mismatch");

        public DesyncDetected GetDesyncDetected() =>
            Kind == RollbackEventKind.DesyncDetected ? _desyncDetected : throw new System.InvalidOperationException("event type mismatch");
    }


    public enum RollbackRequestKind
    {
        SaveGameStateReq,
        LoadGameStateReq,
        AdvanceFrameReq,
    }

    public struct RollbackRequest<TState, TInput>
        where TState : struct
        where TInput : IInput<TInput>
    {
        public struct SaveGameState
        {
            public GameStateCell<TState> Cell;
            public Frame Frame;
        }

        public struct LoadGameState
        {
            public GameStateCell<TState> Cell;
            public Frame Frame;
        }

        public struct AdvanceFrame
        {
            public (TInput input, InputStatus status)[] Inputs;
        }

        public RollbackRequestKind Kind;
        private SaveGameState _saveStateReq;
        private LoadGameState _loadStateReq;
        private AdvanceFrame _advanceFrameReq;

        public static RollbackRequest<TState, TInput> From(in SaveGameState body) =>
            new() { Kind = RollbackRequestKind.SaveGameStateReq, _saveStateReq = body };

        public static RollbackRequest<TState, TInput> From(in LoadGameState body) =>
            new() { Kind = RollbackRequestKind.LoadGameStateReq, _loadStateReq = body };

        public static RollbackRequest<TState, TInput> From(in AdvanceFrame body) =>
            new() { Kind = RollbackRequestKind.AdvanceFrameReq, _advanceFrameReq = body };

        public SaveGameState GetSaveGameStateReq() =>
            Kind == RollbackRequestKind.SaveGameStateReq ? _saveStateReq : throw new InvalidOperationException("body type mismatch");

        public LoadGameState GetLoadGameStateReq() =>
            Kind == RollbackRequestKind.LoadGameStateReq ? _loadStateReq : throw new InvalidOperationException("body type mismatch");

        public AdvanceFrame GetAdvanceFrameRequest() =>
            Kind == RollbackRequestKind.AdvanceFrameReq ? _advanceFrameReq : throw new InvalidOperationException("body type mismatch");
    }

    public interface IInput<TSelf> : IEquatable<TSelf>, ISerializable { }
    public interface IState<TSelf> { }
    public interface IAddress<TSelf> { }

    public interface ISerializable
    {
        public abstract int SerdeSize();
        public abstract int Serialize(Span<byte> outBytes);
        public abstract int Deserialize(ReadOnlySpan<byte> inBytes);
        public void EnsureBufferSize(ReadOnlySpan<byte> buf)
        {
            if (buf.Length < SerdeSize()) { throw new ArgumentException("input buffer too small"); }
        }
    }

    public static class Serializer<T> where T : ISerializable
    {
        private readonly static T _sample = default;
        public static int DefaultSize() { return _sample.SerdeSize(); }
    }

    public interface INonBlockingSocket<TAddress>
    {
        /// <summary>
        /// Takes a message and sends it to the given address
        /// </summary>
        /// <param name="message">The message to send</param>
        /// <param name="addr">The address to send it to</param>
        public abstract void SendTo(in Message message, TAddress addr);

        /// <summary>
        /// This method should return all messages received since the last time this method was called
        /// </summary>
        /// <returns>An list of pairs (addr, message) indicating the source and content of the message</returns>
        public abstract List<(TAddress addr, Message message)> ReceiveAllMessages();
    }
}
