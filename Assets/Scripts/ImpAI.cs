using System.Collections.Generic;
using UnityEngine;

namespace DK
{
    public enum ImpState
    {
        Idle,
        MoveToTarget,
        Digging,
        HaulGold,
        FetchGold,
        ReturnToBase,
    }

    /// <summary>
    /// A worker creature: picks the nearest reachable queued tile, walks to a tile adjacent
    /// to it, digs for a fixed time, and goes straight back for more rock.
    ///
    /// Mined gold drops where the seam was rather than being carried off, so digging never
    /// stops to run an errand. Collecting those piles is what an imp does when it has no rock
    /// left to break — with a crew, whoever runs out of digging first becomes the porter.
    ///
    /// Several imps share one grid, so each claims its target and never poaches another's.
    /// Gold still has to be walked into a vault to count, which is what makes building a
    /// treasury urgent.
    /// </summary>
    public class ImpAI : MonoBehaviour
    {
        public float MoveSpeed = 3.0f;
        public float TurnSpeed = 12f;
        public float DigDuration = 1.2f;

        /// <summary>
        /// How long an imp will hold gold it cannot bank before dumping it and going back to
        /// work. Without this a full vault quietly freezes the whole crew, which reads as a
        /// bug; losing the gold instead is a cost the player can see and fix.
        /// </summary>
        public float HaulPatience = 8f;

        /// <summary>
        /// The same patience when every vault tile is genuinely full. Waiting is only worth it
        /// while there is space somewhere we cannot currently reach — when there is none at
        /// all, nothing changes by standing still, and a crew parked holding gold is exactly
        /// what "the imps stopped digging" looks like from the outside.
        /// </summary>
        public float FullVaultPatience = 1.5f;

        public ImpState State { get; private set; } = ImpState.Idle;

        /// <summary>Gold mined and not yet banked.</summary>
        public int CarriedGold { get; private set; }

        public bool HasDigTarget { get; private set; }
        public Vector2Int DigTarget => _digTarget;

        /// <summary>
        /// Where this imp stands when it has nothing to do. Imps do not sleep and do not take
        /// lairs: every lair an imp claimed was one the portal could not send a creature to,
        /// and with a crew of six that quietly swallowed the first six a player built. Nothing
        /// on screen said so — the portal just reported no free lair.
        /// </summary>
        public Vector2Int HomeCell => _fallbackHome;

        GridManager _grid;
        ResourceManager _resources;
        RoomManager _rooms;
        LooseGold _loose;
        Transform _body;
        Transform _carryIcon;

        readonly List<Vector2Int> _path = new List<Vector2Int>();
        readonly List<Vector2Int> _candidatePath = new List<Vector2Int>();
        readonly List<Vector2Int> _queuedScratch = new List<Vector2Int>();
        readonly List<Vector2Int> _depositScratch = new List<Vector2Int>();
        readonly List<Vector2Int> _looseScratch = new List<Vector2Int>();

        int _pathIndex;
        Vector2Int _digTarget;
        Vector2Int _haulTarget;
        Vector2Int _fetchTarget;
        Vector2Int _fallbackHome;
        bool _haulHasTarget;
        bool _hasFetchTarget;
        float _haulWaitTimer;
        float _digTimer;
        float _repathCooldown;
        float _bobPhase;
        float _bodyBaseY;

        static readonly Vector2Int[] Neighbours =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
        };

        public void Configure(GridManager grid, ResourceManager resources, RoomManager rooms,
                              LooseGold loose, Transform body, Transform carryIcon,
                              Vector2Int fallbackHome)
        {
            _grid = grid;
            _resources = resources;
            _rooms = rooms;
            _loose = loose;
            _body = body;
            _carryIcon = carryIcon;
            if (_body != null) _bodyBaseY = _body.localPosition.y;

            _fallbackHome = grid.IsWalkable(fallbackHome) ? fallbackHome : grid.BaseCell;

            transform.position = grid.CellToWorld(_fallbackHome);
            UpdateCarryIcon();
        }

        void OnDestroy()
        {
            ReleaseTarget();
            ReleaseFetchTarget();
        }

        public Vector2Int CurrentCell => _grid.WorldToCell(transform.position);

