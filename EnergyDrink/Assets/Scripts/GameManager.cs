using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using Netcode.Rollback;
using Netcode.Rollback.Sessions;
using Steamworks;
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
    private P2PSession<GameState, Input, CSteamID> _session;

    private bool _playing;
    private uint _waitRemaining;
    private SteamMatchmakingClient _client;

    void Awake()
    {
        _playing = false;
    }

    void OnDestroy()
    {
        _playing = false;
    }

    void Start()
    {
        _client = new SteamMatchmakingClient();
    }

    public void CreateLobby() => StartCoroutine(CreateLobbyRoutine());
    IEnumerator CreateLobbyRoutine()
    {
        var task = _client.Create();
        while (!task.IsCompleted)
            yield return null;
        if (task.IsFaulted)
        {
            Debug.LogException(task.Exception);
            yield break;
        }
        Debug.Log($"Created lobby {task.Result}");
    }

    public void JoinLobby(CSteamID lobbyId) => StartCoroutine(JoinLobbyRoutine(lobbyId));
    IEnumerator JoinLobbyRoutine(CSteamID lobbyId)
    {
        var task = _client.Join(lobbyId);
        while (!task.IsCompleted)
            yield return null;
        if (task.IsFaulted)
        {
            Debug.LogException(task.Exception);
            yield break;
        }
        Debug.Log($"Joined lobby {lobbyId}");
    }

    public void LeaveLobby() => StartCoroutine(LeaveLobbyRoutine());
    IEnumerator LeaveLobbyRoutine()
    {
        var task = _client.Leave();
        while (!task.IsCompleted)
            yield return null;
        if (task.IsFaulted)
        {
            Debug.LogException(task.Exception);
            yield break;
        }
        Debug.Log($"Left lobby");
    }

    public void StartGame() => StartCoroutine(StartGameRoutine());
    IEnumerator StartGameRoutine()
    {
        Debug.Log($"{_client.Peer}, {_client.Me}, {_client.CurrentLobby}");
        if (!_client.HasPeer) { yield break; }

        var task = _client.StartGame();
        while (!task.IsCompleted)
            yield return null;
        if (task.IsFaulted)
        {
            Debug.LogException(task.Exception);
            yield break;
        }
        var handles = task.Result;

        _curState = GameState.New();
        SessionBuilder<Input, CSteamID> builder = new SessionBuilder<Input, CSteamID>().WithNumPlayers(2).WithFps(64);
        foreach ((CSteamID id, int handle) in handles)
        {
            Debug.Log($"Adding player with id {id} and handle {handle}");
            builder.AddPlayer(new PlayerType<CSteamID> { Kind = _client.Me == id ? PlayerKind.Local : PlayerKind.Remote, Address = id }, new PlayerHandle(handle));
        }
        _session = builder.StartP2PSession<GameState>(_client);
        _playing = true;
    }

    void FixedUpdate()
    {
        if (_client == null)
        {
            return;
        }
        if (!_playing)
        {
            return;
        }
        GameLoop();
    }

    void GameLoop()
    {
        if (_waitRemaining > 0)
        {
            Debug.Log("[Game] Skipping frame due to wait recommendation");
            _waitRemaining--;
            return;
        }
        InputFlags f1Input = InputFlags.None;
        if (UnityEngine.Input.GetKey(KeyCode.A))
            f1Input |= InputFlags.Left;
        if (UnityEngine.Input.GetKey(KeyCode.D))
            f1Input |= InputFlags.Right;
        if (UnityEngine.Input.GetKey(KeyCode.W))
            f1Input |= InputFlags.Up;

        _session.PollRemoteClients();

        foreach (RollbackEvent<Input, CSteamID> ev in _session.DrainEvents())
        {
            Debug.Log($"[Game] Received {ev.Kind} event");
            switch (ev.Kind)
            {
                case RollbackEventKind.WaitRecommendation:
                    RollbackEvent<Input, CSteamID>.WaitRecommendation waitRec = ev.GetWaitRecommendation();
                    _waitRemaining = waitRec.SkipFrames;
                    break;
            }
        }

        if (_session.CurrentState == SessionState.Running)
        {
            _session.AddLocalInput(new PlayerHandle(_client.MyHandle), new Input(f1Input));
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
