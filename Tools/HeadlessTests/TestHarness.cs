// Headless smoke test of the dig loop: grid generation, marking, A*, imp state machine,
// gold award. Runs against the stub mini-engine, not Unity.
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
        var gridObject = new GameObject("Grid");
        var grid = gridObject.AddComponent<GridManager>();
        grid.Configure(20, 20, 1337, 0.10f, 2);

        var economyObject = new GameObject("Economy");
        var economy = economyObject.AddComponent<ResourceManager>();

        var impObject = new GameObject("Imp");
        var body = new GameObject("Body").transform;
        var imp = impObject.AddComponent<ImpAI>();
        imp.MoveSpeed = 3f;
        imp.DigDuration = 1.2f;
        imp.Configure(grid, economy, body);

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

        // --- the actual loop ------------------------------------------------
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

        Check(economy.Gold == goldTilesInCorridor * GridManager.GoldPerSeam,
            $"gold counter is {economy.Gold}, expected {goldTilesInCorridor * GridManager.GoldPerSeam}");

        // --- return to base -------------------------------------------------
        float returned = RunUntil(imp, () => imp.CurrentCell == grid.BaseCell && imp.State == ImpState.Idle, 120f);
        Check(returned > 0f, $"imp walked back to base and went idle after {returned:0.0}s");

        // --- unreachable work does not hang ---------------------------------
        grid.MarkForDigging(0, 0);
        Step(imp, 5f);
        Check(grid.QueuedCount == 1 && imp.State == ImpState.Idle,
            "an unreachable mark leaves the imp idle instead of stuck");

        Console.WriteLine(_failures == 0
            ? "\nAll checks passed."
            : $"\n{_failures} check(s) FAILED.");
        return _failures == 0 ? 0 : 1;
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