        void Update()
        {
            if (_grid == null) return;

            float dt = Time.deltaTime;
            _repathCooldown -= dt;

            switch (State)
            {
                case ImpState.Idle:
                    TickIdle();
                    break;
                case ImpState.MoveToTarget:
                    TickMoveToTarget(dt);
                    break;
                case ImpState.Digging:
                    TickDigging(dt);
                    break;
                case ImpState.HaulGold:
                    TickHaulGold(dt);
                    break;
                case ImpState.FetchGold:
                    TickFetchGold(dt);
                    break;
                case ImpState.ReturnToBase:
                    TickReturnToBase(dt);
                    break;
            }

            AnimateBody(dt);
        }

        // ---------------------------------------------------------------- states

        void TickIdle()
        {
            if (_repathCooldown > 0f) return;
            _repathCooldown = 0.2f;

            // Read home first: this lookup is what moves an imp into a lair the player just
            // built, and going idle between tiles is the only moment it is cheap to do.
            var home = HomeCell;

            if (CarriedGold > 0)
            {
                _haulHasTarget = false;
                _repathCooldown = 0f;
                State = ImpState.HaulGold;
                return;
            }

            if (TrySelectDigTarget())
            {
                State = ImpState.MoveToTarget;
                return;
            }

            // Only once this imp has no rock left to break: the dig queue is the player's
            // order, and gold on the floor keeps. An imp that finds nothing to dig while the
            // others are still busy becomes the one who does the fetching.
            if (TrySelectLooseGold())
            {
                State = ImpState.FetchGold;
                return;
            }

            // Nothing left to claim: idle at home, walking back if we wandered off.
            if (CurrentCell != home && SetPath(home)) State = ImpState.ReturnToBase;
        }

        void TickMoveToTarget(float dt)
        {
            // The player can cancel a mark, or the tile can vanish, while we are en route.
            if (!_grid.IsMarkedForDigging(_digTarget.x, _digTarget.y))
            {
                ReleaseTarget();
                State = ImpState.Idle;
                return;
            }

            if (FollowPath(dt)) return;

            State = ImpState.Digging;
            _digTimer = 0f;
        }

        void TickDigging(float dt)
        {
            if (!_grid.IsDiggable(_digTarget.x, _digTarget.y) ||
                !_grid.IsMarkedForDigging(_digTarget.x, _digTarget.y))
            {
                ReleaseTarget();
                State = ImpState.Idle;
                return;
            }

            FaceTowards(_grid.CellToWorld(_digTarget), dt);

            _digTimer += dt;
            if (_digTimer < DigDuration) return;

            int gold = _grid.DigOut(_digTarget.x, _digTarget.y);

            // Gold falls where the seam was and stays there. Carrying it in immediately would
            // cost the imp the rest of its dig, and clearing rock is what the player asked for
            // — somebody collects the piles once there is nothing left to dig.
            if (gold > 0)
            {
                if (_loose != null) _loose.Drop(_digTarget, gold);
                else CarriedGold += gold;
            }

            ReleaseTarget();
            _repathCooldown = 0f;

            if (CarriedGold > 0)
            {
                UpdateCarryIcon();
                _haulHasTarget = false;
                _haulWaitTimer = 0f;
                State = ImpState.HaulGold;
                return;
            }

            State = ImpState.Idle;
        }

