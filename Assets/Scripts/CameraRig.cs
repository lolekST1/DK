using UnityEngine;

namespace DK
{
    /// <summary>
    /// Fixed-pitch strategy camera: WASD/arrows or screen-edge to pan, wheel to zoom,
    /// Q/E to swing the rig around in 90° steps, pivot clamped to the grid so you cannot
    /// lose the dungeon off-screen.
    /// </summary>
    public class CameraRig : MonoBehaviour
    {
        public float PitchDegrees = 45f;
        public float YawDegrees = 0f;
        public float RotationStepDegrees = 90f;
        public float RotationSpeed = 9f;
        public float PanSpeed = 12f;
        public float EdgePanMargin = 14f;
        public float ZoomSpeed = 12f;
        public float MinDistance = 8f;
        public float MaxDistance = 30f;
        public bool EdgePanEnabled = true;

        /// <summary>
        /// Kept in step with the rig's yaw. The camera only ever sees the block faces turned
        /// towards it, so a light fixed in world space would leave those faces unlit from half
        /// the viewing angles. Swinging it with the rig keeps walls readable from all four.
        /// </summary>
        public Transform Sun;
        public float SunYawOffset = 30f;

        Camera _camera;
        Bounds _panBounds;
        float _distance = 22f;

        /// <summary>How far back the camera is sitting right now.</summary>
        public float Distance => _distance;
        float _targetYaw;
        float _currentYaw;
        float _sunPitch = 50f;

        public void Configure(Camera camera, GridManager grid, Transform sun)
        {
            _camera = camera;
            Sun = sun;

            var center = grid.Center;
            var size = new Vector3(grid.Width * GridManager.TileSize, 0f, grid.Depth * GridManager.TileSize);
            _panBounds = new Bounds(center, size);

            // Far enough out to hold the whole grid in frame at this pitch, whatever size it
            // is, and starting there rather than part way in. A ground square of side S needs
            // roughly S * 1.4 of camera distance at 45 degrees and a 55 degree field of view;
            // the ceiling is well past that so there is somewhere left to pull back to.
            float span = Mathf.Max(size.x, size.z);
            MaxDistance = Mathf.Max(MaxDistance, span * 2.0f);
            _distance = Mathf.Clamp(span * 1.4f, MinDistance, MaxDistance);

            _targetYaw = YawDegrees;
            _currentYaw = YawDegrees;

            if (Sun != null) _sunPitch = Sun.rotation.eulerAngles.x;

            transform.position = center;
            transform.rotation = Quaternion.Euler(0f, _currentYaw, 0f);

            _camera.transform.SetParent(transform, false);
            ApplyCameraTransform();
            ApplySunRotation();
        }

        void Update()
        {
            if (_camera == null) return;

            float dt = Time.deltaTime;
            HandleRotation(dt);
            HandleZoom(dt);
            HandlePan(dt);
            ApplyCameraTransform();
        }

        void HandleRotation(float dt)
        {
            if (Input.GetKeyDown(KeyCode.Q)) _targetYaw -= RotationStepDegrees;
            if (Input.GetKeyDown(KeyCode.E)) _targetYaw += RotationStepDegrees;

            if (Mathf.Abs(Mathf.DeltaAngle(_currentYaw, _targetYaw)) < 0.01f)
            {
                _currentYaw = _targetYaw;
                return;
            }

            _currentYaw = Mathf.LerpAngle(_currentYaw, _targetYaw, 1f - Mathf.Exp(-RotationSpeed * dt));
            transform.rotation = Quaternion.Euler(0f, _currentYaw, 0f);
            ApplySunRotation();
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

            // Pan in the rig's own yaw so "up" always means "away from the camera",
            // whichever way it has been rotated.
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

        void ApplySunRotation()
        {
            if (Sun == null) return;
            Sun.rotation = Quaternion.Euler(_sunPitch, _currentYaw + SunYawOffset, 0f);
        }
    }
}
