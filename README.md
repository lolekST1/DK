# Dungeon Keeper–style Prototype

A vertical slice of the core Dungeon Keeper loop: mark rock for digging, imps path to it and
dig it out, then haul what they mine back to a vault you had to build and pay for. A dungeon
heart sits at the centre, lairs give everyone somewhere to sleep, and a portal sealed in the
rock sends creatures once you dig your way to it — creatures that expect wages every payday.
No combat yet.

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
| `1` / `Esc` | Dig tool (default) |
| `2` | Build treasury — 50 gold a tile, holds 250 |
| `3` | Build lair — 100 gold a tile, one imp each |
| `4` | Sell tool — tears a room out for half its cost |
| Left mouse (hold to paint) | Apply the selected tool |
| Right mouse (hold to paint) | Undo it: unmark while digging, sell while building |
| WASD, arrow keys, or screen edge | Pan the camera |
| Q / E | Rotate the camera 90° |
| Scroll wheel | Zoom |

The cursor turns green where the selected room will go and red where it will not; the HUD
status line says why.

### The loop

You start with a 3×3 dungeon heart holding 100 gold and 225 of vault space. Mark rock, the
imps dig it. A gold seam does **not** credit a counter — the imp picks the gold up, carries it
to the nearest vault tile with room, and drops it there, so the heart fills up fast and you
have to spend your opening 100 gold on a treasury. An imp that finds every vault full waits
about eight seconds and then dumps its load on the floor, which the HUD reports as spilled
gold. Build a lair and an imp moves in on its own; imps with somewhere to sleep dig 30% faster.

The portal sits in a sealed cavern three quarters of the way across the map. Dig a route to it
and creatures start arriving — but only while a lair is standing empty, and imps sleep in the
same lairs, so housing is a real budget. Every creature then bills the vault on payday. A
dungeon that cannot pay watches its creatures redden, and after three missed paydays they walk
back out through the portal. The HUD says what the portal is waiting for and what payroll costs.

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
  GridManager.cs      Terrain: tile states, dig queue, per-tile claims, block visuals
  RoomManager.cs      Rooms on top of terrain: heart, vault gold, lair ownership, slabs
  RoomCatalog.cs      Cost / capacity / colour per room type — the whole economy balance
  RoomType.cs         None / DungeonHeart / Treasury / Lair / Portal
  CreatureManager.cs  Portal roster: arrivals, payday, departures
  CreatureAI.cs       Idle → GoingToLair → Sleeping / Wandering / Leaving
  CreatureCatalog.cs  Wage, fatigue and patience per creature — the whole creature balance
  Pathfinder.cs       4-directional A* over the tile array
  ImpAI.cs            Idle → MoveToTarget → Digging → HaulGold → ReturnToBase, one per imp
  PlayerTools.cs      Mouse picking, hover cursor, dig / build / sell tools
  CameraRig.cs        45° rig: pan, zoom, 90° rotation, clamped to the grid
  ResourceManager.cs  Economy facade over the vaults: spend, bank, lifetime tallies
  GameHud.cs          Runtime-built Canvas: gold, tool bar, status line
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

**A crew, not a worker.** `GameBootstrap.ImpCount` imps share one dig queue. Each claims a
tile in `GridManager` before walking to it and releases the claim when it finishes or the
player cancels the mark, so imps split the work instead of all converging on whichever tile
happens to be nearest. Each also gets its own home tile, so idle imps stand side by side.

**Rooms are a layer, not a tile state.** `GridManager` owns terrain and `RoomManager` owns
what has been built on top of it. A room only ever exists on a tile that is already `Dug`, and
dug tiles never revert, so the two layers never have to reconcile — no shared state, no
ordering rules between them.

**Gold is a place, not a number.** Mined gold sits on specific vault tiles with a per-tile
ceiling, and an imp has to walk it there. That is what makes the treasury a real decision
rather than a cosmetic room, and it is why `ResourceManager` is a facade over `RoomManager`
instead of holding an int of its own — there is only one copy of the number.

**A full vault must not look like a bug.** An imp that cannot bank its load drifts home and
keeps asking for eight seconds, then dumps the gold and goes back to work. Freezing the crew
until the player notices would be the more "correct" simulation and the worse game: a stalled
imp is indistinguishable from a broken one, while spilled gold is a cost you can see in the
HUD and fix.

**Land you dug is land you own.** The original game makes imps claim floor tile by tile before
you can build on it. Here every dug tile is immediately yours. That drops a whole AI state and
a second ownership overlay for something the prototype does not yet use — worth revisiting if
enemy keepers ever turn up, because contested ground is the only thing claiming is really for.

**Creatures are a cost, not a reward.** A portal creature does no work: it sleeps, wanders and
takes wages. That is deliberate — the dig loop already produces gold, and what the game was
missing was somewhere for that gold to have to go. Arrivals are gated on a dug route to the
portal and on a free lair, so every creature is something the dungeon earned and then has to
keep affording.

**Imps and creatures share the housing.** One lair list, claimed first come first served, so an
imp that moved in is one fewer creature the portal will send. Splitting them into two room
types would remove the decision; leaving them shared means the player has to choose between
faster digging and more tenants.

**Anger is slow on purpose.** Homelessness takes two minutes to drive a creature out, and
payroll takes three missed paydays. Both are long enough to notice the HUD warning and dig
your way out of trouble — a creature that vanished the moment the vault ran dry would read as
a bug rather than as a consequence.

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
`RoomManager`, `Pathfinder` and `ImpAI` through the whole loop:

- **Terrain and pathing** — generation determinism, marking rules, A* shortest paths and
  failure cases, and an unreachable mark not wedging the state machine.
- **Rooms** — the heart's 3×3 footprint and starting capital, every placement rule and its
  refusal message, building charging the player, treasuries draining before the heart reserve,
  per-tile deposit ceilings, and selling returning both the stored gold and half the cost.
- **The haul** — a corridor dug to a gold seam and the load carried back into a vault, with
  the banked total checked to the gold piece.
- **The portal** — nothing arrives through a portal still sealed in rock, nothing arrives with
  no free lair, and a creature walks in once a corridor and a lair both exist. It then moves
  into that lair unprompted.
- **Payroll** — a paid creature holds no grudge and the vault is lighter by its wage; three
  missed paydays send it back out through the portal, off the roster, and free its lair for
  whoever comes next.
- **A full vault** — digging continues, the load is dumped after the patience window, the
  spill is recorded, and banking resumes normally once a treasury exists.
- **Lairs** — imps moving in unprompted, never sharing a tile, being evicted when their lair
  is sold, and clearing the same run of rock faster than an imp with nowhere to sleep.
- **A crew** — claims never shared, every queued tile still dug, three imps beating one, and
  the crew's banked gold matching what it mined.

This covers logic only. Rendering, input, and the WebGL build still need the Editor.

## Out of scope for this prototype

Creatures other than imps, a portal to spawn them, food and other needs, combat, hero
incursions, save/load, and verticality. The grid is stored as a 3D array with a single Y
layer, so adding layers later does not mean rewriting the data model.
