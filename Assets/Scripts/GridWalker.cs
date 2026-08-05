using System.Collections.Generic;
using UnityEngine;

namespace DK
{
    /// <summary>
    /// Walks a transform along a grid route. Owns the path, the index into it, and the turning,
    /// so a state machine only has to say where it wants to go and whether it has arrived.
    ///
    /// A path is only ever valid from the cell it starts at, because <see cref="Advance"/>
    /// moves in a straight line to the next waypoint and only the grid guarantees that line is
    /// clear. <see cref="SetPath"/> therefore always routes from where the walker is standing
    /// right now, and clears the route when there is no way through — there is no way to ask
    /// this class to resume a stale one.
    /// </summary>
    public class GridWalker
    {
        public float MoveSpeed = 3f;
        public float TurnSpeed = 12f;

        readonly GridManager _grid;
        readonly Transform _transform;
        readonly List<Vector2Int> _path = new List<Vector2Int>();

        int _index;

        public GridWalker(GridManager grid, Transform transform)
        {
            _grid = grid;
            _transform = transform;
        }

        public Vector2Int CurrentCell => _grid.WorldToCell(_transform.position);

        /// <summary>True while there are waypoints left to walk.</summary>
        public bool HasPath => _index < _path.Count;

        /// <summary>Routes from the current cell. False when there is no way through.</summary>
        public bool SetPath(Vector2Int goal)
        {
            _index = 0;
            return Pathfinder.TryFindPath(_grid, CurrentCell, goal, _path);
        }

        /// <summary>Whether a route exists, without committing to walking it.</summary>
        public bool CanReach(Vector2Int goal, List<Vector2Int> scratch) =>
            Pathfinder.TryFindPath(_grid, CurrentCell, goal, scratch);

        public void Stop()
        {
            _path.Clear();
            _index = 0;
        }

        /// <summary>Advances along the route. Returns true while still moving.</summary>
        public bool Advance(float dt)
        {
            while (_index < _path.Count)
            {
                var waypoint = _grid.CellToWorld(_path[_index]);
                var position = _transform.position;
                var flatDelta = new Vector3(waypoint.x - position.x, 0f, waypoint.z - position.z);

                if (flatDelta.sqrMagnitude <= 0.0025f)
                {
                    _index++;
                    continue;
                }

                float step = MoveSpeed * dt;
                if (flatDelta.magnitude <= step)
                {
                    _transform.position = new Vector3(waypoint.x, position.y, waypoint.z);
                    _index++;
                    continue;
                }

                _transform.position = position + flatDelta.normalized * step;
                FaceTowards(waypoint, dt);
                return true;
            }

            return false;
        }

        public void FaceTowards(Vector3 worldPoint, float dt)
        {
            var flat = new Vector3(worldPoint.x - _transform.position.x, 0f,
                                   worldPoint.z - _transform.position.z);
            if (flat.sqrMagnitude < 0.0001f) return;

            var wanted = Quaternion.LookRotation(flat, Vector3.up);
            _transform.rotation = Quaternion.Slerp(_transform.rotation, wanted, 1f - Mathf.Exp(-TurnSpeed * dt));
        }
    }
}
