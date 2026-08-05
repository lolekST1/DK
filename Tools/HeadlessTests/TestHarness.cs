// Headless smoke test of the dungeon loop: grid generation, marking, A*, the imp state
// machine, room building, the gold haul and lairs. Runs against the stub mini-engine,
// not Unity.
using System;
using System.Collections.Generic;
using System.Reflection;
using DK;
using UnityEngine;

public static class TestHarness
{
    static int _failures;

    public static int Main()
    {
        var world = NewWorld("Main", 1337);
        var grid = world.Grid;
        var economy = world.Economy;
        var imp = AddImp(world, "Imp", grid.BaseCell);

        // --- generation -----------------------------------------------------
        Check(grid.BaseCell.x == 10 && grid.BaseCell.y == 10, "base cell is the grid centre");
        Check(grid.IsWalkable(grid.BaseCell), "base cell starts dug out");
        Check(grid.IsWalkable(new Vector2Int(8, 8)), "starting chamber corner is dug out");
        Check(!grid.IsWalkable(new Vector2Int(7, 7)), "tile outside the chamber is still solid");
        Check(!grid.IsWalkable(new Vector2Int(-1, 0)), "out of bounds is never walkable");

        int gold = 0, rock = 0;
        for (int x = 0; x < 20; x++)
        for (int z = 0; z < 20; z++)
        {
            if (grid.GetTileState(x, z) == TileState.GoldSeam) gold++;
            if (grid.GetTileState(x, z) == TileState.Rock) rock++;
        }
        Check(gold > 5, $"seeded generation scattered gold seams (found {gold})");
        Check(rock > 200, $"most of the grid is rock (found {rock})");

        // Same seed must produce the same map.
        var second = new GameObject("Grid2").AddComponent<GridManager>();
        second.Configure(20, 20, 1337, 0.10f, 2);
        bool identical = true;
        for (int x = 0; x < 20 && identical; x++)
        for (int z = 0; z < 20 && identical; z++)
            identical = grid.GetTileState(x, z) == second.GetTileState(x, z);
        Check(identical, "generation is deterministic for a fixed seed");

        // --- pathfinding ----------------------------------------------------
        var path = new List<Vector2Int>();
        Check(Pathfinder.TryFindPath(grid, new Vector2Int(8, 8), new Vector2Int(12, 12), path),
            "A* crosses the starting chamber");
        Check(path.Count == 9, $"A* returns the manhattan-shortest route (got {path.Count} cells)");
        Check(!Pathfinder.TryFindPath(grid, grid.BaseCell, new Vector2Int(0, 0), path),
            "A* reports failure when the target is walled off");

        // --- marking --------------------------------------------------------
        Check(!grid.MarkForDigging(grid.BaseCell.x, grid.BaseCell.y), "already-dug tiles cannot be queued");
        Check(grid.MarkForDigging(7, 10), "a rock tile can be queued");
        Check(!grid.MarkForDigging(7, 10), "queueing the same tile twice is a no-op");
        Check(grid.QueuedCount == 1, "queue count tracks marked tiles");
        Check(grid.UnmarkForDigging(7, 10) && grid.QueuedCount == 0, "unmarking clears the queue");

        // --- rooms ----------------------------------------------------------
        RoomChecks();

        // --- the actual loop ------------------------------------------------
        // Vault space first, or the imp has nowhere to put what it mines.
        Check(world.Rooms.Build(8, 8, RoomType.Treasury), "treasury tile paid for and placed");
        Check(world.Rooms.Build(8, 9, RoomType.Treasury), "second treasury tile placed");
        int goldBeforeDigging = economy.Gold;

        // Dig a corridor west from the chamber, then down, ending on the nearest gold seam.
        var target = FindNearestGold(grid);
        var corridor = BuildCorridor(grid.BaseCell, target)
            .FindAll(cell => grid.IsDiggable(cell.x, cell.y));
        int goldTilesInCorridor = 0;
        foreach (var cell in corridor)
        {
            if (grid.GetTileState(cell.x, cell.y) == TileState.GoldSeam) goldTilesInCorridor++;
            grid.MarkForDigging(cell.x, cell.y);
        }
        Check(grid.QueuedCount == corridor.Count, $"queued the whole corridor ({corridor.Count} tiles)");
        Check(goldTilesInCorridor > 0, "the corridor ends on a gold seam");

        float simulated = RunUntil(imp, () => grid.QueuedCount == 0, 300f);
        Check(simulated > 0f, $"imp cleared the dig queue in {simulated:0.0}s of simulated time");

        foreach (var cell in corridor)
            Check(grid.GetTileState(cell.x, cell.y) == TileState.Dug, $"tile {cell.x},{cell.y} ended up dug out");

        int expectedGold = goldBeforeDigging + goldTilesInCorridor * GridManager.GoldPerSeam;
        float banked = RunUntil(imp, () => imp.CarriedGold == 0 && imp.State == ImpState.Idle, 120f);
        Check(banked > 0f, $"imp hauled its load into the vault after {banked:0.0}s");
        Check(economy.Gold == expectedGold,
            $"banked gold is {economy.Gold}, expected {expectedGold}");
        Check(economy.TotalSpilled == 0, "nothing spilled while there was vault space");

        // --- return to base -------------------------------------------------
        float returned = RunUntil(imp, () => imp.CurrentCell == grid.BaseCell && imp.State == ImpState.Idle, 120f);
        Check(returned > 0f, $"imp walked back to base and went idle after {returned:0.0}s");

        // --- unreachable work does not hang ---------------------------------
        grid.MarkForDigging(0, 0);
        Step(imp, 5f);
        Check(grid.QueuedCount == 1 && imp.State == ImpState.Idle,
            "an unreachable mark leaves the imp idle instead of stuck");

        // --- several imps share the queue ------------------------------------
        MultiImpChecks();

        // --- hauling and lairs -----------------------------------------------
        FullVaultChecks();
        LairChecks();

        // --- nobody ever stands inside rock -----------------------------------
        SolidGroundChecks();

        // --- portal, creatures and payroll ------------------------------------
        PortalChecks();

        Console.WriteLine(_failures == 0
            ? "\nAll checks passed."
            : $"\n{_failures} check(s) FAILED.");
        return _failures == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------ rooms

    static void RoomChecks()
    {
        var world = NewWorld("Rooms", 99);
        var rooms = world.Rooms;
        var economy = world.Economy;

        int heartTiles = 0;
        for (int x = 0; x < world.Grid.Width; x++)
        for (int z = 0; z < world.Grid.Depth; z++)
            if (rooms.GetRoom(x, z) == RoomType.DungeonHeart) heartTiles++;

        Check(heartTiles == 9, $"the dungeon heart covers a 3x3 (found {heartTiles} tiles)");
        Check(rooms.GetRoom(world.Grid.BaseCell) == RoomType.DungeonHeart, "the heart sits on the base cell");
        Check(rooms.StorageCapacity == 9 * RoomCatalog.CapacityOf(RoomType.DungeonHeart),
            $"heart capacity is {rooms.StorageCapacity}");
        Check(rooms.StoredGold == RoomManager.StartingGold,
            $"the dungeon starts with {rooms.StoredGold} gold banked");
        Check(economy.Gold == rooms.StoredGold, "the economy reads gold straight off the rooms");

        // Placement rules.
        Check(!rooms.CanBuild(0, 0, RoomType.Treasury, out var offMap), $"cannot build on solid rock ({offMap})");
        Check(!rooms.CanBuild(world.Grid.BaseCell.x, world.Grid.BaseCell.y, RoomType.Treasury, out var taken),
            $"cannot build on top of the heart ({taken})");
        Check(!rooms.CanBuild(8, 8, RoomType.DungeonHeart, out _), "the heart is not player-buildable");
        Check(rooms.CanBuild(8, 8, RoomType.Treasury, out _), "a bare chamber tile accepts a treasury");

        // Building charges, and capacity grows.
        int before = rooms.StoredGold;
        Check(rooms.Build(8, 8, RoomType.Treasury), "treasury built");
        Check(rooms.StoredGold == before - RoomCatalog.CostOf(RoomType.Treasury),
            $"building charged {RoomCatalog.CostOf(RoomType.Treasury)} gold");
        Check(rooms.StorageCapacity == 225 + RoomCatalog.CapacityOf(RoomType.Treasury),
            "the treasury tile added its capacity");
        Check(!rooms.CanBuild(8, 8, RoomType.Treasury, out _), "a tile already holding a room refuses another");

        // Broke.
        Check(!rooms.Build(8, 9, RoomType.Lair), "cannot afford a lair on 50 gold");
        Check(rooms.GetRoom(8, 9) == RoomType.None, "the refused lair left no tile behind");

        // Treasuries drain before the heart.
        var vault = new Vector2Int(8, 8);
        rooms.Deposit(vault, 200);
        int heartBefore = rooms.StoredGold - rooms.GetStoredGold(vault.x, vault.y);
        Check(rooms.TryWithdraw(150), "withdrawing 150 succeeds");
        Check(rooms.GetStoredGold(vault.x, vault.y) == 50, "the treasury paid first");
        Check(rooms.StoredGold - rooms.GetStoredGold(vault.x, vault.y) == heartBefore,
            "the heart reserve was left alone");
        Check(!rooms.TryWithdraw(100000), "an unaffordable withdrawal changes nothing");

        // Deposits respect the per-tile ceiling.
        var full = new GameObject("FillRooms").AddComponent<RoomManager>();
        var fullGrid = new GameObject("FillGrid").AddComponent<GridManager>();
        fullGrid.Configure(20, 20, 7, 0.10f, 2);
        full.Configure(fullGrid);
        Check(full.DepositAnywhere(100000) == full.StorageCapacity - RoomManager.StartingGold,
            "depositing past capacity banks only what fits");
        Check(full.FreeCapacity == 0, "the heart reports itself full");
        Check(full.Deposit(fullGrid.BaseCell, 50) == 0, "a full tile accepts nothing");

        // Selling refunds half and rescues the gold that was stored on the tile.
        int stored = rooms.GetStoredGold(vault.x, vault.y);
        int totalBefore = rooms.StoredGold;
        Check(rooms.Sell(vault.x, vault.y, out int lost), "a treasury tile can be sold");
        Check(rooms.GetRoom(vault.x, vault.y) == RoomType.None, "the sold tile is bare floor again");
        Check(rooms.StoredGold + lost == totalBefore + RoomCatalog.RefundOf(RoomType.Treasury),
            $"sale returned the stored {stored} plus half the build cost, minus {lost} that did not fit");
        Check(!rooms.Sell(world.Grid.BaseCell.x, world.Grid.BaseCell.y, out _), "the dungeon heart cannot be sold");
    }

    // ------------------------------------------------------------------ hauling

    /// <summary>An imp that cannot bank its load must not freeze the crew.</summary>
    static void FullVaultChecks()
    {
        var world = NewWorld("Full", 1337);
        var imp = AddImp(world, "FullImp", world.Grid.BaseCell);

        world.Rooms.DepositAnywhere(100000);
        Check(world.Rooms.FreeCapacity == 0, "vault filled to the brim for the test");

        var corridor = BuildCorridor(world.Grid.BaseCell, FindNearestGold(world.Grid))
            .FindAll(cell => world.Grid.IsDiggable(cell.x, cell.y));
        foreach (var cell in corridor) world.Grid.MarkForDigging(cell.x, cell.y);

        float cleared = RunUntil(imp, () => world.Grid.QueuedCount == 0, 400f);
        Check(cleared > 0f, $"a full vault does not stop the digging ({cleared:0.0}s)");

        float dumped = RunUntil(imp, () => imp.CarriedGold == 0, 60f);
        Check(dumped > 0f, $"the imp dumped the load it could not bank after {dumped:0.0}s");
        Check(world.Economy.TotalSpilled >= GridManager.GoldPerSeam,
            $"the spill was recorded ({world.Economy.TotalSpilled} gold)");

        // Give the imp somewhere to put gold and the next seam banks normally again.
        Check(world.Rooms.Build(8, 8, RoomType.Treasury), "treasury built out of the full heart");

        var seam = FindDiggableGoldNextToFloor(world.Grid);
        Check(seam.x >= 0, "the opened corridor exposed another gold seam to mine");

        int spilledBefore = world.Economy.TotalSpilled;
        int bankedBefore = world.Economy.Gold;
        world.Grid.MarkForDigging(seam.x, seam.y);

        float second = RunUntil(imp, () => imp.CarriedGold == 0 && world.Grid.QueuedCount == 0, 200f);
        Check(second > 0f, $"the second seam was mined and hauled in {second:0.0}s");
        Check(world.Economy.Gold == bankedBefore + GridManager.GoldPerSeam,
            $"it banked into the new treasury (gold {world.Economy.Gold}, was {bankedBefore})");
        Check(world.Economy.TotalSpilled == spilledBefore, "nothing spilled once there was room again");
    }

    /// <summary>A gold seam touching already-dug floor, so an imp can actually reach it.</summary>
    static Vector2Int FindDiggableGoldNextToFloor(GridManager grid)
    {
        var offsets = new[]
        {
            new Vector2Int(1, 0), new Vector2Int(-1, 0),
            new Vector2Int(0, 1), new Vector2Int(0, -1),
        };

        for (int x = 0; x < grid.Width; x++)
        for (int z = 0; z < grid.Depth; z++)
        {
            if (grid.GetTileState(x, z) != TileState.GoldSeam) continue;

            foreach (var offset in offsets)
                if (grid.IsWalkable(x + offset.x, z + offset.y)) return new Vector2Int(x, z);
        }

        return new Vector2Int(-1, -1);
    }

    // ------------------------------------------------------------------ lairs

    static void LairChecks()
    {
        var world = NewWorld("Lair", 2024);
        var rooms = world.Rooms;

        var impA = AddImp(world, "LairImpA", new Vector2Int(8, 8));
        var impB = AddImp(world, "LairImpB", new Vector2Int(8, 9));

        Check(!impA.IsRested, "an imp with no lair is not rested");
        Check(impA.HomeCell == new Vector2Int(8, 8), "with no lair the imp falls back to its given home");

        rooms.DepositAnywhere(100000);
        Check(rooms.Build(12, 8, RoomType.Lair), "first lair built");
        Check(rooms.Build(12, 9, RoomType.Lair), "second lair built");

        Step(impA, 1f);
        Step(impB, 1f);

        Check(impA.IsRested && impB.IsRested, "both imps moved into a lair");
        Check(impA.HomeCell != impB.HomeCell, "no two imps share a lair tile");
        Check(impA.EffectiveDigDuration < impA.DigDuration, "a rested imp digs faster");

        // Selling a lair evicts exactly the imp that lived there.
        var evicted = impA.HomeCell;
        Check(rooms.Sell(evicted.x, evicted.y, out _), "a lair tile can be sold");
        Check(!impA.IsRested, "the imp lost its lair when the tile was sold");
        Check(impB.IsRested, "the other imp kept its own lair");
        Check(impA.HomeCell == new Vector2Int(8, 8), "the evicted imp fell back to its old home");

        // Rested imps really are quicker over the same queue.
        float rested = TimeToClearRock(seed: 3131, withLair: true);
        float weary = TimeToClearRock(seed: 3131, withLair: false);
        Check(rested > 0f && weary > 0f, $"both crews finished (rested {rested:0.0}s, weary {weary:0.0}s)");
        Check(rested < weary, "the imp with a lair cleared the same rock sooner");
    }

    /// <summary>Clears an identical run of plain rock, with and without a lair to sleep in.</summary>
    static float TimeToClearRock(int seed, bool withLair)
    {
        var world = NewWorld(withLair ? "Rested" : "Weary", seed);
        var imp = AddImp(world, "SpeedImp", world.Grid.BaseCell);

        if (withLair)
        {
            world.Rooms.DepositAnywhere(100000);
            world.Rooms.Build(8, 8, RoomType.Lair);
        }

        // Plain rock only, so hauling never enters the measurement.
        int marked = 0;
        for (int z = 8; z <= 12 && marked < 5; z++)
        {
            if (world.Grid.GetTileState(7, z) != TileState.Rock) continue;
            if (!world.Grid.MarkForDigging(7, z)) continue;
            marked++;
        }

        if (marked == 0) return -1f;
        return RunUntil(imp, () => world.Grid.QueuedCount == 0, 400f);
    }

    // ------------------------------------------------------------------ multi-imp

    static void MultiImpChecks()
    {
        var single = BuildCrew(1);
        var crew = BuildCrew(3);

        Check(crew.Imps[0].HomeCell != crew.Imps[1].HomeCell &&
              crew.Imps[1].HomeCell != crew.Imps[2].HomeCell &&
              crew.Imps[0].HomeCell != crew.Imps[2].HomeCell,
            "each imp gets its own home tile");

        int expectedGold = 0;
        foreach (var cell in crew.Marked)
            if (crew.World.Grid.GetTileState(cell.x, cell.y) == TileState.GoldSeam) expectedGold++;
        expectedGold *= GridManager.GoldPerSeam;

        float crewTime = RunCrewUntilIdle(crew, out bool everShared);
        float singleTime = RunCrewUntilIdle(single, out _);

        Check(!everShared, "no two imps ever hold a claim on the same tile");
        Check(crewTime > 0f, $"three imps cleared {crew.Marked.Count} tiles in {crewTime:0.0}s");
        Check(singleTime > 0f, $"one imp cleared the same work in {singleTime:0.0}s");
        Check(crewTime < singleTime, "three imps beat one imp on the same queue");

        foreach (var cell in crew.Marked)
            Check(crew.World.Grid.GetTileState(cell.x, cell.y) == TileState.Dug,
                $"crew dug out tile {cell.x},{cell.y}");

        int startingGold = RoomManager.StartingGold - RoomCatalog.CostOf(RoomType.Treasury);
        StepCrew(crew, 60f);
        Check(crew.World.Economy.Gold == startingGold + expectedGold,
            $"crew banked {crew.World.Economy.Gold}, expected {startingGold + expectedGold}");
    }

    class Crew
    {
        public World World;
        public List<ImpAI> Imps = new List<ImpAI>();
        public List<Vector2Int> Marked = new List<Vector2Int>();
    }

    /// <summary>Identical world every time: same seed, same marks, only the head count differs.</summary>
    static Crew BuildCrew(int impCount)
    {
        var crew = new Crew { World = NewWorld("Crew" + impCount, 4242) };

        // Somewhere to bank what the crew mines, so the run measures digging and not spilling.
        crew.World.Rooms.Build(8, 8, RoomType.Treasury);

        // Two clusters on opposite sides of the chamber, every tile touching a walkable one.
        foreach (int x in new[] { 7, 13 })
        for (int z = 8; z <= 12; z++)
        {
            if (!crew.World.Grid.MarkForDigging(x, z)) continue;
            crew.Marked.Add(new Vector2Int(x, z));
        }

        var homes = new[]
        {
            crew.World.Grid.BaseCell,
            new Vector2Int(crew.World.Grid.BaseCell.x - 1, crew.World.Grid.BaseCell.y),
            new Vector2Int(crew.World.Grid.BaseCell.x + 1, crew.World.Grid.BaseCell.y),
        };

        for (int i = 0; i < impCount; i++)
            crew.Imps.Add(AddImp(crew.World, $"CrewImp{i}", homes[i % homes.Length]));

        return crew;
    }

    static float RunCrewUntilIdle(Crew crew, out bool everShared)
    {
        everShared = false;
        int steps = (int)(600f / Time.deltaTime);

        for (int i = 0; i < steps; i++)
        {
            foreach (var imp in crew.Imps) ImpUpdate.Invoke(imp, null);

            for (int a = 0; a < crew.Imps.Count; a++)
            for (int b = a + 1; b < crew.Imps.Count; b++)
            {
                if (!crew.Imps[a].HasDigTarget || !crew.Imps[b].HasDigTarget) continue;
                if (crew.Imps[a].DigTarget == crew.Imps[b].DigTarget) everShared = true;
            }

            if (crew.World.Grid.QueuedCount == 0) return (i + 1) * Time.deltaTime;
        }

        return -1f;
    }

    static void StepCrew(Crew crew, float seconds)
    {
        int steps = (int)(seconds / Time.deltaTime);
        for (int i = 0; i < steps; i++)
            foreach (var imp in crew.Imps) ImpUpdate.Invoke(imp, null);
    }

    // ------------------------------------------------------------------ world building

    class World
    {
        public GridManager Grid;
        public RoomManager Rooms;
        public ResourceManager Economy;
    }

    static World NewWorld(string name, int seed)
    {
        var world = new World();

        world.Grid = new GameObject(name + "Grid").AddComponent<GridManager>();
        world.Grid.Configure(20, 20, seed, 0.10f, 2);

        world.Rooms = new GameObject(name + "Rooms").AddComponent<RoomManager>();
        world.Rooms.Configure(world.Grid);

        world.Economy = new GameObject(name + "Economy").AddComponent<ResourceManager>();
        world.Economy.Configure(world.Rooms);

        return world;
    }

    static ImpAI AddImp(World world, string name, Vector2Int home)
    {
        var imp = new GameObject(name).AddComponent<ImpAI>();
        imp.MoveSpeed = 3f;
        imp.DigDuration = 1.2f;
        imp.Configure(world.Grid, world.Economy, world.Rooms,
            new GameObject(name + "Body").transform, new GameObject(name + "Nugget").transform, home);
        return imp;
    }

    // ------------------------------------------------------------ solid ground

    /// <summary>
    /// Walks a full crew through a long dig with the whole grid queued, checking every single
    /// frame that each imp is standing on a dug-out tile. An imp inside rock is the "walking
    /// through walls" bug, and it is only ever visible for a frame or two at a time.
    /// </summary>
    static void SolidGroundChecks()
    {
        var grid = new GameObject("SolidGrid").AddComponent<GridManager>();
        grid.Configure(20, 20, 4242, 0.10f, 2);

        var rooms = new GameObject("SolidRooms").AddComponent<RoomManager>();
        rooms.Configure(grid);

        var economy = new GameObject("SolidEconomy").AddComponent<ResourceManager>();
        economy.Configure(rooms);

        var imps = new List<ImpAI>();
        for (int i = 0; i < 4; i++)
        {
            var impObject = new GameObject($"SolidImp_{i}");
            var imp = impObject.AddComponent<ImpAI>();
            imp.MoveSpeed = 3f;
            imp.DigDuration = 0.4f;
            imp.Configure(grid, economy, rooms, new GameObject("Body").transform,
                          new GameObject("Nugget").transform, grid.BaseCell);
            imps.Add(imp);
        }

        // Queue the whole map, so the crew is constantly re-targeting and crossing paths.
        for (int x = 0; x < grid.Width; x++)
        for (int z = 0; z < grid.Depth; z++)
            grid.MarkForDigging(x, z);

        float clearedAt = -1f;
        int offGrid = 0;
        var firstOffence = new Vector2Int(-1, -1);
        int frames = (int)(400f / Time.deltaTime);

        for (int frame = 0; frame < frames; frame++)
        {
            for (int i = 0; i < imps.Count; i++)
            {
                ImpUpdate.Invoke(imps[i], null);

                var cell = imps[i].CurrentCell;
                if (grid.IsWalkable(cell)) continue;

                if (offGrid == 0) firstOffence = cell;
                offGrid++;
            }

            if (grid.QueuedCount == 0) { clearedAt = frame * Time.deltaTime; break; }
        }

        Check(offGrid == 0,
            offGrid == 0
                ? "imps never stand inside rock while clearing the whole map"
                : $"imps stood inside rock on {offGrid} frame(s), first at {firstOffence.x},{firstOffence.y}");

        // The crew must also finish the job. An imp that quietly stops digging leaves tiles
        // marked forever, which is what a stalled state machine looks like from the outside.
        Check(grid.QueuedCount == 0,
            grid.QueuedCount == 0
                ? "the crew dug out every marked tile"
                : $"the crew stopped digging with {grid.QueuedCount} tile(s) still marked");

        // A throughput guard, not a stopwatch. The crew clears this map in ~200s; it took
        // ~295s when an imp holding gold sat out a full eight seconds per seam against a
        // vault that was already full, which is what "the imps stopped digging" looked like.
        Check(clearedAt > 0f && clearedAt < 250f, $"the crew kept working through a full vault ({clearedAt:0.0}s)");
        Check(economy.TotalSpilled > 0, "a vault this small does get gold spilled on the floor");

        int stillClaimed = 0;
        for (int x = 0; x < grid.Width; x++)
        for (int z = 0; z < grid.Depth; z++)
            if (grid.IsClaimedByOther(new Vector2Int(x, z), null)) stillClaimed++;

        Check(stillClaimed == 0, $"no dig claim outlived the tile it was on ({stillClaimed} left)");
    }

    // ------------------------------------------------------------------ portal

    static readonly MethodInfo CreatureUpdate =
        typeof(CreatureAI).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);

