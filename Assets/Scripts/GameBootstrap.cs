using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

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
        // Deliberately not serialized. A public field on a component is copied into the scene
        // asset the first time it is saved, and from then on the scene wins: changing a default
        // here would leave a clone still running the old number, with nothing on screen to say
        // why. Every other balance figure in this project lives in code — RoomCatalog,
        // CreatureCatalog, HeroCatalog — and these now do too.

        // --- grid ---
        // 32x32 holds roughly a hundred gold seams. At 20x20 a single raid could take most of
        // what the dungeon had mined, and there was not enough left in the rock to recover
        // before payday came round again.
        [NonSerialized] public int GridWidth = 32;
        [NonSerialized] public int GridDepth = 32;
        [NonSerialized] public int Seed = 1337;
        [NonSerialized] public float GoldChance = 0.10f;
        [NonSerialized] public int StartingChamberRadius = 2;

        // --- portal ---
        [NonSerialized] public int PortalRadius = 1;
        [NonSerialized] public float CreatureSpawnInterval = 20f;
        [NonSerialized] public float PaydayInterval = 60f;
        [NonSerialized] public int MaxCreatures = 8;

        // --- heroes ---
        [NonSerialized] public int HeroGateRadius = 1;
        [NonSerialized] public float FirstRaidDelay = 45f;
        [NonSerialized] public float RaidInterval = 90f;
        [NonSerialized] public int MaxHeroes = 4;
        [NonSerialized] public int WavesBeforeLord = 5;
        [NonSerialized] public int HeartHealth = DungeonHeart.DefaultHealth;

        // --- imps ---
        // More ground to cover, so more diggers. Still one crew, still one queue.
        [NonSerialized] public int ImpCount = 6;
        [NonSerialized] public float ImpMoveSpeed = 3.0f;
        [NonSerialized] public float ImpDigDuration = 1.2f;

        public GridManager Grid { get; private set; }
        public RoomManager Rooms { get; private set; }
        public LooseGold Spillage { get; private set; }
        public ResourceManager Economy { get; private set; }
        public IReadOnlyList<ImpAI> Imps { get; private set; }
        public CreatureManager Creatures { get; private set; }
        public HeroManager Heroes { get; private set; }
        public Battlefield Battlefield { get; private set; }
        public DungeonHeart Heart { get; private set; }
        public GameDirector Director { get; private set; }
        public CameraRig Rig { get; private set; }
        public PlayerTools Tools { get; private set; }

        void Awake()
        {
            // Terrain, then the rooms sitting on it, then the economy that reads those rooms.
            Grid = CreateGrid();
            Rooms = CreateRooms();
            Spillage = CreateLooseGold();
            Economy = CreateResourceManager();
            Battlefield = CreateBattlefield();
            Heart = CreateHeart();
            Creatures = CreateCreatureManager();
            Heroes = CreateHeroManager();
            Director = CreateDirector();

            // Lighting before the rig: the rig keeps the sun in step with its own yaw.
            var sun = CreateLighting();
            var camera = CreateCamera();
            Rig = CreateCameraRig(camera, sun);

            Imps = CreateImps();
            Tools = CreatePlayerTools(camera);
            CreateHud();

            ReportRendering();
        }

        ResourceManager CreateResourceManager()
        {
            var go = new GameObject("ResourceManager");
            go.transform.SetParent(transform, false);

            var economy = go.AddComponent<ResourceManager>();
            economy.Configure(Rooms, Spillage);
            return economy;
        }

        LooseGold CreateLooseGold()
        {
            var go = new GameObject("LooseGold");
            go.transform.SetParent(transform, false);

            var loose = go.AddComponent<LooseGold>();
            loose.Configure(Grid);
            return loose;
        }

        RoomManager CreateRooms()
        {
            var go = new GameObject("Rooms");
            go.transform.SetParent(transform, false);

            var rooms = go.AddComponent<RoomManager>();
            rooms.Configure(Grid);

            // The portal cavern is carved but sealed off: it exists from the first frame, and
            // reaching it is the first thing the player has a reason to dig towards.
            var portalCell = new Vector2Int(Grid.Width * 3 / 4, Grid.Depth * 3 / 4);
            Grid.CarveChamber(portalCell, PortalRadius);
            rooms.BuildPortal(portalCell, PortalRadius);

            // Opposite corner from the portal, so the two things the player digs towards pull
            // in different directions and one of them bites.
            var gateCell = new Vector2Int(Grid.Width / 4, Grid.Depth / 4);
            Grid.CarveChamber(gateCell, HeroGateRadius);
            rooms.BuildHeroGate(gateCell, HeroGateRadius);

            return rooms;
        }

        CreatureManager CreateCreatureManager()
        {
            var go = new GameObject("CreatureManager");
            go.transform.SetParent(transform, false);

            var creatures = go.AddComponent<CreatureManager>();
            creatures.SpawnInterval = CreatureSpawnInterval;
            creatures.PaydayInterval = PaydayInterval;
            creatures.MaxCreatures = MaxCreatures;
            creatures.Configure(Grid, Rooms, Economy, Battlefield);
            return creatures;
        }

        Battlefield CreateBattlefield()
        {
            var go = new GameObject("Battlefield");
            go.transform.SetParent(transform, false);
            return go.AddComponent<Battlefield>();
        }

        HeroManager CreateHeroManager()
        {
            var go = new GameObject("HeroManager");
            go.transform.SetParent(transform, false);

            var heroes = go.AddComponent<HeroManager>();
            heroes.FirstRaidDelay = FirstRaidDelay;
            heroes.RaidInterval = RaidInterval;
            heroes.MaxHeroes = MaxHeroes;
            heroes.WavesBeforeLord = WavesBeforeLord;
            heroes.Configure(Grid, Rooms, Economy, Spillage, Battlefield, Heart);
            return heroes;
        }

        DungeonHeart CreateHeart()
        {
            var go = new GameObject("DungeonHeart");
            go.transform.SetParent(transform, false);

            var heart = go.AddComponent<DungeonHeart>();
            heart.Configure(Battlefield, Rooms.HeartCell, Rooms.HeartCore, HeartHealth);
            return heart;
        }

        GameDirector CreateDirector()
        {
            var go = new GameObject("GameDirector");
            go.transform.SetParent(transform, false);

            var director = go.AddComponent<GameDirector>();
            director.Configure(Heart, Heroes);
            return director;
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

        CameraRig CreateCameraRig(Camera camera, Transform sun)
        {
            var go = new GameObject("CameraRig");
            go.transform.SetParent(transform, false);

            var rig = go.AddComponent<CameraRig>();
            rig.Configure(camera, Grid, sun);
            return rig;
        }

        /// <summary>
        /// One line in the log saying what the renderer actually ended up with. In a build this
        /// goes to the browser console, which is the only way to tell a setting that did not
        /// apply from one that applied and did not help — the difference between the Editor and
        /// a build has cost several rounds of guessing at screenshots.
        /// </summary>
        void ReportRendering()
        {
            var names = QualitySettings.names;
            int level = QualitySettings.GetQualityLevel();
            string quality = names != null && level >= 0 && level < names.Length ? names[level] : "?";

            Debug.Log($"[DK] render: quality={quality} msaa={QualitySettings.antiAliasing}x " +
                      $"shadowDistance={QualitySettings.shadowDistance:0} " +
                      $"pipeline={(GraphicsSettings.defaultRenderPipeline != null ? "URP" : "Built-in")} " +
                      $"screen={Screen.width}x{Screen.height} dpi={Screen.dpi:0}");
        }

        Transform CreateLighting()
        {
            var go = new GameObject("Sun", typeof(Light));
            go.transform.SetParent(transform, false);
            // Light from the south, because the fixed camera looks north and therefore only
            // ever sees south-facing block sides. A northern sun lit the faces nobody can see
            // and threw wall shadows into the pits that everybody can.
            go.transform.rotation = Quaternion.Euler(50f, 30f, 0f);

            var light = go.GetComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.88f);
            light.intensity = 1.15f;

            // No cast shadows. Every block top shares one normal, so they must all light the
            // same — and in a build they did not: the far half of the map came out dark with a
            // stepped edge across the middle, which is a shadow map failing, not lighting.
            // Shadow map size, cascade split and distance all come from the quality level,
            // which differs between the Game view and a player build, so this looked like a
            // WebGL-only fault. The blocks are flat-topped and read by colour and by the
            // shading difference between top and side faces; cast shadows added very little
            // and cost a whole class of platform-dependent breakage.
            light.shadows = LightShadows.None;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            // Slightly lower now that nothing is being lifted out of shadow.
            RenderSettings.ambientLight = new Color(0.32f, 0.32f, 0.38f);
            return go.transform;
        }

        IReadOnlyList<ImpAI> CreateImps()
        {
            var homes = PickHomeCells(Mathf.Max(1, ImpCount));
            var imps = new List<ImpAI>(homes.Count);

            for (int i = 0; i < homes.Count; i++)
                imps.Add(CreateImp(i, homes.Count, homes[i]));

            return imps;
        }

        /// <summary>
        /// Distinct walkable tiles nearest the base, so idle imps stand side by side instead
        /// of piling onto one spot. These are only fallbacks — once the player builds a lair
        /// the imps move in and <see cref="RoomManager.HomeFor"/> takes over.
        /// </summary>
        List<Vector2Int> PickHomeCells(int count)
        {
            var candidates = new List<Vector2Int>();
            var baseCell = Grid.BaseCell;

            for (int x = 0; x < Grid.Width; x++)
            for (int z = 0; z < Grid.Depth; z++)
            {
                if (!Grid.IsWalkable(x, z)) continue;
                // Keep the middle of the heart clear so imps do not stand inside the core.
                if (x == baseCell.x && z == baseCell.y) continue;
                candidates.Add(new Vector2Int(x, z));
            }

            candidates.Sort((a, b) =>
                (Mathf.Abs(a.x - baseCell.x) + Mathf.Abs(a.y - baseCell.y))
                .CompareTo(Mathf.Abs(b.x - baseCell.x) + Mathf.Abs(b.y - baseCell.y)));

            var homes = new List<Vector2Int>(count);
            for (int i = 0; i < count; i++)
                homes.Add(candidates.Count > 0 ? candidates[i % candidates.Count] : baseCell);

            return homes;
        }

        ImpAI CreateImp(int index, int total, Vector2Int homeCell)
        {
            var root = new GameObject($"Imp_{index}");
            root.transform.SetParent(transform, false);

            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            Destroy(body.GetComponent<Collider>());
            body.transform.SetParent(root.transform, false);
            body.transform.localScale = new Vector3(0.45f, 0.32f, 0.45f);
            body.transform.localPosition = new Vector3(0f, 0.32f, 0f);
            // A little colour spread so individual imps stay tellable apart in a crowd.
            float t = total > 1 ? index / (float)(total - 1) : 0f;
            var skin = new Color(0.70f + 0.14f * t, 0.24f + 0.20f * t, 0.36f - 0.10f * t);
            body.GetComponent<Renderer>().sharedMaterial =
                MaterialLibrary.CreateLit($"DK_Imp_{index}", skin);

            // A snout so you can tell which way the imp is facing.
            var snout = GameObject.CreatePrimitive(PrimitiveType.Cube);
            snout.name = "Snout";
            Destroy(snout.GetComponent<Collider>());
            snout.transform.SetParent(body.transform, false);
            snout.transform.localScale = new Vector3(0.5f, 0.35f, 0.6f);
            snout.transform.localPosition = new Vector3(0f, 0.35f, 0.85f);
            snout.GetComponent<Renderer>().sharedMaterial =
                MaterialLibrary.CreateLit("DK_ImpSnout", new Color(0.95f, 0.78f, 0.55f));

            // Shown only while the imp is hauling, so a glance tells you who is carrying.
            var nugget = GameObject.CreatePrimitive(PrimitiveType.Cube);
            nugget.name = "Nugget";
            Destroy(nugget.GetComponent<Collider>());
            nugget.transform.SetParent(root.transform, false);
            nugget.transform.localScale = new Vector3(0.26f, 0.20f, 0.26f);
            nugget.transform.localPosition = new Vector3(0f, 0.78f, 0f);
            nugget.GetComponent<Renderer>().sharedMaterial =
                MaterialLibrary.CreateLit("DK_ImpNugget", new Color(0.95f, 0.78f, 0.20f), 0.55f, 0.75f);
            nugget.SetActive(false);

            var imp = root.AddComponent<ImpAI>();
            imp.MoveSpeed = ImpMoveSpeed;
            imp.DigDuration = ImpDigDuration;
            imp.Configure(Grid, Economy, Rooms, Spillage, body.transform, nugget.transform, homeCell);
            return imp;
        }

        PlayerTools CreatePlayerTools(Camera camera)
        {
            var go = new GameObject("PlayerTools");
            go.transform.SetParent(transform, false);

            var tools = go.AddComponent<PlayerTools>();
            tools.Configure(Grid, Rooms, Economy, camera);
            return tools;
        }

        void CreateHud()
        {
            var go = new GameObject("HUD");
            go.transform.SetParent(transform, false);
            go.AddComponent<GameHud>().Configure(Economy, Rooms, Tools, Imps, Creatures, Heroes,
                                                 Heart, Director);
        }
    }
}
