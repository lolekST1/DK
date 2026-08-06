using System;
using System.Collections.Generic;
using UnityEngine;

namespace DK
{
    /// <summary>
    /// Owns the tile grid: state, dig queue and the block visuals.
    /// Everything (arrays, meshes, materials) is built at runtime from Configure() —
    /// there is nothing to wire in the Inspector.
    /// </summary>
    public class GridManager : MonoBehaviour
    {
        public const float TileSize = 1f;
        public const float BlockHeight = 1f;
        public const int GoldPerSeam = 50;

        /// <summary>How much of its tile a block fills. The remainder is the seam between them.</summary>
        public const float BlockInset = 0.96f;

        // Layout is a 3D array with a single Y layer for now; verticality is out of scope,
        // but the storage shape means adding layers later does not change the data model.
        public int Width { get; private set; } = 20;
        public int Height { get; private set; } = 1;
        public int Depth { get; private set; } = 20;

        public Vector2Int BaseCell { get; private set; }

        TileState[,,] _states;
        bool[,,] _marked;
        GameObject[,,] _blocks;
        Renderer[,,] _renderers;

        readonly HashSet<Vector2Int> _queued = new HashSet<Vector2Int>();

        // Which digger has taken responsibility for which queued tile. Without this, every
        // idle imp picks the same nearest tile and they all walk to it together.
        readonly Dictionary<Vector2Int, object> _claims = new Dictionary<Vector2Int, object>();
        MaterialPropertyBlock _propertyBlock;

        Material _blockMaterial;
        Material _floorMaterial;

        static readonly Color RockColor = new Color(0.56f, 0.53f, 0.48f);
        static readonly Color GoldColor = new Color(0.92f, 0.74f, 0.16f);
        static readonly Color MarkedRockColor = new Color(0.85f, 0.38f, 0.12f);
        static readonly Color MarkedGoldColor = new Color(1.00f, 0.55f, 0.10f);
        static readonly Color FloorColor = new Color(0.19f, 0.17f, 0.16f);

        /// <summary>Raised whenever a tile's state or dig-mark changes (x, z).</summary>
        public event Action<int, int> TileChanged;

        /// <summary>Number of tiles currently queued for digging.</summary>
        public int QueuedCount => _queued.Count;

        public IReadOnlyCollection<Vector2Int> QueuedTiles => _queued;

        /// <summary>Builds the grid. Call once, before anything else uses the grid.</summary>
        public void Configure(int width, int depth, int seed, float goldChance, int startingChamberRadius)
        {
            Width = Mathf.Max(2, width);
            Depth = Mathf.Max(2, depth);
            Height = 1;

            _states = new TileState[Width, Height, Depth];
            _marked = new bool[Width, Height, Depth];
            _blocks = new GameObject[Width, Height, Depth];
            _renderers = new Renderer[Width, Height, Depth];
            _propertyBlock = new MaterialPropertyBlock();

            _blockMaterial = MaterialLibrary.CreateLit("DK_Block", Color.white);
            _floorMaterial = MaterialLibrary.CreateLit("DK_Floor", FloorColor);

            BaseCell = new Vector2Int(Width / 2, Depth / 2);

            Generate(seed, goldChance);
            BuildFloor();
            BuildBlocks();
            CarveStartingChamber(startingChamberRadius);
        }

        void Generate(int seed, float goldChance)
        {
            var rng = new System.Random(seed);
            for (int x = 0; x < Width; x++)
            for (int z = 0; z < Depth; z++)
                _states[x, 0, z] = rng.NextDouble() < goldChance ? TileState.GoldSeam : TileState.Rock;
        }

        void BuildFloor()
        {
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = "Floor";
            Destroy(floor.GetComponent<Collider>());
            floor.transform.SetParent(transform, false);
            floor.transform.localScale = new Vector3(Width * TileSize, 0.2f, Depth * TileSize);
            floor.transform.localPosition = new Vector3(Width * TileSize * 0.5f, -0.1f, Depth * TileSize * 0.5f);
            floor.GetComponent<Renderer>().sharedMaterial = _floorMaterial;
        }

