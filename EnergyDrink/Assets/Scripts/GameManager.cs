using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Netcode.Rollback;
using Netcode.Rollback.Network;
using Netcode.Rollback.Sessions;
using UnityEngine;
using UnityEngine.Assertions;

public class GameManager : MonoBehaviour
{
    [Header("Testing")]
    public ulong EditorRoomId;
    public Task JoinRoomFromEditorAsync(ulong roomId)
    {
        if (_synapse == null) throw new InvalidOperationException("_synapse is null. Enter Play Mode first.");
        return _synapse.JoinRoomAsync(roomId);
    }

    [Header("Servers")]
    public string ServerIp = "144.126.152.174";
    public int HttpPort = 9000;
    public int PunchPort = 9001;
    public int RelayPort = 9002;

    [SerializeField]
    private GameObject _bob1;
    [SerializeField]
    private GameObject _bob2;
    private GameState _curState;
    private P2PSession<GameState, Input, EndPoint> _session;
    private SynapseClient _synapse;
    private volatile int _handle;
    private volatile int _opponentHandle;
    private volatile bool _playing;

    async void Awake()
    {
        _synapse = new SynapseClient(ServerIp, HttpPort, PunchPort, RelayPort);
        _playing = false;
        _handle = -1;
        _opponentHandle = -1;

        _synapse.OnYouAre += h => Debug.Log($"My handle: {h}");
        _synapse.OnPeerFound += ep => Debug.Log($"Peer found: {ep}");
        _synapse.OnPeerJoined += h => Debug.Log($"Peer joined {h}");
        _synapse.OnPeerLeft += h => Debug.Log($"Peer left {h}");
        _synapse.OnRoomCreated += room => Debug.Log($"Room: {room}");

        _synapse.OnPeerJoined += h =>
        {
            _opponentHandle = (int)h;
            _synapse.StartPunching(localUdpPort: 0);
        };
        _synapse.OnPeerLeft += h =>
        {
            _opponentHandle = -1;
            _synapse.StopUdp();
            _playing = false;
        };
        _synapse.OnYouAre += h => _handle = (int)h;
        _synapse.OnPeerFound += ep =>
        {
            _curState = GameState.New();
            SessionBuilder<Input, EndPoint> builder = new SessionBuilder<Input, EndPoint>().WithNumPlayers(2).WithFps(50);
            builder.AddPlayer(new PlayerType<EndPoint> { Kind = PlayerKind.Local, Address = null }, new PlayerHandle(_handle));
            builder.AddPlayer(new PlayerType<EndPoint> { Kind = PlayerKind.Remote, Address = ep }, new PlayerHandle(_opponentHandle));
            _session = builder.StartP2PSession<GameState>(_synapse);
            _playing = true;
        };

        await _synapse.CreateRoomAsync();
    }

    async void OnDestroy()
    {
        if (_synapse != null)
        {
            await _synapse.LeaveRoomAsync();
            _synapse.Dispose();
        }
        _handle = -1;
        _opponentHandle = -1;
        _playing = false;
        _session = null;
        _curState = GameState.New();
    }

    void FixedUpdate()
    {
        if (!_playing) { return; }
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
            Debug.Log($"Event: {ev}");
        }

        if (_session.CurrentState == SessionState.Running)
        {
            _session.AddLocalInput(new PlayerHandle(0), new Input(inputs[0]));
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
                Debug.Log($"Exception {e}");
            }
        }

        Render();
    }

    void Render()
    {
        _bob1.transform.position = _curState.F1Info.Position;
        _bob2.transform.position = _curState.F2Info.Position;
    }
}
