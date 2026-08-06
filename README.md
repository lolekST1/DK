# Dungeon Keeper–style Prototype

A vertical slice of the core Dungeon Keeper loop: mark rock for digging, imps path to it and
dig it out, then haul what they mine back to a vault you had to build and pay for. A dungeon
heart sits at the centre, lairs give your creatures somewhere to sleep and mend, and a portal sealed in the
rock sends creatures once you dig your way to it — creatures that expect wages every payday and
earn them by fighting off the heroes who come for your vault. Hold out through the raids and
the Lord of the Land comes for the heart itself: kill him and the dungeon is yours, lose the
heart and the run is over.

## Running it

1. Open the folder in Unity Hub with **Unity 6.5 (6000.5.x)**. First open resolves
   packages and imports assets, which takes a couple of minutes.
2. Open `Assets/Scenes/Bootstrap.unity`.
3. Press Play.

That is the whole setup — there is nothing to drag into an Inspector. The scene holds a
single `GameBootstrap` GameObject; the grid, camera, lighting, imp, input handler and HUD
are all created and wired at runtime in `GameBootstrap.Awake()`.

### Controls

Every tool has a button in the top-left tool bar — click it, or use the key. Build buttons dim
when the vault cannot cover the next tile.

| Input | Action |
| --- | --- |
| Click a tool button | Select that tool |
| `1` / `Esc` | Dig tool (default) |
| `2` | Build treasury — 50 gold a tile, holds 250 |
| `3` | Build lair — 100 gold a tile, houses one creature |
| `4` | Sell tool — tears a room out for half its cost |
| Left mouse (hold to paint) | Apply the selected tool |
| Right mouse (hold to paint) | Undo it: unmark while digging, sell while building |
| WASD, arrow keys, or screen edge | Pan the camera |
| Q / E | Rotate the camera 90° |
| Scroll wheel | Zoom |

The cursor turns green where the selected room will go and red where it will not; the HUD
status line says why. It shows one thing at a time, the most urgent first: payroll it cannot
meet, then a full vault, then gold waiting on the floor, then what the portal is waiting for.

### The loop

You start with a 3×3 dungeon heart holding 100 gold and 225 of vault space. Mark rock, the
imps dig it. A gold seam does **not** credit a counter: the gold falls where the seam was and
lies there. Imps keep digging as long as there is anything left in the queue, and only turn
porter once they have no rock left to break — with a crew, whoever runs out of digging first
starts collecting while the others carry on. Gold only counts once it is walked into a vault,
so the heart fills up fast and you have to spend your opening 100 gold on a treasury. Piles you
have nowhere to store just stay on the floor until you build one. Build a lair and the portal can
send another creature to fill it; imps do not sleep and never take one.

The portal sits in a sealed cavern three quarters of the way across the map. Dig a route to it
and creatures start arriving — but only while a lair is standing empty, so housing is a real
budget, and a lair is also the only place a wound heals. Every creature then bills the vault on
payday. A dungeon that cannot pay watches its creatures redden, and after three missed paydays they walk
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
  LooseGold.cs        Gold dropped on the floor: piles, per-pile claims, pickup
  RoomType.cs         None / DungeonHeart / Treasury / Lair / Portal / HeroGate
  DungeonHeart.cs     The thing there is to lose: health, damage tint, destruction
  GameDirector.cs     Watches for the two endings and stops the world
  Battlefield.cs      Combatant roster and "who is near enough to hit"
  HeroManager.cs      Hero gate: raid clock, roster, loot stolen and recovered
  HeroAI.cs           Advancing → Fighting → Escaping, and robbing a vault tile
  HeroCatalog.cs      Health, damage, loot and intent per hero kind — knight and Lord
  CreatureManager.cs  Portal roster: arrivals, payday, departures
  CreatureAI.cs       Idle → GoingToLair → Sleeping / Wandering / Fighting / Leaving
  CreatureCatalog.cs  Wage, fatigue, patience and healing per creature — the creature balance
  Pathfinder.cs       4-directional A* over the tile array
  GridWalker.cs       Path following and turning, shared by everything that walks
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

### Raids

A hero gate sits sealed in the opposite corner from the portal. Dig through to it and raids
start after a grace period: a knight walks in, makes for the nearest vault tile with gold on
it, takes what it can carry and heads back to the gate.

