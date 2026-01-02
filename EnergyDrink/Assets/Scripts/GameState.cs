using System.IO.Hashing;
using Netcode.Rollback;
using UnityEngine;

public struct GameState
{
    public FighterInfo F1Info;
    public FighterInfo F2Info;

    public static GameState New()
    {
        return new GameState
        {
            F1Info = new FighterInfo(new Vector2(-9, -4.5f), Vector2.zero, 7f, FighterState.Idle, 0, Vector2.right),
            F2Info = new FighterInfo(new Vector2(9, -4.5f), Vector2.zero, 7f, FighterState.Idle, 0, Vector2.left)
        };
    }

    public void Simulate((Input input, InputStatus status)[] inputs)
    {
        F1Info.HandleInput(inputs[0].input);
        F2Info.HandleInput(inputs[1].input);
    }

    public ulong Checksum()
    {
        // TODO: fixme
        return 0;
    }
}
