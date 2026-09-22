using UnityEngine;
using System.Diagnostics;
using System.Collections.Generic;
using UnityEngine.Tilemaps;


public class PathfinderScheduler : MonoBehaviour
{
    private readonly Queue<Pathfinder.Request> requests = new Queue<Pathfinder.Request>();
    Pathfinder.Request activeRequest;

    private bool hasActiveRequest;

    public double budgetMs = 1.0;

    private readonly Stopwatch stopwatch = new Stopwatch();

    Pathfinder pathfinder;
    public Tilemap obstacleLayer;
    public int nodesPerFrame = 2000;
    public int maximumRequestsStartedPerFrame = 64;
    public int ExpandedLastFrame { get; private set; }

    private void Start()
    {
        pathfinder = new Pathfinder();
    }

    private void Update()
    {
        ExpandedLastFrame = 0;
        if (pathfinder == null || obstacleLayer == null)
        {
            if (hasActiveRequest && pathfinder != null)
                pathfinder.CancelSearch();
            hasActiveRequest = false;
            return;
        }

        int remaining = Mathf.Max(1, nodesPerFrame);
        int started = 0;
        while (remaining > 0)
        {
            if (hasActiveRequest && activeRequest.unit == null)
            {
                pathfinder.CancelSearch();
                hasActiveRequest = false;
            }

            if (!hasActiveRequest)
            {
                if (requests.Count == 0 || started >= Mathf.Max(1, maximumRequestsStartedPerFrame)) break;
                activeRequest = requests.Dequeue();
                started++;
                if (activeRequest.unit == null) continue;
                pathfinder.BeginWorldSearch(activeRequest.startPosition,
                    activeRequest.targetPosition, obstacleLayer, activeRequest);
                hasActiveRequest = true;
            }

            bool finished = pathfinder.StepSearch(remaining, out int used);
            remaining -= used;
            UnityEngine.Debug.Log(remaining);
            ExpandedLastFrame += used;
            if (!finished) break;

            Pathfinder.Result result = pathfinder.CurrentResult;
            if (activeRequest.unit != null)
            {
                var completedPath = new List<Vector3>();
                pathfinder.CopyWorldPath(completedPath);
                activeRequest.unit.path = completedPath;
                UnityEngine.Debug.Log(result.Status);
            }
            hasActiveRequest = false;
        }

        //stopwatch.Restart();
        //while (requests.Count > 0)
        //{
        //    var request = requests.Dequeue();

        //    List<Vector3> path = new List<Vector3>();

        //    var result = pathfinder.FindWorldPath(request.startPosition, request.targetPosition,
        //        path,obstacleLayer, request);

        //    //assign the path 
        //    request.unit.path = path;
        //    UnityEngine.Debug.Log(result.Status);

        //    if (stopwatch.Elapsed.TotalMilliseconds >= budgetMs)
        //        break;
        //}
    }

    public void AddRequest(Pathfinder.Request r)
    {
        requests.Enqueue(r);
    }

    public void CancelAllRequests()
    {
        requests.Clear();
        if (hasActiveRequest && pathfinder != null) pathfinder.CancelSearch();
        hasActiveRequest = false;
        activeRequest = default;
    }

    private void OnDisable()
    {
        CancelAllRequests();
    }
}
