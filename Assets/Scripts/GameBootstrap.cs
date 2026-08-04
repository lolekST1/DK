using UnityEngine;

namespace DK
{
    /// <summary>
    /// The only thing that needs to exist in the scene. Creates and wires the whole
    /// prototype at runtime — grid, camera, lighting, imp, input and HUD — so a fresh
    /// clone runs with no Inspector work beyond opening the scene and pressing Play.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GameBootstrap : MonoBehaviour
    {
        [Header("Grid")]
        public int GridWidth = 20;
        public int GridDepth = 20;
        public int Seed = 1337;
        [Range(0f, 0.5f)] public float GoldChance = 0.10f;
        public int StartingChamberRadius = 2;

        [Header("Imp")]
        public float ImpMoveSpeed = 3.0f;
        public float ImpDigDuration = 1.2f;

        public GridManager Grid { get; private set; }
        public ResourceManager Economy { get; private set; }
        public ImpAI Imp { get; private set; }
        public CameraRig Rig { get; private set; }

        void Awake()
        {
            Economy = CreateResourceManager();
            Grid = CreateGrid();

            var camera = CreateCamera();
            Rig = CreateCameraRig(camera);
            CreateLighting();

            Imp = CreateImp();
            CreateTileDigger(camera);
            CreateHud();
        }

        ResourceManager CreateResourceManager()
        {
            var go = new GameObject("ResourceManager");
            go.transform.SetParent(transform, false);
            return go.AddComponent<ResourceManager>();
        }

        GridManager CreateGrid()
        {
            var go = new GameObject("Grid");
            go.transform.SetParent(transform, false);

            var grid = go.AddComponent<GridManager>();
            grid.Configure(GridWidth, GridDepth, Seed, GoldChance, StartingChamberRadius);
            return grid;
        }

        Camera CreateCamera()
        {
            var go = new GameObject("Main Camera", typeof(Camera));
            go.tag = "MainCamera";

            var camera = go.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.06f, 0.09f);
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 200f;
            camera.fieldOfView = 55f;
            return camera;
        }

        CameraRig CreateCameraRig(Camera camera)
        {
            var go = new GameObject("CameraRig");
            go.transform.SetParent(transform, false);

            var rig = go.AddComponent<CameraRig>();
            rig.Configure(camera, Grid);
            return rig;
        }

        void CreateLighting()
        {
            var go = new GameObject("Sun", typeof(Light));
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(48f, 140f, 0f);

            var light = go.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.intensity = 1.15f;
            light.shadows = LightShadows.Soft;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.28f, 0.28f, 0.34f);
        }

        ImpAI CreateImp()
        {
            var root = new GameObject("Imp");
            root.transform.SetParent(transform, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.45f, 0.32f, 0.45f);
            body.transform.localPosition = new Vector3(0f, 0.32f, 0f);
            body.GetComponent<Renderer>().sharedMaterial =
                MaterialLibrary.CreateLit("DK_Imp", new Color(0.75f, 0.28f, 0.32f));

            // A snout so you can tell which way the imp is facing.
            var snout = GameObject.CreatePrimitive(PrimitiveType.Cube);
            snout.name = "Snout";
            Destroy(snout.GetComponent<Collider>());
            snout.transform.SetParent(body.transform, false);
            snout.transform.localScale = new Vector3(0.5f, 0.35f, 0.6f);
            snout.transform.localPosition = new Vector3(0f, 0.35f, 0.85f);
            snout.GetComponent<Renderer>().sharedMaterial =
                MaterialLibrary.CreateLit("DK_ImpSnout", new Color(0.95f, 0.78f, 0.55f));

            var imp = root.AddComponent<ImpAI>();
            imp.MoveSpeed = ImpMoveSpeed;
            imp.DigDuration = ImpDigDuration;
            imp.Configure(Grid, Economy, body.transform);
            return imp;
        }

        void CreateTileDigger(Camera camera)
        {
            var go = new GameObject("TileDigger");
            go.transform.SetParent(transform, false);
            go.AddComponent<TileDigger>().Configure(Grid, camera);
        }

        void CreateHud()
        {
            var go = new GameObject("HUD");
            go.transform.SetParent(transform, false);
            go.AddComponent<GoldHud>().Configure(Economy);
        }
    }
}
