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

## Beyond the prototype
The vertical slice above is built, verified in-Editor and as a WebGL build, and merged.
The scope list above is the original brief and is kept as written; what actually shipped has
moved past it in places, recorded here.

Landed since:
- Camera rotation (Q/E) with the sun following the rig.
- Multiple imps sharing one dig queue via per-tile claims in `GridManager`.
- **Rooms and the gold economy.** A `RoomManager` layer on top of terrain: a 3×3 dungeon
  heart placed at bootstrap, player-built treasuries and lairs painted with the mouse and
  paid for out of stored gold, and selling for half back. `RoomCatalog` holds every cost and
  capacity, so balancing is one file.
- **Gold is hauled, not credited.** Mined gold is carried by the imp to a vault tile with
  free space; `ImpAI` gained a `HaulGold` state. An imp with nowhere to bank waits, then
  spills. `ResourceManager` is now a facade over the vaults rather than an int of its own,
  so `AddGold(int)` from the brief no longer exists — use `Bank(cell, amount)`.
- **Lairs.** Creatures claim a lair tile unprompted. Imps do not sleep and never take one:
  sharing the pool meant a crew of six swallowed the first six lairs a player built, and the
  portal then reported no free lair with nine of them on the map.
- **Mined gold falls on the floor, as in the original.** `LooseGold` owns the piles, with
  per-pile claims; `ImpAI` gained a `FetchGold` state that runs *after* digging, so imps clear
  rock first and collect only when they have no dig work left. `AddGold`-style crediting and
  mid-dig hauling are both gone.
- **The portal and its creatures.** A `Portal` room the map places in a sealed cavern, plus
  `CreatureManager` (arrivals, payday, departures) and `CreatureAI` (sleep and wage needs).
  Arrivals are gated on a dug route to the portal and a free lair; unpaid creatures redden
  and walk back out. `CreatureCatalog` holds the balance, mirroring `RoomCatalog`.
- Two renames as their jobs widened: `TileDigger` → `PlayerTools` (dig, build and sell
  tools), `GoldHud` → `GameHud` (gold, tool bar, status line).

Deliberately still simplified: dug floor is owned immediately, with no per-tile claiming
step for the imps — see the design notes in `README.md`.

`GridWalker` owns path following and turning for everything that walks the grid.
`CreatureAI` uses it; `ImpAI` deliberately does not, because its route selection commits a
tile claim in the same step and is easier to read as one piece.

- **Combat and hero raids.** A `HeroGate` room sealed in rock like the portal, `HeroManager`
  (raid clock, roster, loot stolen and recovered) and `HeroAI` (advance, rob a vault tile,
  fight, escape). `Battlefield` + `ICombatant` keep creatures and heroes from referencing each
  other. Heroes steal rather than destroy, so there is still no game-over state.

- **A run you can win or lose.** `DungeonHeart` is an `ICombatant` structure with health;
  `ICombatant.IsStructure` keeps ordinary raiders from attacking buildings. `HeroKind.Lord`
  ignores the vault and besieges the heart, and `HeroManager` sends him after
  `WavesBeforeLord` ordinary raids. `GameDirector` watches for either ending and stops the
  clock.

- **A last stand that scales with the garrison.** Being hit re-engages the attacker, and that
  used to restart the swing clock, so whoever was outnumbered got a free swing per blow taken —
  the Lord's damage output grew with the size of the crowd fighting him and he could clear a
  full roster alone. Dead creatures also went on swinging for the frame before the manager
  cleared them, which is what made the harness's last-stand matrix agree with the old numbers.
  With both fixed, the Lord is tuned against the crowd that actually arrives (1100 health):
  four or five defenders lose the heart, six or seven hold it with losses, ten walk away clean.
- **Wounds close in a lair.** `CreatureCatalog.HealPerSecond`, spent in `CreatureState.Sleeping`
  and nowhere else. A creature under half health goes to bed unprompted. Before this, damage was
  permanent and the garrison was worn down over the knight waves with no way back.

Balance figures worth knowing: a full roster's wages have to stay under what a crew of imps
can mine, and `PayrollBalanceChecks` in the harness asserts it. `GameBootstrap`'s tunables are
`[NonSerialized]` on purpose — a public field would be copied into `Bootstrap.unity` and the
scene would then silently outrank the code.

- **More kinds.** `CreatureKind` gained `Fly` and `Troll`, `HeroKind` gained `Thief`. The
  portal rotates through `CreatureCatalog.All`; ordinary raids alternate knight and thief.
  `HeroCatalog.Entry.FightsBack` is the whole of what makes a thief a different problem.

- **The training room.** `RoomType.TrainingRoom`, `CreatureState.Training`, and levels that
  add 20% health and damage each for 40 gold and 20 seconds, capped at four. Deliberately not
  a wage increase: training is a capital cost, so the payroll balance stays where it was.

Verticality was considered and dropped: doing it properly means `Vector2Int` becoming
`Vector3Int` across every file, and the terrain-elevation version that keeps 2D coordinates
buys visual depth rather than the stacked floors the original has. Not worth either price yet.

Watch out for: materials are created at runtime, so nothing in the project references a
shader, so a build strips them all. `ProjectAutoSetup` keeps them in Always Included Shaders.
Without that, `Shader.Find` returns null in the player and the game renders unlit — in the
Editor everything looks right, which is what makes it expensive to find.

## Workflow
Follow the usual approach: work independently, self-fix compile/runtime
errors, and deliver a version ready to test in-Editor and as a WebGL build.
Flag any Unity-API assumptions that turned out to be wrong along the way.
