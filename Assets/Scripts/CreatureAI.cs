using UnityEngine;

namespace DK
{
    public enum CreatureState
    {
        Idle,
        GoingToLair,
        Sleeping,
        Wandering,
        Fighting,
        Leaving,
    }

    /// <summary>
    /// A creature that walked in through the portal. It does no work — it has needs, and the
    /// dungeon either meets them or watches it leave.
    ///
    /// Two needs, both slow: it wants a lair to sleep in, and it wants paying on payday.
    /// Failing either raises its anger, and a creature that runs out of patience walks back
    /// to the portal and is gone. Digging is what pays for both, which is the whole point —
    /// the creature layer is what gives the gold somewhere to go.
    /// </summary>
    public class CreatureAI : MonoBehaviour, ICombatant
    {
        public CreatureKind Kind { get; private set; } = CreatureKind.Beetle;

        public CreatureState State { get; private set; } = CreatureState.Idle;

        /// <summary>0 = fresh, 1 = has to sleep.</summary>
        public float Fatigue { get; private set; }

        /// <summary>0 = content, 1 = walking out of the portal.</summary>
        public float Anger { get; private set; }

        public int MissedPaydays { get; private set; }

        /// <summary>Set once it has reached the portal and should be removed from the roster.</summary>
        public bool HasLeft { get; private set; }

        public bool HasLair => _rooms != null && _rooms.HasLair(this);

        public int Health { get; private set; }

        public int MaxHealth => _stats.Health;

        public bool IsAlive => Health > 0;

        Side ICombatant.Side => Side.Dungeon;

        Vector2Int ICombatant.Cell => CurrentCell;

        Vector3 ICombatant.Position => transform.position;

        bool ICombatant.IsStructure => false;

        public float TurnSpeed = 12f;

        GridManager _grid;
        GridWalker _walker;
        Battlefield _battlefield;
        ICombatant _enemy;
        RoomManager _rooms;
        Renderer _bodyRenderer;
        Transform _body;
        MaterialPropertyBlock _propertyBlock;

        CreatureCatalog.Entry _stats;
        Vector2Int _fallbackHome;
        float _decisionCooldown;
        float _bobPhase;
        float _bodyBaseY;
        float _swingTimer;
        float _chaseRepath;
        int _wanderSeed;

        static readonly Color CalmTint = new Color(1f, 1f, 1f);
        static readonly Color FuriousTint = new Color(1.6f, 0.55f, 0.45f);

        public void Configure(GridManager grid, RoomManager rooms, Battlefield battlefield,
                              CreatureKind kind, Transform body, Renderer bodyRenderer,
                              Vector2Int spawnCell)
        {
            _grid = grid;
            _rooms = rooms;
            _battlefield = battlefield;
            Kind = kind;
            _stats = CreatureCatalog.Get(kind);
            _body = body;
            _bodyRenderer = bodyRenderer;
            _propertyBlock = new MaterialPropertyBlock();

            if (_body != null) _bodyBaseY = _body.localPosition.y;

            _fallbackHome = grid.IsWalkable(spawnCell) ? spawnCell : grid.BaseCell;
            if (_rooms != null) _rooms.RegisterWorker(this, _fallbackHome);

            _walker = new GridWalker(grid, transform)
            {
                MoveSpeed = _stats.MoveSpeed,
                TurnSpeed = TurnSpeed,
            };

            Health = _stats.Health;
            if (_battlefield != null) _battlefield.Register(this);

            _wanderSeed = GetHashCode();
            transform.position = grid.CellToWorld(_fallbackHome);
            ApplyMoodTint();
        }

        void OnDestroy()
        {
            if (_rooms != null) _rooms.UnregisterWorker(this);
            if (_battlefield != null) _battlefield.Unregister(this);
        }

        /// <summary>Takes a hit. A creature that dies is cleaned up by the manager.</summary>
        public void TakeDamage(int amount, ICombatant from)
        {
            if (amount <= 0 || !IsAlive) return;

            Health = Mathf.Max(0, Health - amount);

            // Being hit is reason enough to stop sleeping and turn round.
            if (IsAlive && State != CreatureState.Leaving && from != null && from.IsAlive)
                Engage(from);
        }

        public Vector2Int CurrentCell => _grid.WorldToCell(transform.position);

        /// <summary>Where it sleeps: its lair if it has claimed one, otherwise the portal.</summary>
        public Vector2Int HomeCell => _rooms != null ? _rooms.HomeFor(this) : _fallbackHome;

        // ---------------------------------------------------------------- payroll

        /// <summary>Payday came and the vault covered it.</summary>
        public void OnPaid()
        {
            MissedPaydays = 0;
        }

