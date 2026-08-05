using System;
using UnityEngine;

namespace DK
{
    /// <summary>What the mouse currently does to the tile under it.</summary>
    public enum PlayerTool
    {
        Dig,
        BuildTreasury,
        BuildLair,
        Sell,
    }

    /// <summary>
    /// The player's pointer. Raycasts the mouse against the ground plane, highlights the
    /// hovered tile and applies the selected tool to it. Dragging paints, like the original
    /// game, and right-drag always undoes whatever the left button does.
    /// </summary>
    public class PlayerTools : MonoBehaviour
    {
        GridManager _grid;
        RoomManager _rooms;
        ResourceManager _resources;
        Camera _camera;
        Transform _cursor;
        Renderer _cursorRenderer;
        MaterialPropertyBlock _propertyBlock;

        Vector2Int _hoveredCell;
        bool _hasHover;
        bool _dragPrimary;
        bool _dragSecondary;

        public PlayerTool CurrentTool { get; private set; } = PlayerTool.Dig;

        /// <summary>Why the hovered tile refuses the current tool, or null when it accepts it.</summary>
        public string HoverRefusal { get; private set; }

        public event Action<PlayerTool> ToolChanged;

        static readonly Color HoverDiggableColor = new Color(0.35f, 0.95f, 1f);
        static readonly Color HoverFloorColor = new Color(0.55f, 0.75f, 0.85f);
        static readonly Color HoverBuildableColor = new Color(0.40f, 0.95f, 0.45f);
        static readonly Color HoverBlockedColor = new Color(0.95f, 0.30f, 0.28f);
        static readonly Color HoverSellColor = new Color(1f, 0.62f, 0.20f);

        // Picking is two maths-plane raycasts — no colliders anywhere in the scene.
        // Blocks stand a unit tall, so from an angled camera the top plane is what the
        // cursor is really over; the floor plane catches everything already dug out.
        static readonly Plane FloorPlane = new Plane(Vector3.up, Vector3.zero);
        static readonly Plane BlockTopPlane = new Plane(Vector3.up, new Vector3(0f, GridManager.BlockHeight, 0f));

        public void Configure(GridManager grid, RoomManager rooms, ResourceManager resources, Camera camera)
        {
            _grid = grid;
            _rooms = rooms;
            _resources = resources;
            _camera = camera;
            _propertyBlock = new MaterialPropertyBlock();
            BuildCursor();
        }

        void BuildCursor()
        {
            var cursor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cursor.name = "HoverCursor";
            Destroy(cursor.GetComponent<Collider>());
            cursor.transform.SetParent(transform, false);
            cursor.transform.localScale = new Vector3(GridManager.TileSize * 0.96f, 0.04f, GridManager.TileSize * 0.96f);

            _cursorRenderer = cursor.GetComponent<Renderer>();
            _cursorRenderer.sharedMaterial = MaterialLibrary.CreateLit("DK_Cursor", HoverDiggableColor);

            _cursor = cursor.transform;
            _cursor.gameObject.SetActive(false);
        }

        void Update()
        {
            if (_grid == null || _camera == null) return;

            UpdateToolSelection();
            UpdateHover();
            UpdateInput();
        }

        // ---------------------------------------------------------------- tools

