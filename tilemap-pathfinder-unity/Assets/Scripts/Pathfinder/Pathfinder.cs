using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;



[DisallowMultipleComponent]
public sealed class Pathfinder : MonoBehaviour
{
    public enum PathStatus : byte
    {
        Complete, AlreadyAtDestination, InvalidStart,NoPath,SearchLimitReached
    }

    public struct Request
    { 
        [Min(1)] public int maximumExpandedCells;
        public bool preventCornerCutting;

        public static Request Default => new Request
        {
            maximumExpandedCells = 32000,
            preventCornerCutting = true
        };
    }

    public readonly struct Result
    { 
        public readonly PathStatus Status;
        public readonly Vector3Int RequestedDestination;
        public readonly Vector3Int ReachedDestination;
        public readonly int TotalCost;
        public readonly int ExpandedCells;

        public bool HasPath => Status == PathStatus.Complete || Status == PathStatus.AlreadyAtDestination;

        internal Result(PathStatus status, Vector3Int requested, Vector3Int reached, int cost, int expanded)
        {
            Status = status;
            RequestedDestination = requested;
            ReachedDestination = reached;
            TotalCost = cost;
            ExpandedCells = expanded;
        }
    }

    private static readonly Vector3Int[] Directions =
   {
        new Vector3Int(0, 1), new Vector3Int(1, 0),
        new Vector3Int(0, -1), new Vector3Int(-1, 0),
        new Vector3Int(1, 1), new Vector3Int(1, -1),
        new Vector3Int(-1, -1), new Vector3Int(-1, 1)
    };

    private sealed class SearchData
    {
        public int Cost = int.MaxValue;
        public Vector3Int? Parent;
        public bool Closed;

    }

    private readonly Dictionary<Vector3Int, SearchData> searchData = new Dictionary<Vector3Int, SearchData>();
    private readonly MinHeap open = new MinHeap(256);

    private readonly List<Vector3Int> worldPathCells = new List<Vector3Int>();

    public Result FindPath(Vector3Int start, Vector3Int destination,
       List<Vector3Int> path, Tilemap obstacleLayer, Request request)
    {
        path.Clear();
        if (obstacleLayer == null) throw new ArgumentNullException(nameof(obstacleLayer));
        if (request.maximumExpandedCells < 1)
            request.maximumExpandedCells = 1;

        searchData.Clear();
        open.Clear();

        if (!IsWalkable(start, obstacleLayer))
            return new Result(PathStatus.InvalidStart, destination, start, 0, 0);

        if (start == destination)
        {
            path.Add(start);
            return new Result(PathStatus.AlreadyAtDestination, destination, start, 0, 0);
        }
        if (!IsWalkable(destination, obstacleLayer))
            return new Result(PathStatus.NoPath, destination, start, 0, 0);

        searchData.Add(start, new SearchData { Cost = 0 });
        int serial = 0;
        open.Push(new HeapItem(start, Heuristic(start, destination), 0, serial++));
        int expanded = 0;

        while (open.Count > 0)
        { 
            HeapItem item = open.Pop();
            SearchData current = searchData[item.Cell];
            if (current.Closed || item.Cost != current.Cost) continue;
            if (expanded >= request.maximumExpandedCells)
                return new Result(PathStatus.SearchLimitReached, destination, start, 0, expanded);

            current.Closed = true;
            expanded++;

            if (item.Cell == destination)
            {
                for (Vector3Int? cursor = item.Cell; cursor.HasValue; cursor = searchData[cursor.Value].Parent)
                    path.Add(cursor.Value);
                
                path.Reverse();
                return new Result(PathStatus.Complete, destination, destination, current.Cost, expanded);
            }

            for (int d = 0; d < Directions.Length; d++)
            { 
                Vector3Int direction = Directions[d];
                long x = (long)item.Cell.x + direction.x;
                long y = (long)item.Cell.y + direction.y;
                if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue) continue;
                Vector3Int next = new Vector3Int((int)x, (int)y);
                if (!IsWalkable(next, obstacleLayer)) continue;


                bool diagonal = direction.x != 0 && direction.y != 0;
                if (diagonal && request.preventCornerCutting &&
                 (!IsWalkable(new Vector3Int(next.x, item.Cell.y), obstacleLayer) ||
                  !IsWalkable(new Vector3Int(item.Cell.x, next.y), obstacleLayer))) continue;


                if (!searchData.TryGetValue(next, out SearchData neighbor))
                {
                    neighbor = new SearchData();
                    searchData.Add(next, neighbor);
                }
                if (neighbor.Closed) continue;
                int candidate = ClampCost((long)current.Cost + (diagonal ? 14 : 10));
                if (candidate >= neighbor.Cost) continue;
                neighbor.Cost = candidate;
                neighbor.Parent = item.Cell;
                int score = ClampCost((long)candidate + Heuristic(next, destination));
                open.Push(new HeapItem(next, score, candidate, serial++));

            }
        }