        /// <summary>Payday came and the vault did not cover it.</summary>
        public void OnUnpaid()
        {
            MissedPaydays++;
            Anger = Mathf.Clamp01(Anger + _stats.AngerPerMissedWage);
        }

        public int Wage => _stats.Wage;

        // ---------------------------------------------------------------- loop

        void Update()
        {
            if (_grid == null) return;

            float dt = Time.deltaTime;

            UpdateNeeds(dt);

            switch (State)
            {
                case CreatureState.Idle:
                    TickIdle();
                    break;
                case CreatureState.GoingToLair:
                    TickGoingToLair(dt);
                    break;
                case CreatureState.Sleeping:
                    TickSleeping(dt);
                    break;
                case CreatureState.Wandering:
                    TickWandering(dt);
                    break;
                case CreatureState.Fighting:
                    TickFighting(dt);
                    break;
                case CreatureState.Leaving:
                    TickLeaving(dt);
                    break;
            }

            AnimateBody(dt);
        }

        void UpdateNeeds(float dt)
        {
            if (State == CreatureState.Leaving) return;

            // Wages and sleep can wait while there is a hero in the dungeon.
            if (State == CreatureState.Fighting) return;

            if (State == CreatureState.Sleeping)
                Fatigue = Mathf.Clamp01(Fatigue - _stats.RestPerSecond * dt);
            else
                Fatigue = Mathf.Clamp01(Fatigue + _stats.FatiguePerSecond * dt);

            bool housed = HasLair;
            bool paid = MissedPaydays == 0;

            if (!housed) Anger = Mathf.Clamp01(Anger + _stats.HomelessAngerPerSecond * dt);
            else if (paid) Anger = Mathf.Clamp01(Anger - _stats.CalmPerSecond * dt);

            ApplyMoodTint();

            if (Anger >= 1f) BeginLeaving();
        }

        void BeginLeaving()
        {
            if (State == CreatureState.Leaving) return;

            State = CreatureState.Leaving;
            _walker.Stop();

            if (_rooms != null && _rooms.HasPortal) _walker.SetPath(_rooms.PortalCell);
        }

        void TickIdle()
        {
            // Ahead of the cooldown on purpose. Idling is paced so creatures do not re-plan
            // every frame, but a hero in the dungeon is not something to get round to in a
            // second and a bit — that pause read as creatures standing about during a raid.
            if (TryFindFight()) return;

            _decisionCooldown -= Time.deltaTime;
            if (_decisionCooldown > 0f) return;
            _decisionCooldown = 0.4f;

            // Reading home is also what moves a creature into a lair the player just built.
            var home = HomeCell;

            if (Fatigue >= 1f && HasLair)
            {
                if (CurrentCell == home)
                {
                    State = CreatureState.Sleeping;
                    return;
                }

                if (_walker.SetPath(home))
                {
                    State = CreatureState.GoingToLair;
                    return;
                }
            }

            if (TryPickWanderTarget()) State = CreatureState.Wandering;
        }

        void TickGoingToLair(float dt)
        {
            if (TryFindFight()) return;

            if (!HasLair)
            {
                State = CreatureState.Idle;
                return;
            }

            if (_walker.Advance(dt)) return;

            State = CurrentCell == HomeCell ? CreatureState.Sleeping : CreatureState.Idle;
        }

        void TickSleeping(float dt)
        {
            // Worth waking up for.
            if (TryFindFight()) return;

            // A lair sold out from under it wakes it up, which is the player's problem.
            if (!HasLair || Fatigue <= 0f)
            {
                State = CreatureState.Idle;
                _decisionCooldown = 0f;
            }
        }

        void TickWandering(float dt)
        {
            if (TryFindFight()) return;

            if (Fatigue >= 1f && HasLair)
            {
                State = CreatureState.Idle;
                _decisionCooldown = 0f;
                return;
            }

            if (_walker.Advance(dt)) return;

            State = CreatureState.Idle;
            _decisionCooldown = 1.2f;
        }

        void TickLeaving(float dt)
        {
            if (_walker.Advance(dt)) return;

            // Reached the portal, or could not path to it at all. Either way it is done here:
            // a creature stuck forever in a walled-off dungeon would just accumulate.
            HasLeft = true;
        }