        void BuildBlocks()
        {
            var blocksRoot = new GameObject("Blocks").transform;
            blocksRoot.SetParent(transform, false);

            for (int x = 0; x < Width; x++)
            for (int z = 0; z < Depth; z++)
            {
                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = $"Block_{x}_{z}";
                // Picking is done with a maths plane raycast, so colliders are dead weight.
                Destroy(block.GetComponent<Collider>());
                block.transform.SetParent(blocksRoot, false);
                // Slightly under a full tile, so the dark floor shows through as a seam and
                // the rock reads as blocks. With cast shadows off there is nothing else to
                // separate one block top from the next: they share a normal and a colour, and
                // a field of them came out as one flat sheet.
                block.transform.localScale = new Vector3(TileSize * BlockInset, BlockHeight,
                                                         TileSize * BlockInset);
                block.transform.localPosition = CellToWorld(x, z) + Vector3.up * (BlockHeight * 0.5f);

                var renderer = block.GetComponent<Renderer>();
                renderer.sharedMaterial = _blockMaterial;

                _blocks[x, 0, z] = block;
                _renderers[x, 0, z] = renderer;
                ApplyTint(x, z);
            }
        }

        /// <summary>
        /// Digs out a square of tiles immediately, with no queue and no imp. Used for the
        /// places the map starts with rather than the ones the player earns: the opening
        /// chamber and the portal cavern.
        /// </summary>
        public void CarveChamber(Vector2Int centre, int radius)
        {
            for (int x = centre.x - radius; x <= centre.x + radius; x++)
            for (int z = centre.y - radius; z <= centre.y + radius; z++)
            {
                if (!InBounds(x, z)) continue;

                _states[x, 0, z] = TileState.Dug;
                _marked[x, 0, z] = false;
                _queued.Remove(new Vector2Int(x, z));
                _claims.Remove(new Vector2Int(x, z));
                if (_blocks[x, 0, z] != null) _blocks[x, 0, z].SetActive(false);
                TileChanged?.Invoke(x, z);
            }
        }

        void CarveStartingChamber(int radius)
        {
            // The imp needs somewhere to stand before anything has been dug.
            for (int x = BaseCell.x - radius; x <= BaseCell.x + radius; x++)
            for (int z = BaseCell.y - radius; z <= BaseCell.y + radius; z++)
            {
                if (!InBounds(x, z)) continue;
                _states[x, 0, z] = TileState.Dug;
                _marked[x, 0, z] = false;
                if (_blocks[x, 0, z] != null) _blocks[x, 0, z].SetActive(false);
                TileChanged?.Invoke(x, z);
            }
        }

        // ---------------------------------------------------------------- queries

        public bool InBounds(int x, int z) => x >= 0 && z >= 0 && x < Width && z < Depth;

        public TileState GetTileState(int x, int z) => InBounds(x, z) ? _states[x, 0, z] : TileState.Rock;

        public TileState GetTileState(int x, int y, int z) =>
            InBounds(x, z) && y >= 0 && y < Height ? _states[x, y, z] : TileState.Rock;

        /// <summary>Walkable == already dug out. Out-of-bounds is never walkable.</summary>
        public bool IsWalkable(int x, int z) => InBounds(x, z) && _states[x, 0, z] == TileState.Dug;

        public bool IsWalkable(Vector2Int cell) => IsWalkable(cell.x, cell.y);

        public bool IsDiggable(int x, int z) => InBounds(x, z) && _states[x, 0, z] != TileState.Dug;

        public bool IsMarkedForDigging(int x, int z) => InBounds(x, z) && _marked[x, 0, z];

        /// <summary>True when some other digger has already taken this tile.</summary>
        public bool IsClaimedByOther(Vector2Int cell, object digger) =>
            _claims.TryGetValue(cell, out var holder) && !ReferenceEquals(holder, digger);

        public Vector3 CellToWorld(int x, int z) =>
            new Vector3((x + 0.5f) * TileSize, 0f, (z + 0.5f) * TileSize);

        public Vector3 CellToWorld(Vector2Int cell) => CellToWorld(cell.x, cell.y);

