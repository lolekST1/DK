using System.Collections.Generic;
using UnityEngine;

namespace DK
{
    public enum CreatureState
    {
        Idle,
        GoingToLair,
        Sleeping,
        Wandering,
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
    public class CreatureAI : MonoBehaviour
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

        public float TurnSpeed = 12f;

        GridManager _grid;
        GridWalker _walker;
        RoomManager _rooms;
        Renderer _bodyRenderer;
        Transform _body;
        MaterialPropertyBlock _propertyBlock;

        readonly List<Vector2Int> _scratch = new List<Vector2Int>();

        CreatureCatalog.Entry _stats;
        Vector2Int _fallbackHome;
        float _decisionCooldown;
        float _bobPhase;
        float _bodyBaseY;
        int _wanderSeed;

        static readonly Color CalmTint = new Color(1f, 1f, 1f);
        static readonly Color FuriousTint = new Color(1.6f, 0.55f, 0.45f);

        public void Configure(GridManager grid, RoomManager rooms, CreatureKind kind,
                              Transform body, Renderer bodyRenderer, Vector2Int spawnCell)
        {
            _grid = grid;
            _rooms = rooms;
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

            _wanderSeed = GetHashCode();
            transform.position = grid.CellToWorld(_fallbackHome);
            ApplyMoodTint();
        }

        void OnDestroy()
        {
            if (_rooms != null) _rooms.UnregisterWorker(this);
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
                case CreatureState.Leaving:
                    TickLeaving(dt);
                    break;
            }

            AnimateBody(dt);
        }

        void UpdateNeeds(float dt)
        {
            if (State == CreatureState.Leaving) return;

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
            // A lair sold out from under it wakes it up, which is the player's problem.
            if (!HasLair || Fatigue <= 0f)
            {
                State = CreatureState.Idle;
                _decisionCooldown = 0f;
            }
        }

        void TickWandering(float dt)
        {
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
