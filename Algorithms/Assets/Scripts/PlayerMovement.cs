using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public PathfindingManager pathfindingManager;
    public float moveSpeed = 3f;

    private List<Vector3> currentPath = new();
    private bool isMoving = false;

    private void Awake()
    {
        // Auto-find the PathfindingManager in the scene
        if (pathfindingManager == null)
        {
            pathfindingManager = FindObjectOfType<PathfindingManager>();
        }
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0)) // Left-click
        {
            TryMoveToClickedPosition();
        }

        if (isMoving)
        {
            FollowPath();
        }
    }

    void TryMoveToClickedPosition()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            Vector3 clickedWorldPos = hit.point;
            Vector2Int targetGrid = new(Mathf.RoundToInt(clickedWorldPos.x), Mathf.RoundToInt(clickedWorldPos.z));
            Vector2Int startGrid = new(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.z));

            if (pathfindingManager.grid.TryGetValue(targetGrid, out var targetNode) && targetNode.walkable)
            {
                currentPath = FindPath(startGrid, targetGrid);
                if (currentPath.Count > 0)
                {
                    isMoving = true;
                }
            }
        }

        Debug.Log("Hit: " + hit.collider.name + " Tag: " + hit.collider.tag);
    }

    void FollowPath()
    {
        if (currentPath.Count == 0)
        {
            isMoving = false;
            return;
        }

        Vector3 targetPos = currentPath[0];
        transform.position = Vector3.MoveTowards(transform.position, targetPos, moveSpeed * Time.deltaTime);

        if (Vector3.Distance(transform.position, targetPos) < 0.1f)
        {
            currentPath.RemoveAt(0);
        }
    }

    List<Vector3> FindPath(Vector2Int start, Vector2Int end)
    {
        Debug.Log($"Path found with {currentPath.Count} steps.");
        // Simple BFS for now; can be replaced with full A* later
        Queue<Vector2Int> queue = new();
        Dictionary<Vector2Int, Vector2Int> cameFrom = new();
        HashSet<Vector2Int> visited = new();

        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            if (current == end) break;

            foreach (Vector2Int dir in new[] {
                Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
            })
            {
                Vector2Int neighbor = current + dir;

                if (visited.Contains(neighbor)) continue;
                if (!pathfindingManager.grid.TryGetValue(neighbor, out var node)) continue;
                if (!node.walkable) continue;

                queue.Enqueue(neighbor);
                visited.Add(neighbor);
                cameFrom[neighbor] = current;
            }
        }

        List<Vector3> path = new();
        if (!cameFrom.ContainsKey(end)) return path; // No path found

        Vector2Int step = end;
        while (step != start)
        {
            path.Insert(0, pathfindingManager.grid[step].worldPos + Vector3.up * 0.5f);
            step = cameFrom[step];
        }

        return path;
    }
}
