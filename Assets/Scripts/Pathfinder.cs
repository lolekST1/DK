using System.Collections.Generic;
using UnityEngine;

namespace DK
{
    /// <summary>
    /// Hand-rolled 4-directional A* over the tile grid. Deliberately not NavMesh: tiles appear
    /// and disappear at runtime, and re-baking a NavMesh for that is both slower and less
    /// predictable than searching the array we already have.
    /// </summary>
    public static class Pathfinder
    {
        static readonly Vector2Int[] Neighbours =
        {
            new Vector2Int(1, 0),
            new Vector2Int(-1, 0),
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
        };

        // Scratch buffers, reused between searches so pathfinding does not allocate per call.
        static float[] _gScore;
        static float[] _fScore;
        static int[] _cameFrom;
        static bool[] _closed;
        static MinHeap _heap;
        static int _width;

        /// <summary>
        /// Finds a walkable path from <paramref name="start"/> to <paramref name="goal"/>.
        /// The result includes both endpoints. Returns false if no path exists.
        /// </summary>
        public static bool TryFindPath(GridManager grid, Vector2Int start, Vector2Int goal, List<Vector2Int> result)
        {
            result.Clear();

            if (!grid.IsWalkable(start) || !grid.IsWalkable(goal)) return false;

            if (start == goal)
            {
                result.Add(start);
                return true;
            }

            EnsureBuffers(grid.Width, grid.Depth);

            int startIndex = Index(start);
            int goalIndex = Index(goal);

            _heap.Clear();
            _gScore[startIndex] = 0f;
            _fScore[startIndex] = Heuristic(start, goal);
            _cameFrom[startIndex] = -1;
            _heap.Push(startIndex, _fScore[startIndex]);

            while (_heap.Count > 0)
            {
                int current = _heap.Pop();

                if (current == goalIndex)
                {
                    Reconstruct(current, result);
                    return true;
                }

                if (_closed[current]) continue;
                _closed[current] = true;

                var currentCell = Cell(current);
                for (int i = 0; i < Neighbours.Length; i++)
                {
                    var next = currentCell + Neighbours[i];
                    if (!grid.IsWalkable(next)) continue;

                    int nextIndex = Index(next);
                    if (_closed[nextIndex]) continue;

                    float tentative = _gScore[current] + 1f;
                    if (tentative >= _gScore[nextIndex]) continue;

                    _cameFrom[nextIndex] = current;
                    _gScore[nextIndex] = tentative;
                    _fScore[nextIndex] = tentative + Heuristic(next, goal);
                    _heap.Push(nextIndex, _fScore[nextIndex]);
                }
            }

            return false;
        }

        static void Reconstruct(int current, List<Vector2Int> result)
        {
            while (current != -1)
            {
                result.Add(Cell(current));
                current = _cameFrom[current];
            }
            result.Reverse();
        }

        static void EnsureBuffers(int width, int depth)
        {
            int count = width * depth;
            if (_gScore == null || _gScore.Length != count || _width != width)
            {
                _gScore = new float[count];
                _fScore = new float[count];
                _cameFrom = new int[count];
                _closed = new bool[count];
                _heap = new MinHeap(count);
            }

            _width = width;

            for (int i = 0; i < count; i++)
            {
                _gScore[i] = float.PositiveInfinity;
                _fScore[i] = float.PositiveInfinity;
                _cameFrom[i] = -1;
                _closed[i] = false;
            }
        }

        static float Heuristic(Vector2Int a, Vector2Int b) =>
            Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);

        static int Index(Vector2Int cell) => cell.y * _width + cell.x;

        static Vector2Int Cell(int index) => new Vector2Int(index % _width, index / _width);

        /// <summary>Minimal binary min-heap keyed on float priority, storing cell indices.</summary>
        class MinHeap
        {
            int[] _items;
            float[] _priorities;
            int _count;

            public MinHeap(int capacity)
            {
                capacity = Mathf.Max(8, capacity);
                _items = new int[capacity];
                _priorities = new float[capacity];
            }

            public int Count => _count;

            public void Clear() => _count = 0;

            public void Push(int item, float priority)
            {
                if (_count == _items.Length) Grow();

                _items[_count] = item;
                _priorities[_count] = priority;

                int child = _count++;
                while (child > 0)
                {
                    int parent = (child - 1) / 2;
                    if (_priorities[parent] <= _priorities[child]) break;
                    Swap(parent, child);
                    child = parent;
                }
            }

            public int Pop()
            {
                int top = _items[0];
                _count--;
                _items[0] = _items[_count];
                _priorities[0] = _priorities[_count];

                int parent = 0;
                while (true)
                {
                    int left = parent * 2 + 1;
                    int right = left + 1;
                    int smallest = parent;

                    if (left < _count && _priorities[left] < _priorities[smallest]) smallest = left;
                    if (right < _count && _priorities[right] < _priorities[smallest]) smallest = right;
                    if (smallest == parent) break;

                    Swap(parent, smallest);
                    parent = smallest;
                }

                return top;
            }

            void Grow()
            {
                System.Array.Resize(ref _items, _items.Length * 2);
                System.Array.Resize(ref _priorities, _priorities.Length * 2);
            }

            void Swap(int a, int b)
            {
                (_items[a], _items[b]) = (_items[b], _items[a]);
                (_priorities[a], _priorities[b]) = (_priorities[b], _priorities[a]);
            }
        }
    }
}