Creatures answer a raid from anywhere in the dungeon, breaking off whatever they were doing and
waking up for it if they were asleep. One beetle loses to a knight but leaves it under half
health, so the second one there finishes it. Kill a knight before it reaches the gate and the
loot falls on the floor for the imps to carry home; let it out and the gold is gone.

Survive five of those and the sixth wave is the **Lord of the Land**, who walks past the vault
and swings at the dungeon heart instead. Kill him and you have won; let the heart fall and the
run ends there. He is balanced against the whole garrison, because that is who turns up: what
a crowd of *N* can land on him before he kills them all goes up with *N* squared, so the fight
is decided by how many creatures the dungeon housed and paid for. Four or five lose the heart.
Six or seven hold it and bury two to four of their own. A full roster of ten walks away without
a loss. The HUD counts his health down while he is on the map.

Wounds only close in a lair. A creature that comes out of a fight badly hurt goes to bed
unprompted and sleeps itself whole, which is what the quiet between raids is for — and a
dungeon with no lairs keeps every scratch it takes until the Lord arrives to collect.

## Design notes

**Everything procedural.** No prefabs, no Inspector references, and nothing serialized on the
component either. Grid size, seed, gold density, wave timings and the rest are plain fields on
`GameBootstrap` marked `[NonSerialized]`, so the code is the only copy. They used to be
ordinary public fields, which Unity writes into the scene asset the first time it is saved —
after which the scene silently wins, and changing a default in code leaves a clone running the
old number with nothing on screen to explain it. Every other balance figure here already lived
in code; these now do too.

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

**Digging beats carrying.** Mined gold drops where the seam was and stays there; collecting it
is what an imp does when it has nothing left to dig. An imp that broke off mid-queue to run each
nugget to the vault spent most of its time walking, and clearing rock is what the player
actually asked for. It also gives a crew a natural division of labour for free: the imp that
runs out of reachable dig work becomes the porter while the rest keep breaking rock. Piles are
claimed one imp at a time, the same way dig targets are, so one spill does not drag the whole
crew across the map.

**Dropped is not destroyed.** Gold you have nowhere to store is not deleted, it is left on the
floor where you can see it, and it goes into the vault the moment you build one. Deleting it
would have made a full vault a silent, permanent tax on a mistake the player can already see
and fix.

**A full vault must not look like a bug.** An imp that cannot bank its load drifts home and
keeps asking, then dumps the gold and goes back to work. Freezing the crew until the player
notices would be the more "correct" simulation and the worse game: a stalled imp is
indistinguishable from a broken one, while spilled gold is a cost you can see in the HUD and
fix. How long it asks depends on why it failed — eight seconds when the vault has space it
could not reach, since that can change, but only a second and a half when every tile is
genuinely full, because nothing will change by standing still. Half a crew parked holding gold
is the exact thing this note is trying to avoid.

**Land you dug is land you own.** The original game makes imps claim floor tile by tile before
you can build on it. Here every dug tile is immediately yours. That drops a whole AI state and
a second ownership overlay for something the prototype does not yet use — worth revisiting if
enemy keepers ever turn up, because contested ground is the only thing claiming is really for.

**Creatures are a cost, not a reward.** A portal creature does no work: it sleeps, wanders and
takes wages. That is deliberate — the dig loop already produces gold, and what the game was
missing was somewhere for that gold to have to go. Arrivals are gated on a dug route to the
portal and on a free lair, so every creature is something the dungeon earned and then has to
keep affording.

**Lairs are for creatures only.** They used to be shared with the imps, on the theory that
housing should be a budget spent between faster digging and more tenants. In practice a crew of
six silently ate the first six lairs a player built, the portal reported "no free lair" with
nine of them standing there, and nothing on screen connected the two. A lair now means one more
creature, which is what building one looks like it should do.

**Being outnumbered has to be bad for whoever is in the middle.** Taking a hit re-engages
whoever landed it, so that a creature jumped from behind turns round. That used to restart the
attacker's swing clock too, which handed the one in the middle a free swing for every blow
taken: a Lord with five creatures on him swung six times his catalogued rate, and the more
defenders a keeper housed, the faster he killed them. Switching targets inside a fight now
carries the clock over; only stepping into a fight restarts it. Dead creatures also kept
swinging until the manager cleared the body a frame later — long enough not to matter in play,
but every last-stand figure in the test harness had been measured against defenders that fought
on after they died, which is why the shipped Lord read as unbeatable and tested as trivial.

