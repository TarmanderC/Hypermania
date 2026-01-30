using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Design;
using Design.Animation;
using MemoryPack;
using Netcode.Rollback;
using UnityEngine;
using Utils;
using Utils.SoftFloat;

namespace Game.Sim
{
    public enum GameMode
    {
        Fighting,
        Mania,
    }

    [MemoryPackable]
    public partial class GameState : IState<GameState>
    {
        /// <summary>
        /// Physics context used to find collisions between boxes.
        /// </summary>
        [ThreadStatic]
        private static Physics<BoxProps> _physics;
        private static Physics<BoxProps> Physics
        {
            get
            {
                if (_physics == null)
                    _physics = new Physics<BoxProps>(MAX_COLLIDERS);
                return _physics;
            }
        }

        /// <summary>
        /// Cached list used to sort and process collisions, cleared at the end of every frame
        /// </summary>
        [ThreadStatic]
        private static List<Physics<BoxProps>.Collision> _collisions;
        private static List<Physics<BoxProps>.Collision> Collisions
        {
            get
            {
                if (_collisions == null)
                    _collisions = new List<Physics<BoxProps>.Collision>(MAX_COLLIDERS);
                return _collisions;
            }
        }

        [ThreadStatic]
        private static Dictionary<(int, int), Physics<BoxProps>.Collision> _hurtHitCollisions;
        private static Dictionary<(int, int), Physics<BoxProps>.Collision> HurtHitCollisions
        {
            get
            {
                if (_hurtHitCollisions == null)
                    _hurtHitCollisions = new Dictionary<(int, int), Physics<BoxProps>.Collision>(MAX_COLLIDERS);
                return _hurtHitCollisions;
            }
        }

        [MemoryPackIgnore]
        public const int MAX_COLLIDERS = 100;

        public Frame Frame;
        public FighterState[] Fighters;
        public ManiaState[] Manias;
        public GameMode GameMode;

        /// <summary>
        /// Use this static builder instead of the constructor for creating new GameStates. This is because MemoryPack,
        /// which we use to serialize the GameState, places some funky restrictions on the constructor's paratmeter
        /// list.
        /// </summary>
        /// <param name="characterConfigs">Character configs to use</param>
        /// <returns>The created GameState</returns>
        public static GameState Create(CharacterConfig[] characters)
        {
            GameState state = new GameState();
            state.Frame = Frame.FirstFrame;
            state.Fighters = new FighterState[characters.Length];
            state.Manias = new ManiaState[characters.Length];
            state.GameMode = GameMode.Fighting;
            for (int i = 0; i < characters.Length; i++)
            {
                sfloat xPos = i - ((sfloat)characters.Length - 1) / 2;
                FighterFacing facing = xPos > 0 ? FighterFacing.Left : FighterFacing.Right;
                state.Fighters[i] = FighterState.Create(new SVector2(xPos, sfloat.Zero), facing, characters[i]);
                state.Manias[i] = ManiaState.Create(
                    new ManiaConfig
                    {
                        NumKeys = 4,
                        HitHalfRange = 8,
                        MissHalfRange = 6,
                    }
                );
            }
            return state;
        }

        public void Advance(
            (GameInput input, InputStatus status)[] inputs,
            CharacterConfig[] characters,
            GlobalConfig config
        )
        {
            if (inputs.Length != characters.Length || characters.Length != Fighters.Length)
            {
                throw new InvalidOperationException("invalid inputs and characters to advance game state with");
            }
            Frame += 1;

            // Push the current input into the input history, to read for buffering.
            for (int i = 0; i < Fighters.Length; i++)
            {
                if (GameMode == GameMode.Fighting)
                {
                    Fighters[i].InputH.PushInput(inputs[i].input);
                }
                else
                {
                    Fighters[i].InputH.PushInput(GameInput.None);
                }
            }

            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].DoFrameStart();
            }

