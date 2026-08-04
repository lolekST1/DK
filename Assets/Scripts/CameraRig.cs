using UnityEngine;

namespace DK
{
    /// <summary>
    /// Fixed-angle strategy camera: WASD/arrows or screen-edge to pan, wheel to zoom,
    /// pivot clamped to the grid so you cannot lose the dungeon off-screen. Axis-aligned by
    /// default -- a yawed rig turns the square grid into a diamond that wastes screen space.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public float PitchDegrees = 45f;
        public float YawDegrees = 0f;
        public float PanSpeed = 12f;
        public float EdgePanMargin = 14f;
        public float ZoomSpeed = 12f;
        public float MinDistance = 8f;
        public float MaxDistance = 30f;
        public bool EdgePanEnabled = true;

        Camera _camera;
        Bounds _panBounds;
        float _distance = 22f;

        public void Configure(Camera camera, GridManager grid)
        {
            _camera = camera;

            var center = grid.Center;
            var size = new Vector3(grid.Width * GridManager.TileSize, 0f, grid.Depth * GridManager.TileSize);
            _panBounds = new Bounds(center, size);

            transform.position = center;
            transform.rotation = Quaternion.Euler(0f, YawDegrees, 0f);

            _camera.transform.SetParent(transform, false);
            ApplyCameraTransform();
        }

        void Update()
        {
            if (_camera == null) return;

            float dt = Time.deltaTime;
            HandleZoom(dt);
            HandlePan(dt);
            ApplyCameraTransform();
        }

        void HandleZoom(float dt)
        {
            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Approximately(scroll, 0f)) return;

            _distance = Mathf.Clamp(_distance - scroll * ZoomSpeed * dt * 10f, MinDistance, MaxDistance);
        }

        void HandlePan(float dt)
        {
            var input = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));

            if (EdgePanEnabled && Application.isFocused)
            {
                var mouse = Input.mousePosition;
                if (mouse.x >= 0f && mouse.x <= Screen.width && mouse.y >= 0f && mouse.y <= Screen.height)
                {
                    if (mouse.x <= EdgePanMargin) input.x -= 1f;
                    if (mouse.x >= Screen.width - EdgePanMargin) input.x += 1f;
                    if (mouse.y <= EdgePanMargin) input.y -= 1f;
                    if (mouse.y >= Screen.height - EdgePanMargin) input.y += 1f;
                }
            }

            if (input.sqrMagnitude < 0.0001f) return;
            input = Vector2.ClampMagnitude(input, 1f);

            // Pan in the rig's own yaw so "up" always means "away from the camera".
            var forward = transform.forward;
            var right = transform.right;
            forward.y = 0f;
            right.y = 0f;

            // Panning stays proportional to zoom, so it feels the same close up and far out.
            float speed = PanSpeed * dt * Mathf.Lerp(0.6f, 1.6f, Mathf.InverseLerp(MinDistance, MaxDistance, _distance));
            var move = (forward.normalized * input.y + right.normalized * input.x) * speed;

            var position = transform.position + move;
            position.x = Mathf.Clamp(position.x, _panBounds.min.x, _panBounds.max.x);
            position.z = Mathf.Clamp(position.z, _panBounds.min.z, _panBounds.max.z);
            position.y = 0f;
            transform.position = position;
        }

        void ApplyCameraTransform()
        {
            var localRotation = Quaternion.Euler(PitchDegrees, 0f, 0f);
            _camera.transform.localRotation = localRotation;
            _camera.transform.localPosition = localRotation * Vector3.back * _distance;
        }
    }
}
