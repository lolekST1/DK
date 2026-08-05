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

        // --- gold dropped on the floor is not gold lost ------------------------
        SpilledGoldChecks();
        DigBeforeHaulingChecks();

        // --- portal, creatures and payroll ------------------------------------
        PortalChecks();

        // --- heroes, raids and combat -----------------------------------------
        EndgameChecks();
        DefenceChecks();
        DuelChecks();
        CombatChecks();

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

        Check(imp.CarriedGold == 0, "the imp never broke off digging to carry gold");
        Check(world.Spillage.Total >= GridManager.GoldPerSeam,
            $"the mined seam is lying on the floor ({world.Spillage.Total} gold)");

        Step(imp, 10f);
        Check(world.Spillage.Total >= GridManager.GoldPerSeam,
            "nobody hauls gold about while there is nowhere to put it");

        // Give the imp somewhere to put gold and the floor gets tidied up.
        Check(world.Rooms.Build(8, 8, RoomType.Treasury), "treasury built out of the full heart");

        int floorBefore = world.Spillage.Total;
        int vaultBefore = world.Economy.Gold;

        float collected = RunUntil(imp, () => world.Spillage.Total == 0 && imp.CarriedGold == 0, 300f);
        Check(collected > 0f, $"the imp collected the floor once there was room ({collected:0.0}s)");
        Check(world.Economy.Gold == vaultBefore + floorBefore,
            $"every piece off the floor went into the vault (gold {world.Economy.Gold})");

        var seam = FindDiggableGoldNextToFloor(world.Grid);
        Check(seam.x >= 0, "the opened corridor exposed another gold seam to mine");

        int bankedBefore = world.Economy.Gold;
        world.Grid.MarkForDigging(seam.x, seam.y);

        float second = RunUntil(imp,
            () => imp.CarriedGold == 0 && world.Grid.QueuedCount == 0 && world.Spillage.Total == 0, 300f);
        Check(second > 0f, $"the second seam was mined, collected and banked in {second:0.0}s");
        Check(world.Economy.Gold == bankedBefore + GridManager.GoldPerSeam,
            $"it went into the new treasury (gold {world.Economy.Gold}, was {bankedBefore})");
        Check(world.Economy.TotalSpilled == 0,
            "no imp ever had to put a load back down, so nothing counts as spilled");
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
        public LooseGold Spillage;
    }

    static World NewWorld(string name, int seed)
    {
        var world = new World();

        world.Grid = new GameObject(name + "Grid").AddComponent<GridManager>();
        world.Grid.Configure(20, 20, seed, 0.10f, 2);

        world.Rooms = new GameObject(name + "Rooms").AddComponent<RoomManager>();
        world.Rooms.Configure(world.Grid);

        world.Spillage = new GameObject(name + "Loose").AddComponent<LooseGold>();
        world.Spillage.Configure(world.Grid);

        world.Economy = new GameObject(name + "Economy").AddComponent<ResourceManager>();
        world.Economy.Configure(world.Rooms, world.Spillage);

        return world;
    }

    static ImpAI AddImp(World world, string name, Vector2Int home)
    {
        var imp = new GameObject(name).AddComponent<ImpAI>();
        imp.MoveSpeed = 3f;
        imp.DigDuration = 1.2f;
        imp.Configure(world.Grid, world.Economy, world.Rooms, world.Spillage,
            new GameObject(name + "Body").transform, new GameObject(name + "Nugget").transform, home);
        return imp;
    }

    // ------------------------------------------------------- digging comes first

    /// <summary>
    /// The rule from the original game: imps clear rock, and gold lies where it fell until
    /// there is nothing left to dig. An imp that breaks off mid-queue to run a nugget to the
    /// vault is the thing this guards against — with plenty of vault space available, so the
    /// only reason not to carry gold is that digging outranks it.
    /// </summary>
    static void DigBeforeHaulingChecks()
    {
        var world = NewWorld("Order", 4242);

        world.Rooms.DepositAnywhere(1000);
        for (int x = 8; x <= 11; x++)
            Check(world.Rooms.Build(x, 8, RoomType.Treasury), $"treasury tile at {x},8");

        Check(world.Rooms.FreeCapacity > 500, $"plenty of vault to carry gold into ({world.Rooms.FreeCapacity})");

        var imps = new List<ImpAI>();
        for (int i = 0; i < 4; i++) imps.Add(AddImp(world, $"OrderImp_{i}", world.Grid.BaseCell));

        for (int x = 0; x < world.Grid.Width; x++)
        for (int z = 0; z < world.Grid.Depth; z++)
            world.Grid.MarkForDigging(x, z);

        int carryingFrames = 0;
        int frames = (int)(400f / Time.deltaTime);
        float clearedAt = -1f;

        for (int frame = 0; frame < frames; frame++)
        {
            foreach (var imp in imps) ImpUpdate.Invoke(imp, null);

            // Late in the run the last few tiles are all claimed by other imps, and an imp with
            // nothing left to claim is supposed to go fetching. Only judge the busy stretch.
            if (world.Grid.QueuedCount > 20)
                foreach (var imp in imps)
                    if (imp.State == ImpState.HaulGold || imp.State == ImpState.FetchGold) carryingFrames++;

            if (world.Grid.QueuedCount == 0) { clearedAt = frame * Time.deltaTime; break; }
        }

        Check(carryingFrames == 0,
            carryingFrames == 0
                ? "no imp carried gold while there was still rock queued to dig"
                : $"imps spent {carryingFrames} frame(s) hauling gold with the dig queue still full");

        Check(clearedAt > 0f, $"the crew cleared the map ({clearedAt:0.0}s)");
        Check(world.Spillage.Total > 0 || world.Economy.Gold > 1000,
            "the mined gold is either on the floor or already banked");

        // And once the digging is done, the crew turns porter -- until the vault is full,
        // which on this map it will be: four treasury tiles cannot hold a whole map of seams.
        int floorAfterDigging = world.Spillage.Total;

        float tidied = RunCrewUntil(imps,
            () => world.Spillage.Total == 0 || world.Rooms.FreeCapacity == 0, 600f);
        Check(tidied > 0f, $"with nothing left to dig, the crew went and collected it ({tidied:0.0}s)");
        Check(world.Spillage.Total < floorAfterDigging,
            $"the floor got cleared down ({floorAfterDigging} -> {world.Spillage.Total} gold)");
        Check(world.Rooms.FreeCapacity == 0,
            "collecting only stopped because the vault filled up, not because the imps gave up");
    }

    static float RunCrewUntil(List<ImpAI> imps, Func<bool> done, float timeoutSeconds)
    {
        int steps = (int)(timeoutSeconds / Time.deltaTime);
        for (int i = 0; i < steps; i++)
        {
            foreach (var imp in imps) ImpUpdate.Invoke(imp, null);
            if (done()) return (i + 1) * Time.deltaTime;
        }
        return -1f;
    }

    // ------------------------------------------------------------ spilled gold

    /// <summary>
    /// A full vault means an imp drops its load on the floor. That gold has to still be there
    /// afterwards, and somebody has to come back for it once there is room again.
    /// </summary>
    static void SpilledGoldChecks()
    {
        var world = NewWorld("Spill", 1337);

        // Fill the heart to the brim, so the first seam mined has nowhere to go.
        world.Rooms.DepositAnywhere(1000);
        Check(world.Rooms.FreeCapacity == 0, "the vault starts this test completely full");

        var imp = AddImp(world, "SpillImp", world.Grid.BaseCell);

        var target = FindNearestGold(world.Grid);
        foreach (var cell in BuildCorridor(world.Grid.BaseCell, target))
            world.Grid.MarkForDigging(cell.x, cell.y);

        float dropped = RunUntil(imp, () => world.Spillage.Total > 0, 300f);
        Check(dropped > 0f, $"mined gold lands on the floor ({dropped:0.0}s)");

        int onFloor = world.Spillage.Total;
        Check(onFloor == GridManager.GoldPerSeam, $"the whole seam is on the floor ({onFloor})");
        Check(world.Spillage.AmountAt(target) == GridManager.GoldPerSeam,
            "it lands on the tile the seam was in, not wherever the imp happened to stand");
        Check(world.Spillage.PileCount == 1, "it is one pile, not a trail of crumbs");
        Check(world.Economy.LooseGold == onFloor, "the economy reports gold lying on the floor");

        // An imp will not pick a pile up with nowhere to put it, or it would only drop it again.
        Step(imp, 5f);
        Check(world.Spillage.Total == onFloor, "nobody fetches gold while the vault is still full");

        // Spend some of the vault, and the floor gets tidied up.
        Check(world.Rooms.TryWithdraw(200), "spending gold makes room in the vault");
        int banked = world.Economy.Gold;

        float fetched = RunUntil(imp, () => world.Economy.Gold >= banked + onFloor, 300f);
        Check(fetched > 0f, $"an imp went back for the gold on the floor and banked it ({fetched:0.0}s)");
        Check(world.Spillage.Total == 0, "the floor is clear again");
        Check(world.Spillage.PileCount == 0, "and the pile is gone with it");

        // --- claims keep the crew off each other's piles ----------------------
        var first = new object();
        var second = new object();
        var cellA = world.Grid.BaseCell;

        world.Spillage.Drop(cellA, 10);
        Check(world.Spillage.TryClaim(cellA, first), "a pile can be claimed");
        Check(!world.Spillage.TryClaim(cellA, second), "a claimed pile is off limits to everyone else");
        Check(world.Spillage.TryClaim(cellA, first), "the owner can re-claim its own pile");

        world.Spillage.Release(cellA, first);
        Check(world.Spillage.TryClaim(cellA, second), "releasing a claim hands the pile over");
        Check(world.Spillage.Take(cellA) == 10, "picking a pile up returns exactly what was there");
        Check(!world.Spillage.TryClaim(cellA, first), "an empty tile cannot be claimed");
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

        var loose = new GameObject("SolidLoose").AddComponent<LooseGold>();
        loose.Configure(grid);

        var economy = new GameObject("SolidEconomy").AddComponent<ResourceManager>();
        economy.Configure(rooms, loose);

        var imps = new List<ImpAI>();
        for (int i = 0; i < 4; i++)
        {
            var impObject = new GameObject($"SolidImp_{i}");
            var imp = impObject.AddComponent<ImpAI>();
            imp.MoveSpeed = 3f;
            imp.DigDuration = 0.4f;
            imp.Configure(grid, economy, rooms, loose, new GameObject("Body").transform,
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
        Check(loose.Total > 0, "a vault this small leaves mined gold lying on the floor");

        int stillClaimed = 0;
        for (int x = 0; x < grid.Width; x++)
        for (int z = 0; z < grid.Depth; z++)
            if (grid.IsClaimedByOther(new Vector2Int(x, z), null)) stillClaimed++;

        Check(stillClaimed == 0, $"no dig claim outlived the tile it was on ({stillClaimed} left)");
    }

    // ----------------------------------------------------------------- endgame

    static readonly MethodInfo DirectorUpdate =
        typeof(GameDirector).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// The two ways a run can end. Also that an ordinary raid cannot end it either way: a
    /// knight is here for the gold and leaves the masonry alone.
    /// </summary>
    static void EndgameChecks()
    {
        // --- a knight ignores the heart ---------------------------------------
        var world = OpenGateWorld("Endgame", out var field, out var heart, out var heroes);
        heroes.WavesBeforeLord = 5;

        Check(!field.TryFindNearestEnemy(heart, int.MaxValue, out _),
            "the heart never goes looking for a fight");

        heroes.Raid();
        var knight = heroes.Heroes[0];
        Check(knight.Kind == HeroKind.Knight, "the early waves are knights");
        Check(heroes.WavesSent == 1 && !heroes.LordSent, "and the wave is counted");

        Check(!field.TryFindNearestEnemy((ICombatant)knight, int.MaxValue, out _),
            "a knight looking for a fight does not find a building");
        Check(field.TryFindNearestEnemy((ICombatant)knight, int.MaxValue, true, out var found) && found == heart,
            "though it is there for whoever asks for it");

        float raid = RunHeroUntil(knight, () => knight.HasEscaped, 400f);
        Check(raid > 0f, $"the knight robbed the place and left ({raid:0.0}s)");
        Check(heart.Health == heart.MaxHealth, "and never laid a finger on the heart");

        // --- the Lord comes for the heart itself -------------------------------
        var siege = OpenGateWorld("Siege", out var siegeField, out var siegeHeart, out var siegeHeroes);
        siegeHeroes.WavesBeforeLord = 0;

        var director = new GameObject("SiegeDirector").AddComponent<GameDirector>();
        director.Configure(siegeHeart, siegeHeroes);
        Check(director.Result == Outcome.Playing, "the run starts undecided");

        int goldBefore = siege.Economy.Gold;
        siegeHeroes.Raid();

        var lord = siegeHeroes.Heroes[0];
        Check(lord.Kind == HeroKind.Lord && siegeHeroes.LordSent, "the last wave is the Lord of the Land");

        float struck = RunHeroUntil(lord, () => siegeHeart.Health < siegeHeart.MaxHealth, 200f);
        Check(struck > 0f, $"he walked past the vault and started on the heart ({struck:0.0}s)");
        Check(siege.Economy.Gold == goldBefore, "taking nothing on the way");

        float fell = RunHeroUntil(lord, () => !siegeHeart.IsAlive, 400f);
        Check(fell > 0f, $"and brought it down ({fell:0.0}s)");
        Check(director.Result == Outcome.Lost, "which loses the run");
        Check(director.Finished, "and ends it");

        // --- or you kill him and the dungeon is yours --------------------------
        var won = OpenGateWorld("Won", out _, out var wonHeart, out var wonHeroes);
        wonHeroes.WavesBeforeLord = 0;

        var winDirector = new GameObject("WinDirector").AddComponent<GameDirector>();
        winDirector.Configure(wonHeart, wonHeroes);

        wonHeroes.Raid();
        var doomed = wonHeroes.Heroes[0];
        doomed.TakeDamage(10000, null);

        StepRaidClock(wonHeroes, 0.2f);
        Check(wonHeroes.LordDefeated, "the Lord can be killed");

        DirectorUpdate.Invoke(winDirector, null);
        Check(winDirector.Result == Outcome.Won, "which wins the run");
        Check(wonHeart.Health == wonHeart.MaxHealth, "with the heart untouched");
    }

    /// <summary>A world with a heart, a hero gate, and a corridor already dug between them.</summary>
    static World OpenGateWorld(string name, out Battlefield battlefield, out DungeonHeart heart,
                               out HeroManager heroes)
    {
        var world = NewWorld(name, 1337);
        battlefield = new GameObject(name + "Field").AddComponent<Battlefield>();

        heart = new GameObject(name + "Heart").AddComponent<DungeonHeart>();
        heart.Configure(battlefield, world.Grid.BaseCell, null, DungeonHeart.DefaultHealth);

        var gateCell = new Vector2Int(5, 5);
        world.Grid.CarveChamber(gateCell, 1);
        world.Rooms.BuildHeroGate(gateCell, 1);

        foreach (var cell in new[]
                 {
                     new Vector2Int(7, 8), new Vector2Int(6, 8), new Vector2Int(5, 8),
                     new Vector2Int(5, 7),
                 })
            world.Grid.CarveChamber(cell, 0);

        heroes = new GameObject(name + "Heroes").AddComponent<HeroManager>();
        heroes.Configure(world.Grid, world.Rooms, world.Economy, world.Spillage, battlefield, heart);

        return world;
    }

    // --------------------------------------------------------------- defending

    /// <summary>
    /// Creatures have to go to the hero by themselves, from wherever they are and whatever
    /// they were doing. Both halves of this were broken at once: a hero further off than the
    /// old alert range was invisible, and a creature on its way to bed never looked up.
    /// </summary>
    static void DefenceChecks()
    {
        var world = NewWorld("Defence", 1337);
        var battlefield = new GameObject("DefenceBattlefield").AddComponent<Battlefield>();

        // A long corridor west out of the starting chamber, so the two ends are far apart.
        for (int x = 0; x <= 7; x++) world.Grid.CarveChamber(new Vector2Int(x, 10), 0);

        var farCorner = new Vector2Int(12, 12);
        var corridorEnd = new Vector2Int(0, 10);

        int distance = Mathf.Abs(farCorner.x - corridorEnd.x) + Mathf.Abs(farCorner.y - corridorEnd.y);
        Check(distance > 12, $"the two ends are {distance} tiles apart, further than a creature used to see");

        // --- a raid across the map is still a raid ----------------------------
        var beetle = NewBeetle(world, battlefield, battlefield, farCorner);
        var hero = NewHero(world, battlefield, corridorEnd);

        float noticed = RunDuelUntil(hero, beetle, () => beetle.State == CreatureState.Fighting, 10f);
        Check(noticed > 0f, $"the creature set off after a hero right across the dungeon ({noticed:0.0}s)");

        // Chasing re-lays the route several times a second. Each of those used to restart it
        // at the centre of the cell the creature was halfway out of, so it rocked back and
        // forth on the spot instead of closing in. Measure the ground it covers, not its state.
        float chaseSpeed = CreatureCatalog.Get(CreatureKind.Beetle).MoveSpeed;
        float gapBefore = FlatDistance(beetle.transform.position, hero.transform.position);

        StepCreature(beetle, 3f);

        float closed = gapBefore - FlatDistance(beetle.transform.position, hero.transform.position);
        Check(closed > chaseSpeed * 3f * 0.6f,
            $"the chase covers ground ({closed:0.0} of a possible {chaseSpeed * 3f:0.0} in 3s)");

        float reached = RunDuelUntil(hero, beetle, () => beetle.Health < CreatureCatalog.Get(CreatureKind.Beetle).Health
                                                      || hero.Health < HeroCatalog.Get(HeroKind.Knight).Health, 90f);
        Check(reached > 0f, $"and got close enough to actually trade blows ({reached:0.0}s)");

        // --- and one on its way to bed turns round ----------------------------
        var world2 = NewWorld("Bedtime", 1337);
        var field2 = new GameObject("BedtimeBattlefield").AddComponent<Battlefield>();

        for (int x = 0; x <= 7; x++) world2.Grid.CarveChamber(new Vector2Int(x, 10), 0);

        world2.Rooms.DepositAnywhere(1000);
        Check(world2.Rooms.Build(1, 10, RoomType.Lair), "a lair at the far end of the corridor");

        var sleeper = NewBeetle(world2, field2, field2, farCorner);

        float tired = RunCreatureUntil(sleeper, () => sleeper.State == CreatureState.GoingToLair, 300f);
        Check(tired > 0f, $"the creature got tired and set off for its lair ({tired:0.0}s)");
        Check(sleeper.HasLair, "having claimed it on the way");

        StepCreature(sleeper, 2f);
        var onTheWay = sleeper.CurrentCell;
        Check(onTheWay != farCorner, "it is genuinely walking, not still standing at the start");

        var ambusher = NewHero(world2, field2, farCorner);
        float turned = RunDuelUntil(ambusher, sleeper, () => sleeper.State == CreatureState.Fighting, 10f);
        Check(turned > 0f, $"a hero behind it makes it break off and turn round ({turned:0.0}s)");
    }

    // ------------------------------------------------------------------- duels

    /// <summary>
    /// One creature against one hero, which is what a raid actually looks like: defenders
    /// arrive from their lairs one at a time. Guards the swing cadence as well as the outcome
    /// — both sides once fed each other a free attack on every hit, so a fight that should
    /// take ten seconds was over in a tenth of one and read as creatures dying on contact.
    /// </summary>
    static void DuelChecks()
    {
        var world = NewWorld("Duel", 1337);
        var battlefield = new GameObject("DuelBattlefield").AddComponent<Battlefield>();

        var arena = world.Grid.BaseCell;
        var hero = NewHero(world, battlefield, arena);
        var beetle = NewBeetle(world, battlefield, battlefield, arena + new Vector2Int(0, 1));

        var heroStats = HeroCatalog.Get(HeroKind.Knight);
        var beetleStats = CreatureCatalog.Get(CreatureKind.Beetle);

        // Three seconds of a one-on-one is three swings each, give or take one.
        StepDuel(hero, beetle, 3f);

        int heroLost = heroStats.Health - hero.Health;
        int beetleLost = beetleStats.Health - beetle.Health;

        Check(heroLost >= beetleStats.Damage * 2 && heroLost <= beetleStats.Damage * 4,
            $"the beetle swung about three times in three seconds ({heroLost} damage dealt)");
        Check(beetleLost >= heroStats.Damage && beetleLost <= heroStats.Damage * 3,
            $"and so did the knight ({beetleLost} damage dealt)");
        Check(beetle.IsAlive && hero.IsAlive, "nobody is deleted on contact");

        // See it through: one beetle loses, but not for free.
        float duel = RunDuelUntil(hero, beetle, () => !beetle.IsAlive || !hero.IsAlive, 120f);
        Check(duel > 5f, $"a one-on-one takes seconds, not frames ({duel:0.0}s)");
        Check(!beetle.IsAlive && hero.IsAlive, "one beetle loses to a knight");
        Check(hero.Health < heroStats.Health / 2,
            $"but leaves it under half health ({hero.Health}/{heroStats.Health})");

        // The next one through the door finishes it. This is the claim the catalog makes.
        var second = NewBeetle(world, battlefield, battlefield, hero.CurrentCell + new Vector2Int(0, -1));
        float finish = RunDuelUntil(hero, second, () => !second.IsAlive || !hero.IsAlive, 120f);

        Check(finish > 0f, $"the second beetle got there ({finish:0.0}s)");
        Check(!hero.IsAlive, "two beetles arriving one after the other kill a knight");
        Check(second.IsAlive, "and the second one lives to tell it");
    }

    static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    static HeroAI NewHero(World world, Battlefield battlefield, Vector2Int cell)
    {
        var hero = new GameObject("DuelHero").AddComponent<HeroAI>();
        hero.Configure(world.Grid, world.Rooms, world.Economy, world.Spillage, battlefield, null,
                       HeroKind.Knight, new GameObject("DuelHeroBody").transform, null, cell);
        return hero;
    }

    static CreatureAI NewBeetle(World world, Battlefield battlefield, Battlefield unused, Vector2Int cell)
    {
        var beetle = new GameObject("DuelBeetle").AddComponent<CreatureAI>();
        beetle.Configure(world.Grid, world.Rooms, battlefield, CreatureKind.Beetle,
                         new GameObject("DuelBeetleBody").transform, null, cell);
        return beetle;
    }

    static void StepDuel(HeroAI hero, CreatureAI creature, float seconds)
    {
        int steps = (int)(seconds / Time.deltaTime);
        for (int i = 0; i < steps; i++)
        {
            HeroUpdate.Invoke(hero, null);
            CreatureUpdate.Invoke(creature, null);
        }
    }

    static float RunDuelUntil(HeroAI hero, CreatureAI creature, Func<bool> done, float timeoutSeconds)
    {
        int steps = (int)(timeoutSeconds / Time.deltaTime);
        for (int i = 0; i < steps; i++)
        {
            HeroUpdate.Invoke(hero, null);
            CreatureUpdate.Invoke(creature, null);
            if (done()) return (i + 1) * Time.deltaTime;
        }
        return -1f;
    }

    // ------------------------------------------------------------------ combat

    static readonly MethodInfo HeroUpdate =
        typeof(HeroAI).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);

    static readonly MethodInfo HeroManagerUpdate =
        typeof(HeroManager).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic);

    static void CombatChecks()
    {
        var world = NewWorld("Raid", 1337);
        var grid = world.Grid;
        var rooms = world.Rooms;

        var battlefield = new GameObject("RaidBattlefield").AddComponent<Battlefield>();

        // A one-tile portal beside the starting chamber, so creatures can be spawned on demand.
        var portalCell = new Vector2Int(13, 10);
        grid.CarveChamber(portalCell, 0);
        rooms.BuildPortal(portalCell, 0);

        // The hero gate, sealed in rock in the far corner.
        var gateCell = new Vector2Int(5, 5);
        grid.CarveChamber(gateCell, 1);
        rooms.BuildHeroGate(gateCell, 1);

        Check(rooms.HasHeroGate, "the map starts with a hero gate");
        Check(rooms.HeroGateCell == gateCell, "the gate knows where it is");
        Check(!rooms.CanSell(gateCell.x, gateCell.y), "the hero gate cannot be sold off");

        var creatures = new GameObject("RaidCreatures").AddComponent<CreatureManager>();
        creatures.Configure(grid, rooms, world.Economy, battlefield);

        var heroes = new GameObject("RaidHeroes").AddComponent<HeroManager>();
        heroes.FirstRaidDelay = 1f;
        heroes.RaidInterval = 1000f;
        heroes.Configure(grid, rooms, world.Economy, world.Spillage, battlefield, null);

        // --- sealed gate ------------------------------------------------------
        StepRaidClock(heroes, 5f);
        Check(!heroes.GateReachable, "a gate walled off from the heart is not reachable");
        Check(heroes.HeroCount == 0, "no raid comes through a sealed gate");
        Check(heroes.SecondsToRaid < 0f, "and no raid clock is running yet");

        // --- dig through to it ------------------------------------------------
        foreach (var cell in new[]
                 {
                     new Vector2Int(7, 8), new Vector2Int(6, 8), new Vector2Int(5, 8),
                     new Vector2Int(5, 7),
                 })
            grid.CarveChamber(cell, 0);

        StepRaidClock(heroes, 0.2f);
        Check(heroes.GateReachable, "digging through to the gate opens it");

        StepRaidClock(heroes, 1.5f);
        Check(heroes.HeroCount == 1, $"a raid came through ({heroes.HeroCount} hero)");

        var hero = heroes.Heroes[0];
        Check(hero.CurrentCell == gateCell, "the hero starts on the gate");
        Check(battlefield.CountOf(Side.Hero) == 1, "the battlefield knows about it");

        // --- an undefended dungeon gets robbed ---------------------------------
        int vaultBefore = world.Economy.Gold;
        float robbed = RunHeroUntil(hero, () => hero.CarriedGold > 0, 300f);
        Check(robbed > 0f, $"the hero walked in and helped itself to the vault ({robbed:0.0}s)");

        int loot = hero.CarriedGold;
        Check(world.Economy.Gold == vaultBefore - loot,
            $"the gold left the books the moment it was lifted ({vaultBefore} -> {world.Economy.Gold})");

        // --- killed while carrying, and the loot hits the floor ----------------
        hero.TakeDamage(10000, null);
        Check(!hero.IsAlive, "a hero can be killed");

        StepRaidClock(heroes, 0.2f);
        Check(heroes.Repelled == 1, "the kill is counted");
        Check(heroes.Escaped == 0 && heroes.GoldStolen == 0, "and nothing was carried out");
        Check(heroes.HeroCount == 0, "the hero is off the roster");
        Check(world.Spillage.Total == loot, $"its loot is on the dungeon floor ({world.Spillage.Total})");
        Check(heroes.GoldRecovered == loot, "and the recovery is counted");

        // --- a defended dungeon kills the next one -----------------------------
        for (int i = 0; i < 4; i++) creatures.Spawn();
        Check(battlefield.CountOf(Side.Dungeon) == 4, "four defenders on the battlefield");

        heroes.Raid();
        var defended = heroes.Heroes[0];

        float fought = RunBattleUntil(heroes, creatures, () => heroes.HeroCount == 0, 400f);
        Check(fought > 0f, $"the raid was met and put down ({fought:0.0}s)");
        Check(heroes.Repelled == 2, "the defenders got the credit");
        Check(heroes.Escaped == 0, "nothing walked back out of the gate");
        Check(!defended.HasEscaped, "and the hero certainly did not");

        // --- with nobody home, an escape is a real loss ------------------------
        foreach (var creature in new List<CreatureAI>(creatures.Creatures))
            creature.TakeDamage(1000, null);
        StepBattle(heroes, creatures, 0.2f);
        Check(creatures.CreatureCount == 0, "the defenders are gone for the third raid");
        Check(battlefield.CountOf(Side.Dungeon) == 0, "and off the battlefield with them");

        heroes.Raid();
        var third = heroes.Heroes[0];

        int beforeEscape = world.Economy.Gold;
        float stole = RunHeroUntil(third, () => third.HasEscaped, 400f);
        Check(stole > 0f, $"an unopposed hero carried the loot back out ({stole:0.0}s)");

        int taken = third.CarriedGold;
        StepRaidClock(heroes, 0.2f);
        Check(heroes.Escaped == 1, "the escape is counted");
        Check(heroes.GoldStolen == taken, $"and the loss is recorded ({heroes.GoldStolen} gold)");
        Check(world.Economy.Gold == beforeEscape - taken, "the vault is lighter by exactly that much");

        // --- the battlefield only ever points at the other side ----------------
        var lone = new GameObject("Lone").AddComponent<CreatureAI>();
        lone.Configure(grid, rooms, battlefield, CreatureKind.Beetle,
                       new GameObject("LoneBody").transform, null, grid.BaseCell);
        Check(!battlefield.TryFindNearestEnemy(lone, int.MaxValue, out _),
            "a dungeon creature finds no enemy when there are no heroes");
    }

    /// <summary>Runs the raid clock only, leaving the heroes themselves standing still.</summary>
    static void StepRaidClock(HeroManager heroes, float seconds)
    {
        int steps = (int)(seconds / Time.deltaTime);
        for (int i = 0; i < steps; i++) HeroManagerUpdate.Invoke(heroes, null);
    }

    static void StepHeroManager(HeroManager heroes, float seconds)
    {
        int steps = (int)(seconds / Time.deltaTime);
        for (int i = 0; i < steps; i++)
        {
            HeroManagerUpdate.Invoke(heroes, null);
            foreach (var hero in heroes.Heroes) HeroUpdate.Invoke(hero, null);
        }
    }

    static float RunHeroUntil(HeroAI hero, Func<bool> done, float timeoutSeconds)
    {
        int steps = (int)(timeoutSeconds / Time.deltaTime);
        for (int i = 0; i < steps; i++)
        {
            HeroUpdate.Invoke(hero, null);
            if (done()) return (i + 1) * Time.deltaTime;
        }
        return -1f;
    }

    static void StepBattle(HeroManager heroes, CreatureManager creatures, float seconds)
    {
        int steps = (int)(seconds / Time.deltaTime);
        for (int i = 0; i < steps; i++) TickBattle(heroes, creatures);
    }

    static float RunBattleUntil(HeroManager heroes, CreatureManager creatures,
                                Func<bool> done, float timeoutSeconds)
    {
        int steps = (int)(timeoutSeconds / Time.deltaTime);
        for (int i = 0; i < steps; i++)
        {
            TickBattle(heroes, creatures);
            if (done()) return (i + 1) * Time.deltaTime;
        }
        return -1f;
    }

    static void TickBattle(HeroManager heroes, CreatureManager creatures)
    {
        for (int i = heroes.Heroes.Count - 1; i >= 0; i--) HeroUpdate.Invoke(heroes.Heroes[i], null);
        for (int i = creatures.Creatures.Count - 1; i >= 0; i--)
            CreatureUpdate.Invoke(creatures.Creatures[i], null);

        // The rosters are what retire the dead and hand back their lairs.
        HeroManagerUpdate.Invoke(heroes, null);
        ManagerUpdate.Invoke(creatures, null);
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
        var battlefield = new GameObject("PortalBattlefield").AddComponent<Battlefield>();
        manager.Configure(grid, rooms, economy, battlefield);

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
