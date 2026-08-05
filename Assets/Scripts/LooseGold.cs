using System;
using System.Collections.Generic;
using UnityEngine;

namespace DK
{
    /// <summary>
    /// Gold lying on the dungeon floor. An imp that mined a seam it could not bank drops its
    /// load here rather than deleting it, and any imp will come back for it once there is
    /// vault space again.
    ///
    /// Neither terrain nor a room, so it is neither <see cref="GridManager"/>'s nor
    /// <see cref="RoomManager"/>'s to own: a pile sits on a dug tile regardless of what has
    /// been built there, and it disappears the moment somebody picks it up.
    /// </summary>
    public class LooseGold : MonoBehaviour
    {
        GridManager _grid;

        int[,] _amounts;
        Transform[,] _piles;

        readonly List<Vector2Int> _tiles = new List<Vector2Int>();

        // One imp per pile, so a spill does not pull the whole crew across the map.
        readonly Dictionary<Vector2Int, object> _claims = new Dictionary<Vector2Int, object>();

        Material _material;
        Transform _root;

        /// <summary>Gold on the floor right now, across every pile.</summary>
        public int Total { get; private set; }

        public int PileCount => _tiles.Count;

        /// <summary>Raised when a pile appeared, grew or was picked up. Carries the new total.</summary>
        public event Action<int> Changed;

        public void Configure(GridManager grid)
        {
            _grid = grid;

            _amounts = new int[grid.Width, grid.Depth];
            _piles = new Transform[grid.Width, grid.Depth];

            _root = new GameObject("LooseGold").transform;
            _root.SetParent(transform, false);

            _material = MaterialLibrary.CreateLit("DK_LooseGold", new Color(0.88f, 0.70f, 0.22f), 0.5f, 0.7f);
        }

        // ---------------------------------------------------------------- queries

        public int AmountAt(Vector2Int cell) =>
            _amounts != null && _grid.InBounds(cell.x, cell.y) ? _amounts[cell.x, cell.y] : 0;

        public bool IsClaimedByOther(Vector2Int cell, object imp) =>
            _claims.TryGetValue(cell, out var owner) && !ReferenceEquals(owner, imp);

        /// <summary>Every pile nobody else has claimed, nearest first.</summary>
        public void CollectPiles(Vector2Int from, object imp, List<Vector2Int> result)
        {
            result.Clear();

            for (int i = 0; i < _tiles.Count; i++)
            {
                var cell = _tiles[i];
                if (IsClaimedByOther(cell, imp)) continue;

                result.Add(cell);
            }

            result.Sort((a, b) =>
                (Mathf.Abs(a.x - from.x) + Mathf.Abs(a.y - from.y))
                .CompareTo(Mathf.Abs(b.x - from.x) + Mathf.Abs(b.y - from.y)));
        }

        // ---------------------------------------------------------------- moving gold

        /// <summary>Dumps gold on a tile. Piles merge, so a bad patch does not litter the floor.</summary>
        public void Drop(Vector2Int cell, int amount)
        {
            if (amount <= 0 || _amounts == null || !_grid.InBounds(cell.x, cell.y)) return;

            if (_amounts[cell.x, cell.y] == 0) _tiles.Add(cell);

            _amounts[cell.x, cell.y] += amount;
            Total += amount;

            UpdatePile(cell);
            Changed?.Invoke(Total);
        }

        /// <summary>Picks a pile up whole. Returns what was actually there.</summary>
        public int Take(Vector2Int cell)
        {
            if (_amounts == null || !_grid.InBounds(cell.x, cell.y)) return 0;

            int amount = _amounts[cell.x, cell.y];
            if (amount <= 0) return 0;

            _amounts[cell.x, cell.y] = 0;
            _tiles.Remove(cell);
            _claims.Remove(cell);
            Total -= amount;

            UpdatePile(cell);
            Changed?.Invoke(Total);
            return amount;
        }

        // ---------------------------------------------------------------- claims

        public bool TryClaim(Vector2Int cell, object imp)
        {
            if (AmountAt(cell) <= 0) return false;
            if (_claims.TryGetValue(cell, out var owner)) return ReferenceEquals(owner, imp);

            _claims[cell] = imp;
            return true;
        }

        public void Release(Vector2Int cell, object imp)
        {
            if (_claims.TryGetValue(cell, out var owner) && ReferenceEquals(owner, imp))
                _claims.Remove(cell);
        }

        // ---------------------------------------------------------------- visuals

        void UpdatePile(Vector2Int cell)
        {
            int amount = _amounts[cell.x, cell.y];

            if (amount <= 0)
            {
                if (_piles[cell.x, cell.y] != null) Destroy(_piles[cell.x, cell.y].gameObject);
                _piles[cell.x, cell.y] = null;
                return;
            }

            if (_piles[cell.x, cell.y] == null)
            {
                var pile = GameObject.CreatePrimitive(PrimitiveType.Cube);
                pile.name = $"Loose_{cell.x}_{cell.y}";
                Destroy(pile.GetComponent<Collider>());
                pile.transform.SetParent(_root, false);
                pile.GetComponent<Renderer>().sharedMaterial = _material;
                _piles[cell.x, cell.y] = pile.transform;
            }

            // Flatter and wider than a vault pile: gold on the floor should read as a mess to
            // be tidied up, not as storage.
            float fill = Mathf.Clamp(amount / (float)(GridManager.GoldPerSeam * 3), 0.15f, 1f);
            float height = 0.06f + 0.14f * fill;
            float width = 0.42f + 0.22f * fill;

            _piles[cell.x, cell.y].localScale = new Vector3(width, height, width);
            _piles[cell.x, cell.y].localPosition = _grid.CellToWorld(cell) + Vector3.up * (height * 0.5f);
        }
    }
}