**Wounds close in a lair, and nowhere else.** Nothing healed before, so damage was permanent
and a garrison was ground down over the five knight waves with no way back — the last stand was
fought by whatever was left rather than by what the player had paid for. A creature below half
health now goes to bed unprompted and sleeps until it is whole. It is the lair's second job,
and it gives the gap between raids something to be for.

**Anger is slow on purpose.** Homelessness takes two minutes to drive a creature out, and
payroll takes three missed paydays. Both are long enough to notice the HUD warning and dig
your way out of trouble — a creature that vanished the moment the vault ran dry would read as
a bug rather than as a consequence.

**The heart is a structure, not a fighter.** It sits on the battlefield like anything else, but
it never moves and never swings back, and enemy searches skip structures unless asked. That is
what keeps a knight from stopping to hack at the masonry while the vault stands open, and it
means the Lord is a different problem rather than a knight with more health — one line of
intent in the catalog, no special case in the AI.

**Digging is what invites the raids in.** The hero gate has to be dug through to before
anything comes out of it, exactly like the portal. That makes expanding a decision rather than
a chore: the map is not a safe box you empty at your own pace, and the two things worth digging
towards pull in opposite directions.

**Every creature answers every raid.** There is no way for the player to order a creature into
battle, so a defender that ignores a hero on the far side of the dungeon simply does not
defend. The alert range is wider than any prototype map, and looking for a fight happens ahead
of the pacing that keeps idle creatures from re-planning every frame — a second of "I will get
to it" reads as creatures standing about while the vault is robbed. The per-kind range stays in
the catalog for the day a lazier creature wants one.

**Heroes steal instead of destroying.** A raid that reached the heart could have ended the run,
which for a prototype means writing a game-over screen and a restart before the combat itself
is worth playing. Loot is a loss you read straight off the gold counter, it is recoverable if
you kill the thief in time, and it needs no new UI at all.

**The run ends by stopping the clock.** `GameDirector` sets the timescale to zero and puts the
result in the status line. Disabling every AI in the scene would have been the tidier-sounding
option and would silently miss whatever gets added next; a frozen dungeon behind the verdict is
also the right picture of what just happened. There is no restart yet — press Play again.

**Neither side knows the other exists.** `CreatureAI` and `HeroAI` never reference each other —
they ask `Battlefield` for the nearest enemy and get an `ICombatant` back. One place decides
who can see and reach whom, and adding a third faction would not touch either class. Imps are
deliberately not on the roster: a workforce that could be killed needs a replacement policy the
prototype does not have.

**No NavMesh.** Tiles appear and vanish at runtime, which NavMesh handles poorly. A
hand-rolled A* over the array we already own is simpler, faster and predictable. It reuses
its scratch buffers, so pathfinding does not allocate per search.

**The tool bar is buttons, not a legend.** It used to be a line of text naming the hotkeys,
which meant that building — the half of the game the dig loop pays for — was invisible to
anyone who had not read this file. The buttons carry their own cost and dim when you cannot
afford them, so the economy is legible without a manual. `PlayerTools` asks the HUD whether the
pointer is over one before it picks or paints, so a click on a button never also paints the
tile behind it, and every HUD text line is `raycastTarget = false` so the full-width labels do
not swallow clicks aimed at the map.

**The build has to be told what the Editor assumes.** A URP asset is created with
multisampling off and fifty metres of shadow distance. The Game view hides both — it is usually
looking at part of the dungeon from close up — and a build does not: block edges crawl, and
everything past the shadow distance lights differently from everything inside it. Both are set
explicitly, and across *every* quality level rather than the active one, because
`QualitySettings` writes to whichever level is current and a player build uses the level
configured for its platform, not the one the Editor happens to be sitting on. The camera's
framing scales with the grid too, and starts far enough out to hold all of it.

**No colliders.** Mouse picking is two maths-plane raycasts — one at the top of the blocks,
one at floor level — so there is not a single collider in the scene.

