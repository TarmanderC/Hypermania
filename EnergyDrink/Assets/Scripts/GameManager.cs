using System;
using System.Collections.Generic;
using System.Net;
using Netcode.P2P;
using Netcode.Rollback;
using Netcode.Rollback.Sessions;
using UnityEngine;

public class GameManager : MonoBehaviour
{
    [Header("Servers")]
    public string ServerIp = "144.126.152.174";
    public int HttpPort = 9000;
    public int RelayPort = 9002;

    [SerializeField]
    private GameObject _bob1;
    [SerializeField]
    private GameObject _bob2;
    private GameState _curState;
    private P2PSession<GameState, Input, EndPoint> _session;
    private SynapseClient _synapse;

    // matchmaking 
    private int? _handle;
    private int? _opponentHandle;
    private ulong? _roomId;

    private bool _playing;

    //rollback
    private uint _waitRemaining;

    void Awake()
    {
        _synapse = new SynapseClient(ServerIp, HttpPort, RelayPort);
        _playing = false;
        _handle = null;
        _roomId = null;
        _opponentHandle = null;
    }

    async void OnDestroy()
    {
        if (_synapse != null)
        {
            await _synapse.LeaveRoomAsync();
            _synapse.Dispose();
        }
        _handle = null;
        _opponentHandle = null;
        _roomId = null;
        _session = null;
        _playing = false;
        _curState = GameState.New();
    }

    void FixedUpdate()
    {
        NetworkingLoop();
        if (!_playing)
        {
            return;
        }
        GameLoop();
    }

    void NetworkingLoop()
    {
        List<InWsEvent> events = _synapse.PumpWebSocket();
        foreach (InWsEvent ev in events)
        {
            switch (ev.Kind)
            {
                case InWsEventKind.JoinedRoom:
                    _roomId = ev.RoomId;
                    Debug.Log($"[Matchmaking] Joined room {ev.RoomId}");
                    break;
                case InWsEventKind.PeerLeft:
                    _opponentHandle = null;
                    Debug.Log($"[Matchmaking] Peer {ev.Handle} left");
                    break;
                case InWsEventKind.PeerJoined:
                    _opponentHandle = (int)ev.Handle;
                    Debug.Log($"[Matchmaking] Peer {ev.Handle} joined");
                    break;
                case InWsEventKind.YouAre:
                    _handle = (int)ev.Handle;
                    Debug.Log($"[Matchmaking] You are {ev.Handle}");
                    break;
                case InWsEventKind.StartedGame:

                    Debug.Log($"[Matchmaking] Started game with handle: {_handle}, opponentHandle: {_opponentHandle}, roomId: {_roomId}");
                    if (_handle == null || _opponentHandle == null || _roomId == null) return;

                    Debug.Log($"[Matchmaking] Playing with peer {ep}");
                    _curState = GameState.New();
                    SessionBuilder<Input, EndPoint> builder = new SessionBuilder<Input, EndPoint>().WithNumPlayers(2).WithFps(50);
                    builder.AddPlayer(new PlayerType<EndPoint> { Kind = PlayerKind.Local, Address = null }, new PlayerHandle(_handle.Value));
                    builder.AddPlayer(new PlayerType<EndPoint> { Kind = PlayerKind.Remote, Address = ep }, new PlayerHandle(_opponentHandle.Value));
                    _session = builder.StartP2PSession<GameState>(_synapse);
                    _playing = true;
                    break;
            }
        }
    }

    public async void CreateRoom()
    {
        Debug.Log("[Matchmaking] Creating room...");
        await _synapse.CreateRoomAsync();
    }

    public async void JoinRoom(ulong roomId)
    {
        Debug.Log($"[Matchmaking] Joining room {roomId}...");
        await _synapse.JoinRoomAsync(roomId);
    }

    public async void LeaveRoom()
    {
        Debug.Log($"[Matchmaking] Leaving room...");
        await _synapse.LeaveRoomAsync();
        _roomId = null;
        _handle = null;
        _opponentHandle = null;
    }

    public async void StartGame()
    {
        if (_handle == null || _opponentHandle == null || _roomId == null) return;
        Debug.Log($"[Matchmaking] Starting game...");
        await _synapse.StartGame();
    }

    void GameLoop()
    {
        if (_waitRemaining > 0)
        {
            Debug.Log("[Game] Skipping frame due to wait recommendation");
            _waitRemaining--;
            return;
        }
        InputFlags[] inputs = new InputFlags[2];

        InputFlags f1Input = InputFlags.None;
        if (UnityEngine.Input.GetKey(KeyCode.A))
            f1Input |= InputFlags.Left;
        if (UnityEngine.Input.GetKey(KeyCode.D))
            f1Input |= InputFlags.Right;
        if (UnityEngine.Input.GetKey(KeyCode.W))
            f1Input |= InputFlags.Up;
        inputs[0] = f1Input;

        InputFlags f2Input = InputFlags.None;
        if (UnityEngine.Input.GetKey(KeyCode.LeftArrow))
            f2Input |= InputFlags.Left;
        if (UnityEngine.Input.GetKey(KeyCode.RightArrow))
            f2Input |= InputFlags.Right;
        if (UnityEngine.Input.GetKey(KeyCode.UpArrow))
            f2Input |= InputFlags.Up;
        inputs[1] = f2Input;

        _session.PollRemoteClients();

        foreach (RollbackEvent<Input, EndPoint> ev in _session.DrainEvents())
        {
            Debug.Log($"[Game] Received {ev.Kind} event");
            switch (ev.Kind)
            {
                case RollbackEventKind.WaitRecommendation:
                    RollbackEvent<Input, EndPoint>.WaitRecommendation waitRec = ev.GetWaitRecommendation();
                    _waitRemaining = waitRec.SkipFrames;
                    break;
            }
        }

        if (_session.CurrentState == SessionState.Running)
        {
            if (_handle == 0)
                _session.AddLocalInput(new PlayerHandle(0), new Input(inputs[0]));
            if (_handle == 1)
                _session.AddLocalInput(new PlayerHandle(1), new Input(inputs[1]));

            try
            {
                List<RollbackRequest<GameState, Input>> requests = _session.AdvanceFrame();
                foreach (RollbackRequest<GameState, Input> request in requests)
                {
                    switch (request.Kind)
                    {
                        case RollbackRequestKind.SaveGameStateReq:
                            RollbackRequest<GameState, Input>.SaveGameState saveReq = request.GetSaveGameStateReq();
                            saveReq.Cell.Save(saveReq.Frame, _curState, _curState.Checksum());
                            break;
                        case RollbackRequestKind.LoadGameStateReq:
                            _curState = request.GetLoadGameStateReq().Cell.State.Value.Data;
                            break;
                        case RollbackRequestKind.AdvanceFrameReq:
                            _curState.Simulate(request.GetAdvanceFrameRequest().Inputs);
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.Log($"[Game] Exception {e}");
            }
        }

        Render();
    }

    void Render()
    {
        if (_bob1 != null)
            _bob1.transform.position = _curState.F1Info.Position;
        if (_bob2 != null)
            _bob2.transform.position = _curState.F2Info.Position;
    }
}
