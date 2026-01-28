using System;
using Game.Sim;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Game
{
    public class InputBuffer
    {
        InputFlags input = InputFlags.None;

        public void Saturate()
        {
            // Movement
            if (Keyboard.current.aKey.isPressed)
                input |= InputFlags.Left;
            if (Keyboard.current.dKey.isPressed)
                input |= InputFlags.Right;
            if (Keyboard.current.wKey.isPressed)
                input |= InputFlags.Up;
            if (Keyboard.current.sKey.isPressed)
                input |= InputFlags.Down;

            // Attacks
            if (Keyboard.current.jKey.isPressed)
                input |= InputFlags.LightAttack;
            if (Keyboard.current.kKey.isPressed)
                input |= InputFlags.MediumAttack;
            if (Keyboard.current.lKey.isPressed)
                input |= InputFlags.SuperAttack;

            // Mania Keys
            if (Keyboard.current.aKey.isPressed)
                input |= InputFlags.Mania5;
            if (Keyboard.current.sKey.isPressed)
                input |= InputFlags.Mania3;
            if (Keyboard.current.dKey.isPressed)
                input |= InputFlags.Mania1;
            if (Keyboard.current.jKey.isPressed)
                input |= InputFlags.Mania2;
            if (Keyboard.current.kKey.isPressed)
                input |= InputFlags.Mania4;
            if (Keyboard.current.lKey.isPressed)
                input |= InputFlags.Mania6;
        }

        public GameInput Consume()
        {
            GameInput res = new GameInput(input);
            input = InputFlags.None;
            return res;
        }
    }
}