    static readonly MethodInfo ManagerUpdate =
        typeof(CreatureManager).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);

    static void PortalChecks()
    {
        var grid = new GameObject("PortalGrid").AddComponent<GridManager>();
        grid.Configure(20, 20, 99, 0.10f, 2);

        var rooms = new GameObject("PortalRooms").AddComponent<RoomManager>();
        rooms.Configure(grid);

        var economy = new GameObject("PortalEconomy").AddComponent<ResourceManager>();
        economy.Configure(rooms);

        var portalCell = new Vector2Int(15, 15);
        grid.CarveChamber(portalCell, 1);
        rooms.BuildPortal(portalCell, 1);

        Check(rooms.HasPortal, "the map starts with a portal");
        Check(rooms.PortalCell == portalCell, "the portal knows its own centre");
        Check(rooms.GetRoom(15, 16) == RoomType.Portal, "the portal covers its whole cavern");
        Check(!rooms.CanSell(15, 15), "the portal cannot be sold off by accident");

        var manager = new GameObject("PortalManager").AddComponent<CreatureManager>();
        manager.SpawnInterval = 1f;
        manager.PaydayInterval = 1000f;
        manager.Configure(grid, rooms, economy);

        // Sealed in rock: nothing arrives however long you wait.
        StepManager(manager, 5f);
        Check(!manager.PortalConnected, "a portal walled off from the heart counts as unreachable");
        Check(manager.ArrivalBlocker != null, $"arrivals are blocked: {manager.ArrivalBlocker}");
        Check(manager.CreatureCount == 0, "no creature arrives through a sealed portal");

        // Dig a corridor out to it.
        for (int x = 13; x <= 15; x++) grid.CarveChamber(new Vector2Int(x, 10), 0);
        for (int z = 11; z <= 13; z++) grid.CarveChamber(new Vector2Int(15, z), 0);

        StepManager(manager, 0.2f);
        Check(manager.PortalConnected, "digging through to the portal connects it");
        Check(manager.ArrivalBlocker != null && manager.ArrivalBlocker.Contains("lair"),
            "a connected portal still waits for somewhere to sleep");
        Check(manager.CreatureCount == 0, "no creature arrives with no lair to move into");

        // Somewhere to sleep. The heart's opening capital is exactly one lair tile.
        Check(rooms.Build(12, 10, RoomType.Lair), "a lair goes up for the starting 100 gold");
        StepManager(manager, 0.2f);
        Check(manager.ArrivalBlocker == null, "a connected portal with a free lair lets creatures in");

        StepManager(manager, 1.2f);
        Check(manager.CreatureCount == 1, $"a creature walked in ({manager.CreatureCount})");

        var beetle = manager.Creatures[0];
        Check(beetle.CurrentCell == portalCell, "the creature arrives standing on the portal");

        StepCreature(beetle, 1f);
        Check(beetle.HasLair, "the creature moves into the free lair unprompted");
        Check(rooms.FreeLairCount == 0, "an occupied lair is no longer free");

        StepManager(manager, 0.2f);
        Check(manager.ArrivalBlocker != null && manager.ArrivalBlocker.Contains("lair"),
            "the next creature waits for a second lair");

        // --- payday ---------------------------------------------------------
        rooms.DepositAnywhere(60);
        int before = economy.Gold;
        manager.RunPayday();

        Check(economy.Gold == before - beetle.Wage,
            $"payday took {beetle.Wage} gold out of the vault ({before} -> {economy.Gold})");
        Check(beetle.MissedPaydays == 0, "a paid creature has no grudge");
        Check(manager.TotalWagesPaid == beetle.Wage, "wages paid are tallied");

        // Drain the vault and miss three in a row.
        while (economy.TrySpend(1)) { }
        Check(economy.Gold == 0, "the vault is empty for the unpaid case");

        for (int i = 0; i < 3; i++) manager.RunPayday();

        Check(beetle.MissedPaydays == 3, "three paydays missed in a row");
        Check(manager.MissedPayments == 3, "the dungeon counts its failures to pay");
        Check(beetle.Anger >= 1f, $"three missed paydays is the end of its patience ({beetle.Anger:0.00})");

        StepCreature(beetle, 0.1f);
        Check(beetle.State == CreatureState.Leaving, "an unpaid creature heads for the portal");

        float walked = RunCreatureUntil(beetle, () => beetle.HasLeft, 120f);
        Check(walked > 0f, $"it walked back out through the portal after {walked:0.0}s");

        StepManager(manager, 0.1f);
        Check(manager.CreatureCount == 0, "a departed creature leaves the roster");
        Check(manager.Departed == 1, "the departure is counted");
        Check(rooms.FreeLairCount == 1, "its lair is free again for the next arrival");
    }

    static void StepManager(CreatureManager manager, float seconds)
    {
        int steps = (int)(seconds / Time.deltaTime);
        for (int i = 0; i < steps; i++) ManagerUpdate.Invoke(manager, null);
    }

    static void StepCreature(CreatureAI creature, float seconds)
    {
        int steps = (int)(seconds / Time.deltaTime);
        for (int i = 0; i < steps; i++) CreatureUpdate.Invoke(creature, null);
    }

    static float RunCreatureUntil(CreatureAI creature, Func<bool> done, float timeoutSeconds)
    {
        int steps = (int)(timeoutSeconds / Time.deltaTime);
        for (int i = 0; i < steps; i++)
        {
            CreatureUpdate.Invoke(creature, null);
            if (done()) return (i + 1) * Time.deltaTime;
        }
        return -1f;
    }

    // ------------------------------------------------------------------ helpers

    static readonly MethodInfo ImpUpdate =
        typeof(ImpAI).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);

    static void Step(ImpAI imp, float seconds)
    {
        int steps = (int)(seconds / Time.deltaTime);
        for (int i = 0; i < steps; i++) ImpUpdate.Invoke(imp, null);
    }

    static float RunUntil(ImpAI imp, Func<bool> done, float timeoutSeconds)
    {
        int steps = (int)(timeoutSeconds / Time.deltaTime);
        for (int i = 0; i < steps; i++)
        {
            ImpUpdate.Invoke(imp, null);
            if (done()) return (i + 1) * Time.deltaTime;
        }
        return -1f;
    }

    static Vector2Int FindNearestGold(GridManager grid)
    {
        var best = new Vector2Int(-1, -1);
        int bestDistance = int.MaxValue;

        for (int x = 0; x < grid.Width; x++)
        for (int z = 0; z < grid.Depth; z++)
        {
            if (grid.GetTileState(x, z) != TileState.GoldSeam) continue;

            int distance = Math.Abs(x - grid.BaseCell.x) + Math.Abs(z - grid.BaseCell.y);
            if (distance >= bestDistance) continue;

            bestDistance = distance;
            best = new Vector2Int(x, z);
        }

        return best;
    }

    /// <summary>L-shaped run of solid tiles from the base to the target, base excluded.</summary>
    static List<Vector2Int> BuildCorridor(Vector2Int from, Vector2Int to)
    {
        var cells = new List<Vector2Int>();
        int x = from.x, z = from.y;

        while (x != to.x)
        {
            x += Math.Sign(to.x - x);
            cells.Add(new Vector2Int(x, z));
        }
        while (z != to.y)
        {
            z += Math.Sign(to.y - z);
            cells.Add(new Vector2Int(x, z));
        }

        return cells;
    }

    static void Check(bool condition, string description)
    {
        if (!condition) _failures++;
        Console.WriteLine($"{(condition ? "  ok" : "FAIL")}  {description}");
    }
}