        void TickHaulGold(float dt)
        {
            if (CarriedGold <= 0)
            {
                _haulHasTarget = false;
                State = ImpState.Idle;
                return;
            }

            if (_haulHasTarget)
            {
                if (FollowPath(dt)) return;

                // Arrived. Whatever does not fit stays on our back — another imp may have
                // topped the tile up while we were walking.
                int banked = _resources != null ? _resources.Bank(_haulTarget, CarriedGold) : 0;
                CarriedGold -= banked;
                UpdateCarryIcon();

                _haulHasTarget = false;
                _repathCooldown = banked > 0 ? 0f : 0.5f;
                if (banked > 0) _haulWaitTimer = 0f;

                if (CarriedGold <= 0) State = ImpState.Idle;
                return;
            }

            // Nowhere to bank. Give it a while, then dump the load and get back to work —
            // an imp frozen forever holding gold reads as a broken imp, not as a full vault.
            float patience = _rooms != null && _rooms.FreeCapacity == 0 ? FullVaultPatience : HaulPatience;

            _haulWaitTimer += dt;
            if (_haulWaitTimer >= patience)
            {
                // Dropped, not destroyed: the gold stays on the floor and somebody comes back
                // for it once there is room. The tally still counts it, because a pile the
                // player never notices is the same problem as gold that vanished.
                if (_loose != null) _loose.Drop(CurrentCell, CarriedGold);
                if (_resources != null) _resources.ReportSpill(CarriedGold);
                CarriedGold = 0;
                UpdateCarryIcon();

                _haulWaitTimer = 0f;
                _repathCooldown = 0f;
                State = ImpState.Idle;
                return;
            }

            // Drift home while waiting: standing in the corridor the other imps are using
            // would look like a bug even though it is only a full vault.
            if (FollowPath(dt)) return;

            if (_repathCooldown > 0f) return;
            _repathCooldown = 0.5f;

            if (TrySelectHaulTarget())
            {
                _haulHasTarget = true;
                _haulWaitTimer = 0f;
                return;
            }

            SetPath(HomeCell);
        }

        void TickFetchGold(float dt)
        {
            // Another imp may have got there first, and the player may have dug the floor out
            // from under a pile that no longer matters.
            if (_loose == null || _loose.AmountAt(_fetchTarget) <= 0)
            {
                ReleaseFetchTarget();
                State = ImpState.Idle;
                return;
            }

            if (FollowPath(dt)) return;

            int picked = _loose.Take(_fetchTarget);
            ReleaseFetchTarget();

            if (picked <= 0)
            {
                State = ImpState.Idle;
                return;
            }

            CarriedGold += picked;
            UpdateCarryIcon();

            _haulHasTarget = false;
            _haulWaitTimer = 0f;
            _repathCooldown = 0f;
            State = ImpState.HaulGold;
        }

        void TickReturnToBase(float dt)
        {
            // New work always wins over standing around at base.
            if (_grid.QueuedCount > 0 && TrySelectDigTarget())
            {
                State = ImpState.MoveToTarget;
                return;
            }

            if (FollowPath(dt)) return;

            State = ImpState.Idle;
        }

        // ---------------------------------------------------------------- helpers

