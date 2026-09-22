using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;


public sealed class Pathfinder
{
    public enum PathStatus : byte
    {
        Complete, AlreadyAtDestination, InvalidStart,NoPath,SearchLimitReached,
        Running,Cancelled
    }

    [System.Serializable]
    public struct Request
    {
        public Vector3 startPosition;
        public Vector3 targetPosition;
        public PathfinderDebug unit;

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
        public readonly Vector2Int RequestedDestination;
        public readonly Vector2Int ReachedDestination;
        public readonly int TotalCost;
        public readonly int ExpandedCells;

        public bool HasPath => Status == PathStatus.Complete || Status == PathStatus.AlreadyAtDestination;

        internal Result(PathStatus status, Vector2Int requested, Vector2Int reached, int cost, int expanded)
        {
            Status = status;
            RequestedDestination = requested;
            ReachedDestination = reached;
            TotalCost = cost;
            ExpandedCells = expanded;
        }
    }

    private static readonly Vector2Int[] Directions =
   {
        new Vector2Int(0, 1), new Vector2Int(1, 0),
        new Vector2Int(0, -1), new Vector2Int(-1, 0),
        new Vector2Int(1, 1), new Vector2Int(1, -1),
        new Vector2Int(-1, -1), new Vector2Int(-1, 1)
    };

    private sealed class SearchData
    {
        public int Cost = int.MaxValue;
        public Vector2Int? Parent;
        public bool Closed;

    }

    private readonly Dictionary<long, SearchData> searchData = new Dictionary<long, SearchData>();
    private readonly MinHeap open = new MinHeap(256);

    public static long GetKey(Vector2Int value)
    {
        return ((long)value.x << 32) | (uint)value.y;
    }

    public static long GetKey(int x,int y)
    {
        return ((long)x << 32) | (uint)y;
    }

    public static Vector2Int GetPosition(long key)
    {
        int x = (int)(key >> 32);
        int y = (int)key;

        return new Vector2Int(x, y);
    }

    private readonly List<Vector2Int> worldPathCells = new List<Vector2Int>();

    private Vector2Int start;
    private Vector2Int destination;
    private Tilemap obstacleLayer;
    private Request request;
    private int serial;
    private int expanded;
    private PathStatus status = PathStatus.Cancelled;
    private int finalCost;

    public bool IsSearching => status == PathStatus.Running;
    public Result CurrentResult => new Result(status, destination,
        status == PathStatus.Complete
        || status == PathStatus.AlreadyAtDestination ? destination : start,
        finalCost, expanded);

    public void BeginWorldSearch(Vector3 from, Vector3 to, Tilemap obstacles, Request? options = null)
    {
        if (obstacles == null) throw new ArgumentNullException(nameof(obstacles));
        Vector3Int a = obstacles.WorldToCell(from);
        Vector3Int b = obstacles.WorldToCell(to);
        BeginSearch(new Vector2Int(a.x, a.y), new Vector2Int(b.x, b.y), obstacles, options ?? Request.Default);
    }


    public void BeginSearch(Vector2Int from, Vector2Int to,
        Tilemap obstacles, Request request)
    {
        if (IsSearching) throw new InvalidOperationException("Finish or cancel the active search first");
        start = from;
        destination = to;
        obstacleLayer = obstacles;
        this.request = request;
        if (request.maximumExpandedCells < 1) request.maximumExpandedCells = 32000;
        searchData.Clear();
        open.Clear();
        worldPathCells.Clear();
        serial = 0;
        expanded = 0;
        finalCost = 0;
        status = PathStatus.Running;

        if (!IsWalkable(start)) { status = PathStatus.InvalidStart; return; }
        if (start == destination)
        {
            worldPathCells.Add(start);
            status = PathStatus.AlreadyAtDestination;
            return;
        }

        if (!IsWalkable(destination)) { status = PathStatus.NoPath; return; }
        searchData.Add(GetKey(start), new SearchData { Cost = 0 });
        open.Push(new HeapItem(start, Heuristic(start, destination), 0, serial++));
    }