**No cast shadows.** Every block top shares one normal, so they must all light identically —
and in a build they did not: the far half of the map came out dark with a stepped edge across
the middle. That is a shadow map failing, not lighting. Shadow map size, cascade split and
distance all come from the quality level, which differs between the Game view and a player
build, which is why it looked like a WebGL-only fault. The blocks are flat-topped and read by
colour and by the shading difference between top and side faces, so cast shadows bought very
little and cost a whole class of platform-dependent breakage.

`GameBootstrap` logs one line at startup with the quality level, MSAA, shadow distance,
pipeline and canvas size. In a build that goes to the browser console, which is the only way to
tell a setting that never applied from one that applied and did not help.

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

Every run is meant to be identical: the simulation takes its randomness from the map seed and
from spawn order, never from object hash codes, so a timing assertion is a measurement rather
than a coin toss.

It type-checks every script against stub Unity types, then runs the real `GridManager`,
`RoomManager`, `Pathfinder` and `ImpAI` through the whole loop:

- **Terrain and pathing** — generation determinism, marking rules, A* shortest paths and
  failure cases, and an unreachable mark not wedging the state machine.
- **Rooms** — the heart's 3×3 footprint and starting capital, every placement rule and its
  refusal message, building charging the player, treasuries draining before the heart reserve,
  per-tile deposit ceilings, and selling returning both the stored gold and half the cost.
- **The haul** — a corridor dug to a gold seam and the load carried back into a vault, with
  the banked total checked to the gold piece.
- **Spilled gold** — a mined seam leaves exactly one pile of the right size on the tile it came
  out of, nobody fetches it while there is nowhere to put it, and once the vault has room an imp
  carries it in. Claims are checked directly: two imps cannot hold the same pile, releasing
  hands it over, and an empty tile cannot be claimed at all.
- **Payroll balance** — the one balance figure that can be checked rather than eyeballed. A
  crew of six digs for two simulated minutes, and what they bank has to beat the wage bill for
  a full house over the same stretch, with half again on top so a raid does not tip it over.
  It fails at the old numbers.
- **The last stand** — the fight the whole economy pays for, measured rather than reasoned
  about. Four to nine defenders, bunched at the heart or ringed twelve tiles out, with and
  without three knights still inside, plus what a single beetle is worth fed to him alone. Four
  have to lose the heart and seven have to hold it, which is the threshold the Lord's health is
  set to.
- **Swarms** — five creatures on one hero, arriving out of step so each is a separate attacker.
  His swing rate has to stay inside his catalogued interval however many are hitting him, and
  the crowd has to be winning the exchange.
- **Mending** — a creature down to its last few points goes to its lair without waiting to get
  tired, sleeps itself whole, and gets up again; one with nowhere to sleep stays hurt.
- **The endings** — a knight robs the place and leaves the heart untouched, while the Lord
  walks past the vault, takes nothing, and brings the heart down in about forty seconds, which
  loses the run. Kill him instead and it is won. The structure rule is checked directly: a
  knight looking for a fight does not find a building, but whoever asks for one does.
- **Defending** — a creature answers a raid from right across the map, and one already walking
  to its lair turns round for it.
- **Duels** — one creature against one hero, which is what a raid really looks like. Checks the
  swing cadence as well as the outcome: three seconds of fighting is three swings each, a
  one-on-one takes seconds rather than frames, and two beetles arriving one after the other
  kill a knight.
- **Raids** — nothing comes through a gate still sealed in rock, digging through opens it, and
  a hero walks in on the clock. Then the three ways a raid can end: robbed and killed while
  carrying, so the loot lands on the floor and is counted as recovered; met by four defenders
  and put down; or unopposed, out through the gate, with the vault lighter by exactly what it
  carried.
- **Digging first** — with a whole map queued and vault space to spare, no imp spends a single
  frame carrying gold while there is still rock queued to break. Once the queue is empty the
  same crew collects the floor, and stops only when the vault is full rather than giving up.
- **Solid ground** — a crew of four clears a fully marked map while every imp is checked, on
  every simulated frame, to be standing on dug-out floor. Paths are only ever walked from the
  cell the imp is standing in, and this is what proves it. It also times the run against a
  full vault, so an imp that sits out its patience instead of digging shows up as a regression.
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
