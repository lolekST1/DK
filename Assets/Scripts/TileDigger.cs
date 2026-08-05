using UnityEngine;

namespace DK
{
    /// <summary>
    /// Player input: raycasts the mouse against the ground plane, highlights the hovered tile
    /// and marks/unmarks tiles for digging. Dragging paints, like the original game.
    /// </summary>
    public class TileDigger : MonoBehaviour
    {
        GridManager _grid;
        Camera _camera;
        Transform _cursor;
        Renderer _cursorRenderer;
        MaterialPropertyBlock _propertyBlock;

        Vector2Int _hoveredCell;
        bool _hasHover;
        bool _dragMarking;
        bool _dragUnmarking;

        static readonly Color HoverDiggableColor = new Color(0.35f, 0.95f, 1f);
        static readonly Color HoverFloorColor = new Color(0.55f, 0.75f, 0.85f);

        // Picking is two maths-plane raycasts — no colliders anywhere in the scene.
        // Blocks stand a unit tall, so from an angled camera the top plane is what the
        // cursor is really over; the floor plane catches everything already dug out.
        static readonly Plane FloorPlane = new Plane(Vector3.up, Vector3.zero);
        static readonly Plane BlockTopPlane = new Plane(Vector3.up, new Vector3(0f, GridManager.BlockHeight, 0f));

        public void Configure(GridManager grid, Camera camera)
        {
            _grid = grid;
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

            UpdateHover();
            UpdateInput();
        }

        void UpdateHover()
        {
            _hasHover = false;

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

            bool diggable = _grid.IsDiggable(_hoveredCell.x, _hoveredCell.y);
            var position = _grid.CellToWorld(_hoveredCell);
            position.y = _grid.SurfaceHeight(_hoveredCell.x, _hoveredCell.y) + 0.03f;
            _cursor.position = position;

            _cursorRenderer.GetPropertyBlock(_propertyBlock);
            MaterialLibrary.SetColor(_propertyBlock, diggable ? HoverDiggableColor : HoverFloorColor);
            _cursorRenderer.SetPropertyBlock(_propertyBlock);
        }

        bool TryPick(Ray ray, Plane plane, out Vector2Int cell)
        {
            cell = default;
            if (!plane.Raycast(ray, out float distance)) return false;

            cell = _grid.WorldToCell(ray.GetPoint(distance));
            return _grid.InBounds(cell.x, cell.y);
        }

        void UpdateInput()
        {
            if (Input.GetMouseButtonDown(0)) _dragMarking = true;
            if (Input.GetMouseButtonUp(0)) _dragMarking = false;
            if (Input.GetMouseButtonDown(1)) _dragUnmarking = true;
            if (Input.GetMouseButtonUp(1)) _dragUnmarking = false;

            if (!_hasHover) return;

            if (_dragMarking) _grid.MarkForDigging(_hoveredCell.x, _hoveredCell.y);
            if (_dragUnmarking) _grid.UnmarkForDigging(_hoveredCell.x, _hoveredCell.y);
        }
    }
}