        void UpdateToolSelection()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1) || Input.GetKeyDown(KeyCode.Escape)) SelectTool(PlayerTool.Dig);
            if (Input.GetKeyDown(KeyCode.Alpha2)) SelectTool(PlayerTool.BuildTreasury);
            if (Input.GetKeyDown(KeyCode.Alpha3)) SelectTool(PlayerTool.BuildLair);
            if (Input.GetKeyDown(KeyCode.Alpha4)) SelectTool(PlayerTool.Sell);
        }

        public void SelectTool(PlayerTool tool)
        {
            if (CurrentTool == tool) return;

            CurrentTool = tool;
            ToolChanged?.Invoke(tool);
        }

        /// <summary>The room a build tool places, or None for tools that build nothing.</summary>
        public static RoomType RoomOf(PlayerTool tool)
        {
            switch (tool)
            {
                case PlayerTool.BuildTreasury: return RoomType.Treasury;
                case PlayerTool.BuildLair: return RoomType.Lair;
                default: return RoomType.None;
            }
        }

        // ---------------------------------------------------------------- hover

        void UpdateHover()
        {
            _hasHover = false;
            HoverRefusal = null;

            var ray = _camera.ScreenPointToRay(Input.mousePosition);

            // A hit on the top plane only counts if that cell still has a block standing.
            if (TryPick(ray, BlockTopPlane, out var topCell) && !_grid.IsWalkable(topCell))
            {
                _hoveredCell = topCell;
                _hasHover = true;
            }
            else if (TryPick(ray, FloorPlane, out var floorCell))
            {
                _hoveredCell = floorCell;
                _hasHover = true;
            }

            if (_cursor == null) return;

            _cursor.gameObject.SetActive(_hasHover);
            if (!_hasHover) return;

            var position = _grid.CellToWorld(_hoveredCell);
            // Clears the room slab and the gold piles sitting on dug-out floor.
            position.y = _grid.SurfaceHeight(_hoveredCell.x, _hoveredCell.y) + 0.09f;
            _cursor.position = position;

            _cursorRenderer.GetPropertyBlock(_propertyBlock);
            MaterialLibrary.SetColor(_propertyBlock, ResolveCursorColor());
            _cursorRenderer.SetPropertyBlock(_propertyBlock);
        }

        /// <summary>Tints the cursor for the active tool, and records why a tile said no.</summary>
        Color ResolveCursorColor()
        {
            int x = _hoveredCell.x, z = _hoveredCell.y;

            switch (CurrentTool)
            {
                case PlayerTool.Dig:
                    return _grid.IsDiggable(x, z) ? HoverDiggableColor : HoverFloorColor;

                case PlayerTool.Sell:
                    if (_rooms != null && _rooms.CanSell(x, z)) return HoverSellColor;
                    HoverRefusal = "nothing to sell here";
                    return HoverBlockedColor;

                default:
                    if (_rooms == null) return HoverBlockedColor;
                    if (_rooms.CanBuild(x, z, RoomOf(CurrentTool), out var reason)) return HoverBuildableColor;
                    HoverRefusal = reason;
                    return HoverBlockedColor;
            }
        }

        bool TryPick(Ray ray, Plane plane, out Vector2Int cell)
        {
            cell = default;
            if (!plane.Raycast(ray, out float distance)) return false;

            cell = _grid.WorldToCell(ray.GetPoint(distance));
            return _grid.InBounds(cell.x, cell.y);
        }

        // ---------------------------------------------------------------- input

        void UpdateInput()
        {
            if (Input.GetMouseButtonDown(0)) _dragPrimary = true;
            if (Input.GetMouseButtonUp(0)) _dragPrimary = false;
            if (Input.GetMouseButtonDown(1)) _dragSecondary = true;
            if (Input.GetMouseButtonUp(1)) _dragSecondary = false;

            if (!_hasHover) return;

            if (_dragPrimary) ApplyPrimary();
            if (_dragSecondary) ApplySecondary();
        }

        void ApplyPrimary()
        {
            int x = _hoveredCell.x, z = _hoveredCell.y;

            switch (CurrentTool)
            {
                case PlayerTool.Dig:
                    _grid.MarkForDigging(x, z);
                    break;

                case PlayerTool.Sell:
                    SellHovered();
                    break;

                default:
                    if (_rooms != null) _rooms.Build(x, z, RoomOf(CurrentTool));
                    break;
            }
        }

        /// <summary>Right-drag undoes the left: unmark while digging, tear out while building.</summary>
        void ApplySecondary()
        {
            if (CurrentTool == PlayerTool.Dig)
            {
                _grid.UnmarkForDigging(_hoveredCell.x, _hoveredCell.y);
                return;
            }

            SellHovered();
        }

        void SellHovered()
        {
            if (_rooms == null) return;
            if (!_rooms.Sell(_hoveredCell.x, _hoveredCell.y, out int lost)) return;

            if (_resources != null) _resources.ReportSpill(lost);
        }
    }
}