        /// <summary>
        /// Picks the nearest queued tile we can actually reach, and paths to the walkable
        /// tile next to it. Candidates are tried in order of distance, so the first success
        /// is the cheapest one worth taking.
        /// </summary>
        bool TrySelectDigTarget()
        {
            if (_grid.QueuedCount == 0) return false;

            var from = CurrentCell;

            _queuedScratch.Clear();
            foreach (var cell in _grid.QueuedTiles) _queuedScratch.Add(cell);
            _queuedScratch.Sort((a, b) => Manhattan(from, a).CompareTo(Manhattan(from, b)));

            foreach (var target in _queuedScratch)
            {
                if (!_grid.IsDiggable(target.x, target.y)) continue;
                if (_grid.IsClaimedByOther(target, this)) continue;

                for (int i = 0; i < Neighbours.Length; i++)
                {
                    var stand = target + Neighbours[i];
                    if (!_grid.IsWalkable(stand)) continue;
                    if (!Pathfinder.TryFindPath(_grid, from, stand, _candidatePath)) continue;

                    if (!_grid.TryClaimTile(target, this)) break;

                    _path.Clear();
                    _path.AddRange(_candidatePath);
                    _pathIndex = 0;
                    _digTarget = target;
                    HasDigTarget = true;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Nearest reachable pile of spilled gold, claimed so the crew spreads out over
        /// several piles instead of all chasing the same one. Only worth doing when there is
        /// somewhere to put it — otherwise the imp would pick the pile up and drop it again.
        /// </summary>
        bool TrySelectLooseGold()
        {
            if (_loose == null || _loose.Total <= 0) return false;
            if (_rooms == null || _rooms.FreeCapacity <= 0) return false;

            var from = CurrentCell;
            _loose.CollectPiles(from, this, _looseScratch);

            foreach (var cell in _looseScratch)
            {
                if (!_grid.IsWalkable(cell)) continue;
                if (!Pathfinder.TryFindPath(_grid, from, cell, _candidatePath)) continue;
                if (!_loose.TryClaim(cell, this)) continue;

                _path.Clear();
                _path.AddRange(_candidatePath);
                _pathIndex = 0;
                _fetchTarget = cell;
                _hasFetchTarget = true;
                return true;
            }

            return false;
        }

        void ReleaseFetchTarget()
        {
            if (!_hasFetchTarget) return;

            _hasFetchTarget = false;
            if (_loose != null) _loose.Release(_fetchTarget, this);
        }

        /// <summary>Nearest storage tile with space that we can actually walk to.</summary>
        bool TrySelectHaulTarget()
        {
            if (_rooms == null) return false;

            var from = CurrentCell;
            _rooms.CollectDepositCells(from, _depositScratch);

            foreach (var cell in _depositScratch)
            {
                if (!_grid.IsWalkable(cell)) continue;
                if (!Pathfinder.TryFindPath(_grid, from, cell, _candidatePath)) continue;

                _path.Clear();
                _path.AddRange(_candidatePath);
                _pathIndex = 0;
                _haulTarget = cell;
                return true;
            }

            return false;
        }

        void ReleaseTarget()
        {
            if (!HasDigTarget) return;

            HasDigTarget = false;
            if (_grid != null) _grid.ReleaseTile(_digTarget, this);
        }

        /// <summary>
        /// Routes to a goal from wherever we are standing right now.
        ///
        /// Every path has to start at the current cell, because <see cref="FollowPath"/> walks
        /// a straight line to the next waypoint and only the grid guarantees that line is
        /// clear. Re-running an old path from index zero used to send an imp diagonally
        /// through solid rock to catch up with a waypoint it had already passed.
        /// </summary>
        bool SetPath(Vector2Int goal)
        {
            _pathIndex = 0;
            return Pathfinder.TryFindPath(_grid, CurrentCell, goal, _path);
        }

        /// <summary>Advances along the current path. Returns true while still moving.</summary>
        bool FollowPath(float dt)
        {
            while (_pathIndex < _path.Count)
            {
                var waypoint = _grid.CellToWorld(_path[_pathIndex]);
                var position = transform.position;
                var flatDelta = new Vector3(waypoint.x - position.x, 0f, waypoint.z - position.z);

                if (flatDelta.sqrMagnitude <= 0.0025f)
                {
                    _pathIndex++;
                    continue;
                }

                float step = MoveSpeed * dt;
                if (flatDelta.magnitude <= step)
                {
                    transform.position = new Vector3(waypoint.x, position.y, waypoint.z);
                    _pathIndex++;
                    continue;
                }

                transform.position = position + flatDelta.normalized * step;
                FaceTowards(waypoint, dt);
                return true;
            }

            return false;
        }

        void FaceTowards(Vector3 worldPoint, float dt)
        {
            var flat = new Vector3(worldPoint.x - transform.position.x, 0f, worldPoint.z - transform.position.z);
            if (flat.sqrMagnitude < 0.0001f) return;

            var wanted = Quaternion.LookRotation(flat, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, wanted, 1f - Mathf.Exp(-TurnSpeed * dt));
        }

        void UpdateCarryIcon()
        {
            if (_carryIcon != null) _carryIcon.gameObject.SetActive(CarriedGold > 0);
        }

        void AnimateBody(float dt)
        {
            if (_body == null) return;

            // Cheap readability cue: bob fast while digging, gently while walking.
            float amplitude = State == ImpState.Digging ? 0.14f : 0.05f;
            float frequency = State == ImpState.Digging ? 14f : 6f;
            bool moving = State == ImpState.MoveToTarget ||
                          State == ImpState.ReturnToBase ||
                          State == ImpState.HaulGold;

            if (State == ImpState.Digging || moving) _bobPhase += dt * frequency;
            else _bobPhase = Mathf.MoveTowards(_bobPhase, 0f, dt * 4f);

            float offset = Mathf.Abs(Mathf.Sin(_bobPhase)) * amplitude;
            _body.localPosition = new Vector3(_body.localPosition.x, _bodyBaseY + offset, _body.localPosition.z);
        }

        static int Manhattan(Vector2Int a, Vector2Int b) =>
            Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }
}