        void TickFighting(float dt)
        {
            if (_enemy == null || !_enemy.IsAlive)
            {
                _enemy = null;
                _walker.Stop();
                State = CreatureState.Idle;
                _decisionCooldown = 0f;
                return;
            }

            if (Battlefield.InReach(this, _enemy))
            {
                _walker.Stop();
                _walker.FaceTowards(_enemy.Position, dt);

                _swingTimer -= dt;
                if (_swingTimer > 0f) return;

                _swingTimer = _stats.AttackInterval;
                _enemy.TakeDamage(_stats.Damage, this);
                return;
            }

            if (_walker.Advance(dt))
            {
                // The hero is walking too, so a route laid once goes stale. Re-lay it a couple
                // of times a second rather than only on arrival, or the chase trails behind.
                _chaseRepath -= dt;
                if (_chaseRepath <= 0f)
                {
                    _chaseRepath = 0.4f;
                    _walker.SetPath(_enemy.Cell);
                }

                return;
            }

            // It moved. Chase it, and give up if it got somewhere we cannot follow.
            if (!_walker.SetPath(_enemy.Cell))
            {
                _enemy = null;
                State = CreatureState.Idle;
                _decisionCooldown = 0f;
            }
        }

        /// <summary>
        /// Looks for a hero worth walking to, and commits to it if there is one. Costs a list
        /// scan and nothing else while no raid is in progress, which is nearly all the time,
        /// so it is safe to call every frame from every state that is not already fighting.
        /// </summary>
        bool TryFindFight()
        {
            if (_battlefield == null || State == CreatureState.Leaving) return false;
            if (_battlefield.CountOf(Side.Hero) == 0) return false;
            if (!_battlefield.TryFindNearestEnemy(this, _stats.AlertRange, out var enemy)) return false;

            return Engage(enemy);
        }

        bool Engage(ICombatant enemy)
        {
            if (enemy == null || !enemy.IsAlive) return false;

            // Already next to it: no route needed, just start swinging.
            if (!Battlefield.InReach(this, enemy) && !_walker.SetPath(enemy.Cell)) return false;

            // Only a new fight restarts the swing clock. Engage is also called from
            // TakeDamage, and resetting there let each side's hit hand the other a free swing:
            // the two fed each other every frame, so a duel meant to take seconds was decided
            // in about a tenth of one and looked like creatures vanishing on contact.
            if (State != CreatureState.Fighting || !ReferenceEquals(_enemy, enemy))
            {
                _swingTimer = 0f;
                _chaseRepath = 0.4f;
            }

            _enemy = enemy;
            State = CreatureState.Fighting;
            return true;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// Picks a nearby dug tile to amble to. Purely cosmetic, but a dungeon of statues
        /// reads as broken, and this is far cheaper than idle animation.
        /// </summary>
        bool TryPickWanderTarget()
        {
            var from = CurrentCell;

            for (int attempt = 0; attempt < 8; attempt++)
            {
                _wanderSeed = _wanderSeed * 1103515245 + 12345;
                int offsetX = (_wanderSeed >> 16) % 9 - 4;
                _wanderSeed = _wanderSeed * 1103515245 + 12345;
                int offsetZ = (_wanderSeed >> 16) % 9 - 4;

                var candidate = new Vector2Int(from.x + offsetX, from.y + offsetZ);
                if (candidate == from) continue;
                if (!_grid.IsWalkable(candidate)) continue;
                if (!_walker.SetPath(candidate)) continue;

                return true;
            }

            return false;
        }

        void AnimateBody(float dt)
        {
            if (_body == null) return;

            if (State == CreatureState.Sleeping)
            {
                // Settle to the floor and breathe, so a sleeping creature is obvious from above.
                _bobPhase += dt * 1.4f;
                float breath = Mathf.Abs(Mathf.Sin(_bobPhase)) * 0.03f;
                _body.localPosition = new Vector3(_body.localPosition.x, _bodyBaseY - 0.10f + breath, _body.localPosition.z);
                return;
            }

            bool moving = State == CreatureState.Wandering || State == CreatureState.GoingToLair ||
                          State == CreatureState.Leaving;

            if (moving) _bobPhase += dt * 7f;
            else _bobPhase = Mathf.MoveTowards(_bobPhase, 0f, dt * 4f);

            float offset = Mathf.Abs(Mathf.Sin(_bobPhase)) * 0.05f;
            _body.localPosition = new Vector3(_body.localPosition.x, _bodyBaseY + offset, _body.localPosition.z);
        }

        /// <summary>Reddens the creature as it loses patience, so a problem is visible on the map.</summary>
        void ApplyMoodTint()
        {
            if (_bodyRenderer == null) return;

            var tint = Color.Lerp(CalmTint, FuriousTint, Anger);
            var skin = new Color(_stats.Skin.r * tint.r, _stats.Skin.g * tint.g, _stats.Skin.b * tint.b);

            _bodyRenderer.GetPropertyBlock(_propertyBlock);
            MaterialLibrary.SetColor(_propertyBlock, skin);
            _bodyRenderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