            // Tick the state machine, making the character idle if an animation/stun finishes
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].TickStateMachine(Frame);
            }

            // This function internally appies changes to the fighter's velocity based on movement inputs
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].ApplyMovementIntent(Frame, characters[i], config);
            }

            // If a player applies inputs to start a state at the start of the frame, we should apply those immediately
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].ApplyActiveState(Frame, characters[i], config);
            }

            if (GameMode == GameMode.Mania)
            {
                DoManiaStep(inputs);
            }

            DoCollisionStep(characters, config);

            // Apply any velocities set during movement or through knockback.
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].UpdatePosition(config);
            }

            // If the fighter is now on the ground, apply aerial cancels
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].ApplyAerialCancel(Frame, config);
            }

            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].FaceTowards(Fighters[i ^ 1].Position);
            }
            // Apply and change the state that derives only from passive factors (movements, etc)
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].ApplyMovementState(Frame, config);
            }
        }

        private void DoManiaStep((GameInput input, InputStatus status)[] inputs)
        {
            for (int i = 0; i < Manias.Length; i++)
            {
                List<ManiaEvent> maniaEvents = new List<ManiaEvent>();
                Manias[i].Tick(Frame, inputs[i].input, maniaEvents);
                // TODO: make note hits do something to the character here

                foreach (ManiaEvent ev in maniaEvents)
                {
                    switch (ev.Kind)
                    {
                        case ManiaEventKind.End:
                            GameMode = GameMode.Fighting;
                            break;
                    }
                }
            }
        }

        private void DoCollisionStep(CharacterConfig[] characters, GlobalConfig config)
        {
            // Each fighter then adds their hit/hurtboxes to the physics context, which will solve and find all
            // collisions. It is our job to then handle them.
            for (int i = 0; i < Fighters.Length; i++)
            {
                Fighters[i].AddBoxes(Frame, characters[i], Physics, i);
            }

            // AdvanceProjectiles();

            Physics.GetCollisions(Collisions);

            // First, solve collisions that would result in player damage. There can only be one such collision per
            // (A, B) ordered pair, where A and B are players, projectiles, or other game objects. For now, we take the
            // first collision that happens this way. In the future, which collision we take should be given by a hitbox
            // priority (a stronger hitting move should be preferred over a projectile)
            //
            // If there are no collisions of that type, then find collisions between hitboxes. This would result in a
            // clank.
            //
            // Finally, if there are no clanks or player damage collisions, make sure that the characters are not
            // colliding. If they are, push them apart.
            Physics<BoxProps>.Collision? clank = null;
            Physics<BoxProps>.Collision? collide = null;
            foreach (var c in Collisions)
            {
                (int, int) hitPair = (-1, -1);
                if (c.BoxA.Data.Kind == HitboxKind.Hitbox && c.BoxB.Data.Kind == HitboxKind.Hurtbox)
                {
                    hitPair = (c.BoxA.Owner, c.BoxB.Owner);
                }
                else if (c.BoxA.Data.Kind == HitboxKind.Hurtbox && c.BoxB.Data.Kind == HitboxKind.Hitbox)
                {
                    hitPair = (c.BoxB.Owner, c.BoxA.Owner);
                }
                else if (c.BoxA.Data.Kind == HitboxKind.Hitbox && c.BoxB.Data.Kind == HitboxKind.Hitbox)
                {
                    clank = c;
                }
                else if (c.BoxA.Data.Kind == HitboxKind.Hurtbox && c.BoxB.Data.Kind == HitboxKind.Hurtbox)
                {
                    collide = c;
                }
                // TODO: sort by priority or something
                if (hitPair != (-1, -1))
                {
                    HurtHitCollisions[hitPair] = c;
                }
            }

            if (HurtHitCollisions.Count > 0)
            {
                foreach ((var owners, var collision) in HurtHitCollisions)
                {
                    //owners[0] hits owners[1]
                    HandleCollision(collision, config);

                    var attackerBox = collision.BoxA.Owner == owners.Item1 ? collision.BoxA : collision.BoxB;
                    //to start a rhythm combo, we must sure that the move was not traded
                    if (
                        attackerBox.Data.StartsRhythmCombo
                        && !HurtHitCollisions.ContainsKey((owners.Item2, owners.Item1))
                        && GameMode == GameMode.Fighting
                    )
                    {
                        // TODO: fix me, 30.72 is hardcoded ticks/beat
                        // make the start frame always be on a multiple of 4 beats starting from 0
                        sfloat ticksPerBeat = (sfloat)30.72;
                        int barInterval = Mathsf.RoundToInt(ticksPerBeat * 4);
                        Frame baseSt = Frame + 10;
                        Frame stFrame = baseSt - baseSt.No % barInterval + barInterval;

                        for (int i = 0; i < 16; i++)
                        {
                            Manias[owners.Item1]
                                .QueueNote(
                                    i % 4,
                                    new ManiaNote
                                    {
                                        Length = 0,
                                        Tick = stFrame + Mathsf.RoundToInt(ticksPerBeat / 2 * i),
                                    }
                                );
                        }
                        Manias[owners.Item1].Enable(stFrame + Mathsf.RoundToInt(ticksPerBeat / 2 * 16));
                        GameMode = GameMode.Mania;
                    }
                }
            }
            else if (clank.HasValue)
            {
                HandleCollision(clank.Value, config);
            }
            else if (collide.HasValue)
            {
                HandleCollision(collide.Value, config);
            }

            // Clear the physics context for the next frame, which will then re-add boxes and solve for collisions again
            Physics.Clear();
            Collisions.Clear();
            HurtHitCollisions.Clear();
        }

        private void HandleCollision(Physics<BoxProps>.Collision c, GlobalConfig config)
        {
            if (c.BoxA.Data.Kind == HitboxKind.Hitbox && c.BoxB.Data.Kind == HitboxKind.Hurtbox)
            {
                Fighters[c.BoxB.Owner].ApplyHit(Frame, c.BoxA.Data);
            }
            else if (c.BoxA.Data.Kind == HitboxKind.Hurtbox && c.BoxB.Data.Kind == HitboxKind.Hitbox)
            {
                Fighters[c.BoxA.Owner].ApplyHit(Frame, c.BoxB.Data);
            }
            else if (c.BoxA.Data.Kind == HitboxKind.Hitbox && c.BoxB.Data.Kind == HitboxKind.Hitbox)
            {
                // TODO: check if moves are allowed to clank
                Fighters[c.BoxA.Owner].ApplyClank(Frame, config);
                Fighters[c.BoxB.Owner].ApplyClank(Frame, config);
            }
            else if (c.BoxA.Data.Kind == HitboxKind.Hurtbox && c.BoxB.Data.Kind == HitboxKind.Hurtbox)
            {
                // TODO: more advanced pushing/hitbox handling, e.g. if someone airborne they shouldn't be able to be
                // pushed
                if (c.BoxA.Box.Pos.x < c.BoxB.Box.Pos.x)
                {
                    Fighters[c.BoxA.Owner].Position.x -= c.OverlapX / 2;
                    Fighters[c.BoxB.Owner].Position.x += c.OverlapX / 2;
                }
                else
                {
                    Fighters[c.BoxA.Owner].Position.x += c.OverlapX / 2;
                    Fighters[c.BoxB.Owner].Position.x -= c.OverlapX / 2;
                }
            }
        }

        [ThreadStatic]
        private static ArrayBufferWriter<byte> _writer;
        private static ArrayBufferWriter<byte> Writer
        {
            get
            {
                if (_writer == null)
                    _writer = new ArrayBufferWriter<byte>(256);
                return _writer;
            }
        }

        public ulong Checksum()
        {
            Writer.Clear();
            MemoryPackSerializer.Serialize(Writer, this);
            ReadOnlySpan<byte> bytes = Writer.WrittenSpan;

            // 64-bit FNV-1a over the serialized bytes
            const ulong OFFSET = 14695981039346656037UL;
            const ulong PRIME = 1099511628211UL;

            ulong hash = OFFSET;
            for (int i = 0; i < bytes.Length; i++)
            {
                hash ^= bytes[i];
                hash *= PRIME;
            }
            return hash;
        }
    }
}
