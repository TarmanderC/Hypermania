using System;
using Design;
using Design.Animation;
using MemoryPack;
using UnityEngine;
using UnityEngine.UIElements;
using Utils;
using Utils.SoftFloat;

namespace Game.Sim
{
    public enum FighterFacing
    {
        Left,
        Right,
    }

    public enum FighterLocation
    {
        Grounded,
        Airborne,
    }

    [MemoryPackable]
    public partial struct FighterState
    {
        public SVector2 Position;
        public SVector2 Velocity;
        public sfloat Health;
        public int ComboedCount;
        public InputHistory InputH;

        public CharacterState State { get; private set; }
        public Frame StateStart { get; private set; }

        /// <summary>
        /// Set to a value that marks the first frame in which the character should return to neutral.
        /// </summary>
        public Frame StateEnd { get; private set; }
        public Frame ImmunityEnd { get; private set; }

        public FighterFacing FacingDir;

        public FighterLocation LastLocation;
        public Frame LocationSt { get; private set; }

        public bool IsAerial =>
            State == CharacterState.LightAerial
            || State == CharacterState.MediumAerial
            || State == CharacterState.SuperAerial
            || State == CharacterState.SpecialAerial;

        public static FighterState Create(SVector2 position, FighterFacing facingDirection, CharacterConfig config)
        {
            FighterState state = new FighterState();
            state.Position = position;
            state.Velocity = SVector2.zero;
            state.State = CharacterState.Idle;
            state.StateStart = Frame.FirstFrame;
            state.StateEnd = Frame.Infinity;
            state.ImmunityEnd = Frame.FirstFrame;
            state.ComboedCount = 0;
            state.InputH = new InputHistory();
            // TODO: character dependent?
            state.Health = config.Health;
            state.FacingDir = facingDirection;
            return state;
        }

        public void DoFrameStart()
        {
            if (State == CharacterState.Idle || State == CharacterState.Jump || State == CharacterState.Walk)
            {
                ComboedCount = 0;
            }
        }

        public FighterLocation Location(GlobalConfig config)
        {
            if (Position.y > config.GroundY)
            {
                return FighterLocation.Airborne;
            }
            return FighterLocation.Grounded;
        }

        public void FaceTowards(SVector2 location)
        {
            // can only switch locations if in idle/walking
            if (State != CharacterState.Idle && State != CharacterState.Walk)
            {
                return;
            }
            if (location.x < Position.x)
            {
                FacingDir = FighterFacing.Left;
            }
            else
            {
                FacingDir = FighterFacing.Right;
            }
        }

        public void TickStateMachine(Frame frame)
        {
            // if animation ends, switch back to idle
            if (frame >= StateEnd)
            {
                State = CharacterState.Idle;
                StateStart = frame;
                StateEnd = Frame.Infinity;
            }
        }

        public void ApplyMovementIntent(Frame frame, CharacterConfig characterConfig, GlobalConfig config)
        {
            if (State != CharacterState.Idle && State != CharacterState.Walk && State != CharacterState.Jump)
            {
                return;
            }
            if (Location(config) == FighterLocation.Grounded)
            {
                // this prevents jumping after dashing preserving momentum
                Velocity.x = 0;

                if (InputH.IsHeld(InputFlags.Left) && InputH.PressedAndReleasedRecently(InputFlags.Left, 12, 1))
                {
                    Velocity.x += 2 * -characterConfig.Speed;
                    State = FacingDir == FighterFacing.Left ? CharacterState.ForwardDash : CharacterState.BackDash;
                    StateEnd = frame + 12;
                    StateStart = frame;
                    return;
                }

                if (InputH.IsHeld(InputFlags.Right) && InputH.PressedAndReleasedRecently(InputFlags.Right, 12, 1))
                {
                    Velocity.x += 2 * characterConfig.Speed;
                    State = FacingDir == FighterFacing.Right ? CharacterState.ForwardDash : CharacterState.BackDash;
                    StateEnd = frame + 12;
                    StateStart = frame;
                    return;
                }

                if (InputH.IsHeld(InputFlags.Left))
                {
                    Velocity.x += -characterConfig.Speed;
                }
                if (InputH.IsHeld(InputFlags.Right))
                {
                    Velocity.x += characterConfig.Speed;
                }

                if (InputH.IsHeld(InputFlags.Up))
                {
                    if (InputH.PressedRecently(InputFlags.Down, 8))
                    {
                        Velocity.y = (sfloat)1.25 * characterConfig.JumpVelocity;
                    }
                    else
                    {
                        Velocity.y = characterConfig.JumpVelocity;
                    }
                }
            }
        }

