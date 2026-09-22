using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Tilemaps;

public class PathfinderDebug : MonoBehaviour
{
    public Transform start;
    public Transform destination;

    public bool requestPath = true;

    public List<Vector3> path = new List<Vector3>();

    public Tilemap obstacleLayer;

    public Pathfinder.Request request;
    public PathfinderScheduler scheduler;

    private void Update()
    {
        if (requestPath)
        {
            requestPath = false;
            RequestPath();
        }

        for (int i = 1; i < path.Count; i++)
        {
            Debug.DrawLine(path[i - 1], path[i], Color.green, 0, false);
        }
    }

    public void RequestPath()
    {
        path.Clear();

        request.startPosition = start.position;
        request.targetPosition = destination.position;
        request.unit = this;
        scheduler.AddRequest(request);


        //Pathfinder.Result result = GetComponent<Pathfinder>().FindWorldPath(
        //   start.position, destination.position, path, obstacleLayer);

        //Debug.Log(result.Status);
    }
}
