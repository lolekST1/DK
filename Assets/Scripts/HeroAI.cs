using UnityEngine;

namespace DK
{
    public enum HeroState
    {
        Advancing,
        Fighting,
        Escaping,
    }

    /// <summary>
    /// A hero raiding the dungeon. It walks to the nearest vault tile with gold in it, helps
    /// itself, and carries the loot back out through the gate it came in by. Anything on the
    /// dungeon's side that gets in the way is fought on the spot.
    ///
    /// It steals rather than destroys, so a raid is a loss the player can see in the gold
    /// counter and answer by housing more creatures — no game-over state, and no separate
    /// screen to explain what just happened. Kill it before it reaches the gate and the loot
    /// falls on the floor for the imps to carry back.
    /// </summary>
    public class HeroAI : MonoBehaviour, ICombatant
    {
        public HeroState State { get; private set; } = HeroState.Advancing;

        public int Health { get; private set; }

        public int MaxHealth => _stats.Health;

        /// <summary>
        /// Alive *and* still in the dungeon. A hero that made it back through the gate stops
        /// being a target the same frame: Unity does not destroy it until the end of the
        /// frame, and defenders were otherwise still swinging at something that had left —
        /// which counted the raid as both robbed and repelled.
        /// </summary>
        public bool IsAlive => Health > 0 && !HasEscaped;

        /// <summary>Gold lifted from the vault and not yet carried out.</summary>
        public int CarriedGold { get; private set; }

        /// <summary>Set once it is back at the gate with the loot. The manager cleans it up.</summary>
        public bool HasEscaped { get; private set; }

        public HeroKind Kind { get; private set; } = HeroKind.Knight;

        Side ICombatant.Side => Side.Hero;

        Vector2Int ICombatant.Cell => CurrentCell;

        Vector3 ICombatant.Position => transform.position;

        bool ICombatant.IsStructure => false;

        GridManager _grid;
        RoomManager _rooms;
        ResourceManager _economy;
        LooseGold _loose;
        Battlefield _battlefield;
        DungeonHeart _heart;
        GridWalker _walker;

        Renderer _bodyRenderer;
        Transform _body;
        MaterialPropertyBlock _propertyBlock;

        HeroCatalog.Entry _stats;
        ICombatant _enemy;
        Vector2Int _gateCell;
        Vector2Int _goal;
        bool _hasGoal;
        float _swingTimer;
        float _decisionCooldown;
        float _bobPhase;
        float _bodyBaseY;

        static readonly Color HurtTint = new Color(1.35f, 0.55f, 0.5f);

        public void Configure(GridManager grid, RoomManager rooms, ResourceManager economy,
                              LooseGold loose, Battlefield battlefield, DungeonHeart heart,
                              HeroKind kind, Transform body, Renderer bodyRenderer,
                              Vector2Int gateCell)
        {
            _grid = grid;
            _rooms = rooms;
            _economy = economy;
            _loose = loose;
            _battlefield = battlefield;
            _heart = heart;

            Kind = kind;
            _stats = HeroCatalog.Get(kind);
            Health = _stats.Health;

            _body = body;
            _bodyRenderer = bodyRenderer;
            _propertyBlock = new MaterialPropertyBlock();
            if (_body != null) _bodyBaseY = _body.localPosition.y;

            _gateCell = gateCell;
            transform.position = grid.CellToWorld(gateCell);

            _walker = new GridWalker(grid, transform)
            {
                MoveSpeed = _stats.MoveSpeed,
            };

            if (_battlefield != null) _battlefield.Register(this);
            ApplyTint();
        }

        void OnDestroy()
        {
            if (_battlefield != null) _battlefield.Unregister(this);
        }

        public Vector2Int CurrentCell => _grid.WorldToCell(transform.position);

