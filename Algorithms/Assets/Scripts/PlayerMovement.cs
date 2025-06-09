using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public class PlayerMovement : MonoBehaviour
{
    public PathfindingManager pathfindingManager;
    public float moveSpeed = 3f;

    private List<Vector3> currentPath = new();
    private bool isMoving = false;

    private NavMeshAgent navAgent;

    private void Awake()
    {
        pathfindingManager ??= FindObjectOfType<PathfindingManager>();
        navAgent = GetComponent<NavMeshAgent>();

        if (navAgent != null)
            navAgent.enabled = pathfindingManager.useUnityNavMesh;
    }

    void Update()
    {
        if (Input.GetMouseButtonDown(0)) // Left-click
        {
            TryMoveToClickedPosition();
        }

        if (!pathfindingManager.useUnityNavMesh && isMoving)
        {
            FollowPath();
        }
    }

    void TryMoveToClickedPosition()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f))
        {
            // Ignore clicks on walls (safety check)
            if (hit.collider.CompareTag("Wall")) return;

            Vector3 clickedWorldPos = hit.point;

            if (pathfindingManager.useUnityNavMesh)
            {
                if (navAgent != null)
                {
                    navAgent.SetDestination(clickedWorldPos);
                    Debug.Log("🧭 Using Unity NavMeshAgent to move.");
                }
            }
            else
            {
                Vector2Int targetGrid = new(Mathf.RoundToInt(clickedWorldPos.x), Mathf.RoundToInt(clickedWorldPos.z));
                Vector2Int startGrid = new(Mathf.RoundToInt(transform.position.x), Mathf.RoundToInt(transform.position.z));

                if (pathfindingManager.grid.TryGetValue(targetGrid, out var targetNode) && targetNode.walkable)
                {
                    currentPath = FindPath(startGrid, targetGrid);
                    isMoving = currentPath.Count > 0;
                    Debug.Log($"🧠 A* path with {currentPath.Count} steps.");
                }
            }

            Debug.Log($"Clicked: {hit.collider.name} [{hit.collider.tag}]");
        }
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
        var openSet = new PriorityQueue<Vector2Int>();
        var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
        var gScore = new Dictionary<Vector2Int, float>();
        var fScore = new Dictionary<Vector2Int, float>();

        openSet.Enqueue(start, 0);
        gScore[start] = 0;
        fScore[start] = Heuristic(start, end);

        while (openSet.Count > 0)
        {
            Vector2Int current = openSet.Dequeue();

            if (current == end)
            {
                // Reconstruct path
                List<Vector3> path = new();
                while (current != start)
                {
                    path.Insert(0, pathfindingManager.grid[current].worldPos + Vector3.up * 0.5f);
                    current = cameFrom[current];
                }
                return path;
            }

            foreach (Vector2Int dir in new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right })
            {
                Vector2Int neighbor = current + dir;
                if (!pathfindingManager.grid.TryGetValue(neighbor, out GridNode node) || !node.walkable)
                    continue;

                float tentativeG = gScore[current] + 1;
                if (!gScore.ContainsKey(neighbor) || tentativeG < gScore[neighbor])
                {
                    cameFrom[neighbor] = current;
                    gScore[neighbor] = tentativeG;
                    fScore[neighbor] = tentativeG + Heuristic(neighbor, end);
                    openSet.Enqueue(neighbor, fScore[neighbor]);
                }
            }
        }

        return new(); // No path found
    }

    float Heuristic(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y); // Manhattan distance
    }

    public class PriorityQueue<T>
    {
        private readonly List<(T item, float priority)> elements = new();

        public int Count => elements.Count;

        public void Enqueue(T item, float priority)
        {
            elements.Add((item, priority));
        }

        public T Dequeue()
        {
            int bestIndex = 0;
            for (int i = 1; i < elements.Count; i++)
            {
                if (elements[i].priority < elements[bestIndex].priority)
                    bestIndex = i;
            }
            T bestItem = elements[bestIndex].item;
            elements.RemoveAt(bestIndex);
            return bestItem;
        }
    }
}