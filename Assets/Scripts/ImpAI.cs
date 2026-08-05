using System.Collections.Generic;
using UnityEngine;

namespace DK
{
    public enum ImpState
    {
        Idle,
        MoveToTarget,
        Digging,
        ReturnToBase,
    }

    /// <summary>
    /// The single worker creature: picks the nearest reachable queued tile, walks to a tile
    /// adjacent to it, digs for a fixed time, then looks for more work or heads back to base.
    /// </summary>
    public class ImpAI : MonoBehaviour
    {
        public float MoveSpeed = 3.0f;
        public float TurnSpeed = 12f;
        public float DigDuration = 1.2f;

        public ImpState State { get; private set; } = ImpState.Idle;

        GridManager _grid;
        ResourceManager _resources;
        Transform _body;

        readonly List<Vector2Int> _path = new List<Vector2Int>();
        readonly List<Vector2Int> _candidatePath = new List<Vector2Int>();
        readonly List<Vector2Int> _queuedScratch = new List<Vector2Int>();

        int _pathIndex;
        Vector2Int _digTarget;
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

        public void Configure(GridManager grid, ResourceManager resources, Transform body)
        {
            _grid = grid;
            _resources = resources;
            _body = body;
            if (_body != null) _bodyBaseY = _body.localPosition.y;

            transform.position = grid.CellToWorld(grid.BaseCell);
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

            if (TrySelectDigTarget())
            {
                State = ImpState.MoveToTarget;
                return;
            }

            // Nothing queued: idle at base, walking back if we wandered off.
            if (CurrentCell != _grid.BaseCell &&
                Pathfinder.TryFindPath(_grid, CurrentCell, _grid.BaseCell, _path))
            {
                _pathIndex = 0;
                State = ImpState.ReturnToBase;
            }
        }

        void TickMoveToTarget(float dt)
        {
            // The player can cancel a mark, or the tile can vanish, while we are en route.
            if (!_grid.IsMarkedForDigging(_digTarget.x, _digTarget.y))
            {
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
                State = ImpState.Idle;
                return;
            }

            FaceTowards(_grid.CellToWorld(_digTarget), dt);

            _digTimer += dt;
            if (_digTimer < DigDuration) return;

            int gold = _grid.DigOut(_digTarget.x, _digTarget.y);
            if (gold > 0 && _resources != null) _resources.AddGold(gold);

            _repathCooldown = 0f;
            State = ImpState.Idle;
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

                for (int i = 0; i < Neighbours.Length; i++)
                {
                    var stand = target + Neighbours[i];
                    if (!_grid.IsWalkable(stand)) continue;
                    if (!Pathfinder.TryFindPath(_grid, from, stand, _candidatePath)) continue;

                    _path.Clear();
                    _path.AddRange(_candidatePath);
                    _pathIndex = 0;
                    _digTarget = target;
                    return true;
                }
            }

            return false;
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

        void AnimateBody(float dt)
        {
            if (_body == null) return;

            // Cheap readability cue: bob fast while digging, gently while walking.
            float amplitude = State == ImpState.Digging ? 0.14f : 0.05f;
            float frequency = State == ImpState.Digging ? 14f : 6f;
            bool moving = State == ImpState.MoveToTarget || State == ImpState.ReturnToBase;

            if (State == ImpState.Digging || moving) _bobPhase += dt * frequency;
            else _bobPhase = Mathf.MoveTowards(_bobPhase, 0f, dt * 4f);

            float offset = Mathf.Abs(Mathf.Sin(_bobPhase)) * amplitude;
            _body.localPosition = new Vector3(_body.localPosition.x, _bodyBaseY + offset, _body.localPosition.z);
        }

        static int Manhattan(Vector2Int a, Vector2Int b) =>
            Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }
}