    public bool StepSearch(int nodeBudget, out int expandedThisStep)
    {
        if (nodeBudget < 0) throw new ArgumentOutOfRangeException(nameof(nodeBudget));
        expandedThisStep = 0;
        if (!IsSearching) return true;

        while (open.Count > 0)
        { 
            if (expandedThisStep >= nodeBudget) return false;

            HeapItem item = open.Pop();
            SearchData current = searchData[GetKey(item.Cell)];
            if (current.Closed || item.Cost != current.Cost) continue;
            if (expanded >= request.maximumExpandedCells)
            {
                status = PathStatus.SearchLimitReached;
                return true;
            }

            current.Closed = true;
            expanded++;
            expandedThisStep++;
            if (item.Cell == destination)
            {
                for (Vector2Int? cursor = item.Cell; cursor.HasValue;
                    cursor = searchData[GetKey(cursor.Value)].Parent)
                    worldPathCells.Add(cursor.Value);

                worldPathCells.Reverse();
                
                SmoothPath(worldPathCells, start, destination);
                
                finalCost = current.Cost;
                status = PathStatus.Complete;
                return true;
            }

            for (int d = 0; d < Directions.Length; d++)
            {
                Vector2Int direction = Directions[d];
                long x = (long)item.Cell.x + direction.x;
                long y = (long)item.Cell.y + direction.y;
                if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue) continue;
                Vector2Int next = new Vector2Int((int)x, (int)y);
                if (!IsWalkable(next)) continue;

                bool diagonal = direction.x != 0 && direction.y != 0;
                if (diagonal && request.preventCornerCutting &&
                 (!IsWalkable(new Vector2Int(next.x, item.Cell.y)) ||
                  !IsWalkable(new Vector2Int(item.Cell.x, next.y)))) continue;

                if (!searchData.TryGetValue(GetKey(next), out SearchData neighbor))
                {
                    neighbor = new SearchData();
                    searchData.Add(GetKey(next), neighbor);
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

        status = PathStatus.NoPath;
        return true;
    }

    public void CancelSearch()
    {
        status = PathStatus.Cancelled;
        finalCost = 0;
        open.Clear();
        searchData.Clear();
        worldPathCells.Clear();
        obstacleLayer = null;
    }

    public void CopyCellPath(List<Vector2Int> output)
    {
        if (output == null) throw new ArgumentNullException(nameof(output));
        if (IsSearching) throw new InvalidOperationException("The search is still running.");
        output.Clear();
        if (CurrentResult.HasPath) output.AddRange(worldPathCells);
    }

    public void CopyWorldPath(List<Vector3> output)
    {
        if (output == null) throw new ArgumentNullException(nameof(output));
        if (IsSearching) throw new InvalidOperationException("The search is still running.");
        output.Clear();
        if (!CurrentResult.HasPath) return;
        if (obstacleLayer == null) throw new InvalidOperationException("The obstacle tilemap was destroyed.");
        foreach (Vector2Int cell in worldPathCells)
            output.Add(obstacleLayer.GetCellCenterWorld(new Vector3Int(cell.x, cell.y, 0)));
    }

    private readonly List<Vector2Int> smoothingBuffer = new List<Vector2Int>();

    private void SmoothPath(List<Vector2Int> path, Vector2Int start, Vector2Int destination)
    {
        if (path.Count < 3)
            return;

        List<Vector2Int> output = smoothingBuffer;
        output.Clear();
        output.Add(path[0]);

        int anchor = 0;
        while (anchor < path.Count - 1)
        {
            int furthest = path.Count - 1;
            while (furthest > anchor + 1 && !HasLineOfSight(path[anchor], path[furthest]))
                furthest--;

            output.Add(path[furthest]);
            anchor = furthest;
        }

        path.Clear();
        path.AddRange(output);
    }


    bool HasLineOfSight(Vector2Int from, Vector2Int to)
    {
        int x = from.x;
        int y = from.y;
        long dx = Math.Abs((long)to.x - x);
        long dy = Math.Abs((long)to.y - y);
        int stepX = Math.Sign((long)to.x - x);
        int stepY = Math.Sign((long)to.y - y);
        long crossedX = 0;
        long crossedY = 0;
        if (!IsWalkable(new Vector2Int(x, y))) return false;

        while (x != to.x || y != to.y)
        {
            decimal boundaryX = (2 * crossedX + 1) * (decimal)dy;
            decimal boundaryY = (2 * crossedY + 1) * (decimal)dx;
            if (boundaryX == boundaryY)
            {
                if (!IsWalkable(new Vector2Int(x + stepX, y)) || !IsWalkable(new Vector2Int(x, y + stepY)))
                    return false;

                x += stepX;
                y += stepY;
                crossedX++;
                crossedY++;
            }
            else if (boundaryX < boundaryY)
            {
                x += stepX;
                crossedX++;
            }
            else
            {
                y += stepY;
                crossedY++;
            }
            if (!IsWalkable(new Vector2Int(x, y)))
                return false;
        }

        return true;

    }


    //keep for compatibility, it will return a path at a single process
    public Result FindPath(Vector2Int start, Vector2Int destination,
       List<Vector2Int> path, Tilemap obstacleLayer, Request request)
    {
        this.obstacleLayer = obstacleLayer;
        path.Clear();
        if (obstacleLayer == null) throw new ArgumentNullException(nameof(obstacleLayer));
        if (request.maximumExpandedCells < 1)
            request.maximumExpandedCells = 32000;

        searchData.Clear();
        open.Clear();

        if (!IsWalkable(start))
            return new Result(PathStatus.InvalidStart, destination, start, 0, 0);

        if (start == destination)
        {
            path.Add(start);
            return new Result(PathStatus.AlreadyAtDestination, destination, start, 0, 0);
        }
        if (!IsWalkable(destination))
            return new Result(PathStatus.NoPath, destination, start, 0, 0);

        searchData.Add(GetKey(start), new SearchData { Cost = 0 });
        int serial = 0;
        open.Push(new HeapItem(start, Heuristic(start, destination), 0, serial++));
        int expanded = 0;

        while (open.Count > 0)
        { 
            HeapItem item = open.Pop();
            SearchData current = searchData[GetKey(item.Cell)];
            if (current.Closed || item.Cost != current.Cost) continue;
            if (expanded >= request.maximumExpandedCells)
                return new Result(PathStatus.SearchLimitReached, destination, start, 0, expanded);

            current.Closed = true;
            expanded++;

            if (item.Cell == destination)
            {
                for (Vector2Int? cursor = item.Cell; cursor.HasValue; cursor = searchData[GetKey(cursor.Value)].Parent)
                    path.Add(cursor.Value);
                
                path.Reverse();
                return new Result(PathStatus.Complete, destination, destination, current.Cost, expanded);
            }

            for (int d = 0; d < Directions.Length; d++)
            { 
                Vector2Int direction = Directions[d];
                long x = (long)item.Cell.x + direction.x;
                long y = (long)item.Cell.y + direction.y;
                if (x < int.MinValue || x > int.MaxValue || y < int.MinValue || y > int.MaxValue) continue;
                Vector2Int next = new Vector2Int((int)x, (int)y);
                if (!IsWalkable(next)) continue;


                bool diagonal = direction.x != 0 && direction.y != 0;
                if (diagonal && request.preventCornerCutting &&
                 (!IsWalkable(new Vector2Int(next.x, item.Cell.y)) ||
                  !IsWalkable(new Vector2Int(item.Cell.x, next.y)))) continue;


                if (!searchData.TryGetValue(GetKey(next), out SearchData neighbor))
                {
                    neighbor = new SearchData();
                    searchData.Add(GetKey(next), neighbor);
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

        var from = obstacleLayer.WorldToCell(start);
        var to = obstacleLayer.WorldToCell(destination);
        Result result = FindPath(new Vector2Int(from.x, from.y), new Vector2Int(to.x, to.y),
           worldPathCells, obstacleLayer, options ?? Request.Default);

        Vector3Int tempPosition = Vector3Int.zero;

        foreach (Vector2Int cell in worldPathCells)
        {
            tempPosition.x = cell.x;
            tempPosition.y = cell.y;
            tempPosition.z = 0;

            worldPath.Add(obstacleLayer.GetCellCenterWorld(tempPosition));
        }

        return result;

    }


    private bool IsWalkable(Vector2Int cell) =>
      obstacleLayer.GetTile(new Vector3Int(cell.x, cell.y, 0)) == null;

    private static int Heuristic(Vector2Int from, Vector2Int to)
    {
        long dx = Math.Abs((long)from.x - to.x);
        long dy = Math.Abs((long)from.y - to.y);
        return ClampCost(14 * Math.Min(dx, dy) + 10 * Math.Abs(dx - dy));
    }

    private static int ClampCost(long cost) => (int)Math.Min(int.MaxValue, cost);

    private readonly struct HeapItem
    {
        public readonly Vector2Int Cell;

        public readonly int Score, Cost, Serial;
        public HeapItem(Vector2Int cell, int score, int cost, int serial) { Cell = cell; Score = score; Cost = cost; Serial = serial; }
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