        return new Result(PathStatus.NoPath, destination, start, 0, expanded);
    }

    public Result FindWorldPath(Vector3 start, Vector3 destination,
      List<Vector3> worldPath, Tilemap obstacleLayer, Request? options = null)
    {
        worldPath.Clear();
        if (obstacleLayer == null) throw new ArgumentNullException(nameof(obstacleLayer));
        Vector3Int from = obstacleLayer.WorldToCell(start);
        Vector3Int to = obstacleLayer.WorldToCell(destination);
        Result result = FindPath(new Vector3Int(from.x, from.y), new Vector3Int(to.x, to.y),
           worldPathCells, obstacleLayer, options ?? Request.Default);

        foreach (Vector3Int cell in worldPathCells)
            worldPath.Add(obstacleLayer.GetCellCenterWorld(new Vector3Int(cell.x, cell.y, 0)));

        return result;

    }


    private static bool IsWalkable(Vector3Int cell, Tilemap obstacleLayer) =>
      obstacleLayer.GetTile(new Vector3Int(cell.x, cell.y, 0)) == null;

    private static int Heuristic(Vector3Int from, Vector3Int to)
    {
        long dx = Math.Abs((long)from.x - to.x);
        long dy = Math.Abs((long)from.y - to.y);
        return ClampCost(14 * Math.Min(dx, dy) + 10 * Math.Abs(dx - dy));
    }

    private static int ClampCost(long cost) => (int)Math.Min(int.MaxValue, cost);

    private readonly struct HeapItem
    {
        public readonly Vector3Int Cell;

        public readonly int Score, Cost, Serial;
        public HeapItem(Vector3Int cell, int score, int cost, int serial) { Cell = cell; Score = score; Cost = cost; Serial = serial; }
    }

    private sealed class MinHeap
    {
        public int Count => items.Count;

        private readonly List<HeapItem> items;
        public MinHeap(int capacity) => items = new List<HeapItem>(capacity);

        public void Clear() => items.Clear();

        public void Push(HeapItem item)
        {
            int i = items.Count;
            items.Add(item);
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (!Before(item, items[parent])) break;
                items[i] = items[parent];
                i = parent;
            }
            items[i] = item;
        }

        public HeapItem Pop()
        {
            HeapItem root = items[0];
            int lastIndex = items.Count - 1;
            HeapItem last = items[lastIndex];
            items.RemoveAt(lastIndex);
            if (items.Count == 0) return root;
            int i = 0;
            while (true)
            {
                int left = i * 2 + 1;
                if (left >= items.Count) break;
                int right = left + 1;
                int child = right < items.Count && Before(items[right], items[left]) ? right : left;
                if (!Before(items[child], last)) break;
                items[i] = items[child];
                i = child;
            }
            items[i] = last;
            return root;
        }

        private static bool Before(HeapItem a, HeapItem b)
        {
            if (a.Score != b.Score) return a.Score < b.Score;
            if (a.Cost != b.Cost) return a.Cost > b.Cost;
            return a.Serial < b.Serial;
        }
    }
}
