# Dungeon Keeper–style Prototype

A vertical slice of the core Dungeon Keeper loop: mark rock for digging, an imp paths
to it, digs it out, gold seams pay out. No combat, no rooms, one creature.

## Running it

1. Open the folder in Unity Hub with **Unity 6.5 (6000.5.x)**. First open resolves
   packages and imports assets, which takes a couple of minutes.
2. Open `Assets/Scenes/Bootstrap.unity`.
3. Press Play.

That is the whole setup — there is nothing to drag into an Inspector. The scene holds a
single `GameBootstrap` GameObject; the grid, camera, lighting, imp, input handler and HUD
are all created and wired at runtime in `GameBootstrap.Awake()`.

### Controls

| Input | Action |
| --- | --- |
| Left mouse (hold to paint) | Mark rock / gold tiles for digging |
| Right mouse (hold to paint) | Cancel a mark |
| WASD, arrow keys, or screen edge | Pan the camera |
| Scroll wheel | Zoom |

## WebGL build

Menu: **Dungeon Keeper Prototype → Build WebGL**, which writes to `Builds/WebGL/`.

Headless equivalent:

```bash
Unity -quit -batchmode -nographics -projectPath . \
      -executeMethod DK.EditorTools.BuildWebGL.Build
```

The build is uncompressed on purpose, so it serves from any static host without
`Content-Encoding` configuration — including `python3 -m http.server` inside
`Builds/WebGL/`.

`Builds/` is gitignored; the build output is an artifact, not source.

## Layout

```
Assets/Scripts/
  GameBootstrap.cs    Creates and wires the whole scene at runtime
  GridManager.cs      Tile states, dig queue, block visuals
  Pathfinder.cs       4-directional A* over the tile array
  ImpAI.cs            Idle → MoveToTarget → Digging → ReturnToBase
  TileDigger.cs       Mouse picking, hover cursor, mark/unmark
  CameraRig.cs        45° rig: pan, zoom, clamped to the grid
  ResourceManager.cs  Gold counter
  GoldHud.cs          Runtime-built Canvas + TextMeshPro readout
  MaterialLibrary.cs  Runtime materials, pipeline-agnostic shader lookup
  TileState.cs        Rock / Dug / GoldSeam
Assets/Editor/
  ProjectAutoSetup.cs Text serialization, URP asset, scene in build settings
  BuildWebGL.cs       WebGL build entry point
Assets/Settings/      URP pipeline and renderer assets, created on first Editor load
Tools/HeadlessTests/  Unity-free smoke test of the dig loop
```

## Design notes

**Everything procedural.** No prefabs, no Inspector references. `GameBootstrap` has a few
tunable public fields (grid size, seed, gold density, imp speed) with working defaults; the
scene runs untouched from a fresh clone.

**No NavMesh.** Tiles appear and vanish at runtime, which NavMesh handles poorly. A
hand-rolled A* over the array we already own is simpler, faster and predictable. It reuses
its scratch buffers, so pathfinding does not allocate per search.

**No colliders.** Mouse picking is two maths-plane raycasts — one at the top of the blocks,
one at floor level — so there is not a single collider in the scene.

**Pipeline-agnostic rendering.** `ProjectAutoSetup` creates and assigns a URP asset on first
load if the package is present. `MaterialLibrary` picks its shader from whichever pipeline is
actually active, so the prototype also renders correctly on Built-in if URP setup is skipped.

**TextMeshPro fallback.** TMP Essential Resources are committed, so the HUD normally uses
TextMeshPro. Reading `TMP_Settings` throws outright in a project that has never imported
them, so the availability check is defensive and drops to legacy uGUI text, then to IMGUI —
the gold counter shows up regardless of what a clone is missing.

## Headless smoke test

Unity is not needed to check the game logic:

```bash
sudo apt-get install -y mono-mcs   # once
./Tools/HeadlessTests/run.sh
```

It type-checks every script against stub Unity types, then runs the real `GridManager`,
`Pathfinder` and `ImpAI` through a full dig cycle — generation determinism, marking rules,
A* shortest paths and failure cases, corridor digging, the gold payout, the walk back to
base, and an unreachable mark not wedging the state machine.

This covers logic only. Rendering, input, and the WebGL build still need the Editor.

## Out of scope for this prototype

Multiple imps, rooms, walls, a dungeon heart, combat, hero incursions, save/load, and
verticality. The grid is stored as a 3D array with a single Y layer, so adding layers later
does not mean rewriting the data model.