        public void ApplyActiveState(Frame frame, CharacterConfig characterConfig, GlobalConfig config)
        {
            if (State != CharacterState.Idle && State != CharacterState.Walk && State != CharacterState.Jump)
            {
                return;
            }
            if (InputH.PressedRecently(InputFlags.LightAttack, 8))
            {
                switch (Location(config))
                {
                    case FighterLocation.Grounded:
                        {
                            Velocity = SVector2.zero;
                            State = CharacterState.LightAttack;
                            StateStart = frame;
                            StateEnd = StateStart + characterConfig.GetHitboxData(State).TotalTicks;
                        }
                        break;
                    case FighterLocation.Airborne:
                        {
                            State = CharacterState.LightAerial;
                            StateStart = frame;
                            StateEnd = StateStart + characterConfig.GetHitboxData(State).TotalTicks;
                        }
                        break;
                }
            }
            else if (InputH.PressedRecently(InputFlags.SuperAttack, 8))
            {
                switch (Location(config))
                {
                    case FighterLocation.Grounded:
                        {
                            Velocity = SVector2.zero;
                            State = CharacterState.SuperAttack;
                            StateStart = frame;
                            StateEnd = StateStart + characterConfig.GetHitboxData(State).TotalTicks;
                        }
                        break;
                }
            }
        }

        public void UpdatePosition(GlobalConfig config)
        {
            // Apply gravity if not grounded
            if (Position.y > config.GroundY || Velocity.y > 0)
            {
                Velocity.y += config.Gravity * 1 / 64;
            }

            // Update Position
            Position += Velocity * 1 / 64;

            // Floor collision
            if (Position.y <= config.GroundY)
            {
                Position.y = config.GroundY;

                if (Velocity.y < 0)
                    Velocity.y = 0;
            }
            if (Position.x >= config.WallsX)
            {
                Position.x = config.WallsX;
                if (Velocity.x > 0)
                    Velocity.x = 0;
            }
            if (Position.x <= -config.WallsX)
            {
                Position.x = -config.WallsX;
                if (Velocity.x < 0)
                    Velocity.x = 0;
            }
        }

        public void ApplyAerialCancel(Frame frame, GlobalConfig config)
        {
            if (!IsAerial)
            {
                return;
            }
            // TODO: apply some landing lag here
            if (Location(config) == FighterLocation.Grounded)
            {
                State = CharacterState.Idle;
                StateStart = frame;
                StateEnd = Frame.Infinity;
            }
        }

        public void AddBoxes(Frame frame, CharacterConfig config, Physics<BoxProps> physics, int handle)
        {
            int tick = frame - StateStart;
            FrameData frameData = config.GetFrameData(State, tick);

            foreach (var box in frameData.Boxes)
            {
                SVector2 centerLocal = box.CenterLocal;
                if (FacingDir == FighterFacing.Left)
                {
                    centerLocal.x *= -1;
                }
                SVector2 sizeLocal = box.SizeLocal;
                SVector2 centerWorld = Position + centerLocal;
                BoxProps newProps = box.Props;
                if (FacingDir == FighterFacing.Left)
                {
                    newProps.Knockback.x *= -1;
                }
                physics.AddBox(handle, centerWorld, sizeLocal, newProps);
            }
        }

        public void ApplyHit(Frame frame, BoxProps props)
        {
            if (ImmunityEnd > frame)
            {
                return;
            }
            State = CharacterState.Hit;
            StateStart = frame;
            // Apply Hit/collision stuff is done after the player is actionable, so if the player needs to be
            // inactionable for "one more frame"
            StateEnd = frame + props.HitstunTicks + 1;
            // TODO: fixme, just to prevent multi hit
            ImmunityEnd = frame + 7;
            // TODO: if high enough, go knockdown
            Health -= props.Damage;

            Velocity = props.Knockback;

            ComboedCount++;
        }

        public void ApplyClank(Frame frame, GlobalConfig config)
        {
            State = CharacterState.Hit;
            StateStart = frame;
            // Apply Hit/collision stuff is done after the player is actionable, so if the player needs to be
            // inactionable for "one more frame"
            StateEnd = frame + config.ClankTicks + 1;
            Velocity = SVector2.zero;
        }

        public void ApplyMovementState(Frame frame, GlobalConfig config)
        {
            if (
                (State == CharacterState.Idle || State == CharacterState.Walk)
                && Location(config) == FighterLocation.Airborne
            )
            {
                State = CharacterState.Jump;
                StateStart = frame;
                StateEnd = Frame.Infinity;
            }
            else if (
                (State == CharacterState.Idle || State == CharacterState.Jump)
                && Velocity.magnitude > (sfloat)0.01f
                && Location(config) == FighterLocation.Grounded
            )
            {
                State = CharacterState.Walk;
                StateStart = frame;
                StateEnd = Frame.Infinity;
            }
            else if (
                (State == CharacterState.Walk || State == CharacterState.Jump)
                && Velocity.magnitude < (sfloat)0.01f
                && Location(config) == FighterLocation.Grounded
            )
            {
                State = CharacterState.Idle;
                StateStart = frame;
                StateEnd = Frame.Infinity;
            }
        }
    }
}