        public void TakeDamage(int amount, ICombatant from)
        {
            if (amount <= 0 || !IsAlive) return;

            Health = Mathf.Max(0, Health - amount);
            ApplyTint();

            // Turn on whatever hit it, unless it is already carrying loot — then the gate
            // matters more than the fight, and it will only stop for whatever blocks the way.
            if (IsAlive && CarriedGold <= 0 && from != null && from.IsAlive) Engage(from);
        }

        /// <summary>
        /// Everything it is carrying falls where it died, for the imps to carry back. Called by
        /// the manager, which owns the roster and the cleanup.
        /// </summary>
        public int DropLoot()
        {
            int carried = CarriedGold;
            CarriedGold = 0;

            if (carried > 0 && _loose != null) _loose.Drop(CurrentCell, carried);
            return carried;
        }

        // ---------------------------------------------------------------- loop

        void Update()
        {
            if (_grid == null || !IsAlive) return;

            float dt = Time.deltaTime;

            switch (State)
            {
                case HeroState.Advancing:
                    TickAdvancing(dt);
                    break;
                case HeroState.Fighting:
                    TickFighting(dt);
                    break;
                case HeroState.Escaping:
                    TickEscaping(dt);
                    break;
            }

            AnimateBody(dt);
        }

        void TickAdvancing(float dt)
        {
            if (TryFindFight()) return;

            if (_hasGoal && _walker.Advance(dt)) return;

            if (_hasGoal)
            {
                _hasGoal = false;

                // Arrived. Either at the heart it is besieging, or at the vault tile it came
                // for — and a tile another hero may have already emptied.
                if (_stats.GoesForTheHeart)
                {
                    if (TrySiegeHeart()) return;
                }
                else if (Rob(_goal) > 0)
                {
                    BeginEscape();
                    return;
                }
            }

            _decisionCooldown -= dt;
            if (_decisionCooldown > 0f) return;
            _decisionCooldown = 0.5f;

            if (_stats.GoesForTheHeart)
            {
                // No loot, no leaving: it came for the heart and stays until one of them is
                // finished. Standing still is right if it cannot reach it — the defenders will
                // come to it, which is the fight either way.
                TrySiegeHeart();
                return;
            }

            if (TrySelectVault()) return;

            // Nothing left worth stealing. Go home rather than stand in the corridor.
            BeginEscape();
        }

