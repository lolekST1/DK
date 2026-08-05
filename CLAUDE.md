# Dungeon Keeper–style Prototype — Claude Code Kickoff Brief

## Goal
A minimal, playable 3D prototype proving the core "dig a dungeon" loop from
Dungeon Keeper: the player marks rock tiles for digging, an imp creature
autonomously paths to them, digs them out, and gold tiles yield a resource.
No combat, no room-building, no multiple creatures yet — this is a vertical
slice of the single most important loop.

## Tech stack
- Unity 6.5 (6000.5.x) — install via Unity Hub if not already present
- URP (Universal Render Pipeline), 3D template
- C#, no external packages beyond what ships with Unity 6 (no NavMesh package needed — see below)
- Build target: WebGL, for fast iteration and easy sharing (Vercel/itch.io)

## Core architectural rule
**Build the scene procedurally from code, not by hand in the Editor.**
Grid, tiles, and the imp should be instantiated and wired entirely at
runtime from a single bootstrap script (e.g. `GameBootstrap.cs`) in an
otherwise-empty scene. Avoid hand-placed prefabs with Inspector-wired
references wherever possible — anything that requires clicking in the
Editor Inspector is a manual step a human has to do later, and it's the
main source of friction in AI-assisted Unity development. If a prefab is
genuinely needed (e.g. for the tile mesh), keep its Inspector surface
minimal and set everything else from code.

## Version control
This project lives in a GitHub repo from the start (not a local-only
folder) — needed so changes are diffable/revertable while Claude Code is
working independently. Set up at project init:
1. `.gitignore` — use Unity's official template (ignore `Library/`, `Temp/`,
   `obj/`, `Build/`, `Logs/`, `UserSettings/`, `MemoryCaptures/`)
2. In `Edit → Project Settings → Editor`: set Asset Serialization to
   *Force Text* and Version Control mode to *Visible Meta Files*. Without
   this, scenes/prefabs serialize as binary and are effectively
   unreviewable in diffs/PRs.
3. Git LFS is not needed yet (prototype uses primitives, no real art
   assets) — add it later if/when actual models or textures are imported.
4. Commit in small, reviewable steps rather than one giant commit at the end.

## Scope for this prototype
1. **GridManager**
   - 3D array of tiles, e.g. 20×1×20 (single Y-layer to start — no verticality yet)
   - Tile states: `Rock`, `Dug`, `GoldSeam`
   - Generates the grid at startup from a simple seeded pattern (a few gold
     seam tiles scattered in, rest rock)
   - Exposes `MarkForDigging(x, z)` and `GetTileState(x, z)`

2. **TileDigger**
   - Mouse raycast against the grid plane → highlight hovered tile
   - Click on a `Rock` or `GoldSeam` tile → mark it queued for digging
     (visual: outline or color tint)
   - When an imp finishes digging a tile, flip its state to `Dug` and
     (if it was `GoldSeam`) award gold via a simple `ResourceManager.AddGold(int)`

3. **ImpAI**
   - Simple explicit state machine: `Idle → MoveToTarget → Digging → ReturnToBase → Idle`
   - Custom A* pathfinding over the grid array (4-directional is fine to
     start). Do **not** use Unity's NavMesh — it doesn't handle tiles
     being removed/added at runtime well, and a hand-rolled A* over a
     known grid is simpler and more predictable here anyway.
   - Picks the nearest queued tile, walks to an adjacent walkable tile,
     "digs" for N seconds (simple timer, no animation needed yet), then
     returns to an idle/base point

4. **CameraRig**
   - Fixed ~45° angled camera looking down at the grid
   - WASD or edge-pan to move, scroll wheel to zoom, clamped to grid bounds

5. **ResourceManager**
   - Just an int gold counter + a basic on-screen UI text (Canvas + TextMeshPro)

## Explicitly out of scope (do not build yet)
- Multiple imps / creature spawning
- Room building, walls, dungeon heart
- Combat, enemies, hero incursions
- Save/load
- Any hand-placed scene content beyond the bootstrap script

## Acceptance criteria
- Press Play in Unity Editor (or open the WebGL build) → grid renders →
  clicking a rock/gold tile queues it → the imp paths to it, digs it out,
  gold tiles increment the counter → imp returns and picks the next queued tile.
- No manual Inspector wiring required to run the scene from a fresh clone
  (aside from opening the one bootstrap scene).
- Produces a working WebGL build in `Builds/WebGL/`.

## Workflow
Follow the usual approach: work independently, self-fix compile/runtime
errors, and deliver a version ready to test in-Editor and as a WebGL build.
Flag any Unity-API assumptions that turned out to be wrong along the way.