        public Vector2Int WorldToCell(Vector3 world) => new Vector2Int(
            Mathf.FloorToInt(world.x / TileSize),
            Mathf.FloorToInt(world.z / TileSize));

        public Vector3 Center => new Vector3(Width * TileSize * 0.5f, 0f, Depth * TileSize * 0.5f);

        // ---------------------------------------------------------------- mutations

        /// <summary>Queues a rock/gold tile for digging. Returns false if it was not diggable.</summary>
        public bool MarkForDigging(int x, int z)
        {
            if (!IsDiggable(x, z) || _marked[x, 0, z]) return false;

            _marked[x, 0, z] = true;
            _queued.Add(new Vector2Int(x, z));
            ApplyTint(x, z);
            TileChanged?.Invoke(x, z);
            return true;
        }

        public bool UnmarkForDigging(int x, int z)
        {
            if (!InBounds(x, z) || !_marked[x, 0, z]) return false;

            _marked[x, 0, z] = false;
            _queued.Remove(new Vector2Int(x, z));
            _claims.Remove(new Vector2Int(x, z));
            ApplyTint(x, z);
            TileChanged?.Invoke(x, z);
            return true;
        }

        /// <summary>
        /// Takes responsibility for digging a queued tile. Returns false when another digger
        /// holds it, or when it is no longer queued.
        /// </summary>
        public bool TryClaimTile(Vector2Int cell, object digger)
        {
            if (!IsDiggable(cell.x, cell.y) || !IsMarkedForDigging(cell.x, cell.y)) return false;

            if (_claims.TryGetValue(cell, out var holder)) return ReferenceEquals(holder, digger);

            _claims[cell] = digger;
            return true;
        }

        /// <summary>Gives a claimed tile back. Claims held by others are left alone.</summary>
        public void ReleaseTile(Vector2Int cell, object digger)
        {
            if (_claims.TryGetValue(cell, out var holder) && ReferenceEquals(holder, digger))
                _claims.Remove(cell);
        }

        /// <summary>Digs a tile out. Returns the gold awarded (0 for plain rock).</summary>
        public int DigOut(int x, int z)
        {
            if (!IsDiggable(x, z)) return 0;

            int gold = _states[x, 0, z] == TileState.GoldSeam ? GoldPerSeam : 0;

            _states[x, 0, z] = TileState.Dug;
            _marked[x, 0, z] = false;
            _queued.Remove(new Vector2Int(x, z));
            _claims.Remove(new Vector2Int(x, z));
            if (_blocks[x, 0, z] != null) _blocks[x, 0, z].SetActive(false);

            TileChanged?.Invoke(x, z);
            return gold;
        }

        // ---------------------------------------------------------------- visuals

        /// <summary>Deterministic per-tile brightness, so the same map always looks the same.</summary>
        static float CellShade(int x, int z)
        {
            int hash = (x * 73856093) ^ (z * 19349663);
            hash = (hash ^ (hash >> 13)) * 1274126177;
            return 0.94f + ((hash >> 16) & 0xFF) / 255f * 0.12f;
        }

        void ApplyTint(int x, int z)
        {
            var renderer = _renderers[x, 0, z];
            if (renderer == null) return;

            bool gold = _states[x, 0, z] == TileState.GoldSeam;
            Color color = _marked[x, 0, z]
                ? (gold ? MarkedGoldColor : MarkedRockColor)
                : (gold ? GoldColor : RockColor);

            // A few per cent of brightness either way, fixed per tile. Enough that a wall of
            // rock has texture rather than being one colour across a third of the screen.
            float shade = CellShade(x, z);
            color = new Color(color.r * shade, color.g * shade, color.b * shade, color.a);

            renderer.GetPropertyBlock(_propertyBlock);
            MaterialLibrary.SetColor(_propertyBlock, color);
            renderer.SetPropertyBlock(_propertyBlock);
        }

        /// <summary>Top surface height of a tile, used for placing the hover cursor.</summary>
        public float SurfaceHeight(int x, int z) => IsWalkable(x, z) ? 0f : BlockHeight;
    }
}