        void TickFighting(float dt)
        {
            if (_enemy == null || !_enemy.IsAlive)
            {
                _enemy = null;
                _walker.Stop();
                _hasGoal = false;
                _decisionCooldown = 0f;
                State = CarriedGold > 0 && !_stats.GoesForTheHeart
                    ? HeroState.Escaping
                    : HeroState.Advancing;

                if (State == HeroState.Escaping) BeginEscape();
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

            if (_walker.Advance(dt)) return;

            if (!_walker.SetPath(_enemy.Cell))
            {
                _enemy = null;
                _decisionCooldown = 0f;
                State = HeroState.Advancing;
            }
        }

        void TickEscaping(float dt)
        {
            // Only stops for something standing between it and the door.
            if (_enemy == null && _battlefield != null &&
                _battlefield.TryFindNearestEnemy(this, 1, out var blocker))
            {
                Engage(blocker);
                return;
            }

            if (_walker.Advance(dt)) return;

            if (CurrentCell == _gateCell)
            {
                HasEscaped = true;
                if (_battlefield != null) _battlefield.Unregister(this);
                return;
            }

            // Could not path home — the dungeon was dug out from under it. Stand and fight.
            if (!_walker.SetPath(_gateCell) && TryFindFight()) return;
        }

        // ---------------------------------------------------------------- helpers

        void BeginEscape()
        {
            State = HeroState.Escaping;
            _enemy = null;
            _hasGoal = false;
            _walker.SetPath(_gateCell);
        }

        /// <summary>
        /// Walks at the heart, and starts swinging once it is next to it. The heart is a
        /// structure, so it never turns up in an ordinary enemy search — it has to be asked
        /// for by name, and only what came for it does that.
        /// </summary>
        bool TrySiegeHeart()
        {
            if (_heart == null || !_heart.IsAlive) return false;

            if (Battlefield.InReach(this, _heart)) return Engage(_heart);
            if (!_walker.SetPath(_heart.Cell)) return false;

            _goal = _heart.Cell;
            _hasGoal = true;
            return true;
        }

        /// <summary>Nearest vault tile that actually has gold on it and can be walked to.</summary>
        bool TrySelectVault()
        {
            if (_rooms == null) return false;

            var from = CurrentCell;
            var best = new Vector2Int(-1, -1);
            int bestDistance = int.MaxValue;

            for (int x = 0; x < _grid.Width; x++)
            for (int z = 0; z < _grid.Depth; z++)
            {
                if (_rooms.GetStoredGold(x, z) <= 0) continue;

                int distance = Mathf.Abs(x - from.x) + Mathf.Abs(z - from.y);
                if (distance >= bestDistance) continue;

                bestDistance = distance;
                best = new Vector2Int(x, z);
            }

            if (bestDistance == int.MaxValue) return false;
            if (!_walker.SetPath(best)) return false;

            _goal = best;
            _hasGoal = true;
            return true;
        }

        /// <summary>Takes what it can carry off one vault tile.</summary>
        int Rob(Vector2Int cell)
        {
            int available = _rooms != null ? _rooms.GetStoredGold(cell.x, cell.y) : 0;
            int wanted = Mathf.Min(available, _stats.LootCapacity - CarriedGold);
            if (wanted <= 0) return 0;

            // Withdraw drains the treasuries before the heart, which is not where this hero is
            // standing — but the gold is fungible and the tile it robbed is the one the player
            // watched it walk to, so the visible story and the books still agree.
            if (_economy == null || !_economy.TrySpend(wanted)) return 0;

            CarriedGold += wanted;
            return wanted;
        }

        bool TryFindFight()
        {
            if (_battlefield == null) return false;
            if (!_battlefield.TryFindNearestEnemy(this, _stats.AlertRange, out var enemy)) return false;

            return Engage(enemy);
        }

        bool Engage(ICombatant enemy)
        {
            if (enemy == null || !enemy.IsAlive) return false;
            if (!Battlefield.InReach(this, enemy) && !_walker.SetPath(enemy.Cell)) return false;

            // See CreatureAI.Engage: only a new fight restarts the swing clock, or being hit
            // buys a free attack and both sides swing every frame.
            if (State != HeroState.Fighting || !ReferenceEquals(_enemy, enemy)) _swingTimer = 0f;

            _enemy = enemy;
            State = HeroState.Fighting;
            return true;
        }

        void AnimateBody(float dt)
        {
            if (_body == null) return;

            bool moving = State != HeroState.Fighting;
            if (moving) _bobPhase += dt * 6f;
            else _bobPhase += dt * 12f;

            float offset = Mathf.Abs(Mathf.Sin(_bobPhase)) * (moving ? 0.06f : 0.11f);
            _body.localPosition = new Vector3(_body.localPosition.x, _bodyBaseY + offset, _body.localPosition.z);
        }

        /// <summary>Reddens as it takes damage, so a fight reads without a health bar.</summary>
        void ApplyTint()
        {
            if (_bodyRenderer == null) return;

            float hurt = MaxHealth > 0 ? 1f - Health / (float)MaxHealth : 0f;
            var tint = Color.Lerp(Color.white, HurtTint, hurt);
            var skin = new Color(_stats.Colour.r * tint.r, _stats.Colour.g * tint.g, _stats.Colour.b * tint.b);

            _bodyRenderer.GetPropertyBlock(_propertyBlock);
            MaterialLibrary.SetColor(_propertyBlock, skin);
            _bodyRenderer.SetPropertyBlock(_propertyBlock);
        }
    }
}
