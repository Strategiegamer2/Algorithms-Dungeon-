using System.Collections.Generic;
using System.Linq;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshSurface))]
public class PathfindingManager : MonoBehaviour
{
    [Header("General Settings")]
    public bool useUnityNavMesh = false;
    public float tileSize = 1f;

    public Dictionary<Vector2Int, GridNode> grid = new();

    private NavMeshSurface navMeshSurface;

    private void Awake()
    {
        navMeshSurface = GetComponent<NavMeshSurface>();
    }

    public void BuildGridFromScene()
    {
        grid.Clear();

        if (useUnityNavMesh)
        {
            BakeNavMeshOnFloors();
            return;
        }

        GameObject[] allObjects = FindObjectsOfType<GameObject>();

        foreach (GameObject obj in allObjects)
        {
            if (!obj.activeInHierarchy) continue;

            string tag = obj.tag;
            if (tag != "Floor" && tag != "Door" && tag != "Wall" && tag != "Player") continue;

            if (!obj.TryGetComponent<Collider>(out Collider col)) continue;

            Bounds bounds = col.bounds;

            if (tag == "Wall" || tag == "Door")
            {
                bounds.Expand(new Vector3(-0.2f, 0f, -0.2f));
            }

            Vector3 min = bounds.min;
            Vector3 max = bounds.max;

            for (int x = Mathf.FloorToInt(min.x); x <= Mathf.FloorToInt(max.x); x++)
            {
                for (int z = Mathf.FloorToInt(min.z); z <= Mathf.FloorToInt(max.z); z++)
                {
                    Vector2Int gridPos = new Vector2Int(x, z);
                    Vector3 worldPos = new Vector3(x + 0.5f, 0.05f, z + 0.5f);

                    if (!grid.TryGetValue(gridPos, out GridNode existing))
                    {
                        grid[gridPos] = new GridNode(gridPos, worldPos, tag != "Wall");
                    }
                    else
                    {
                        if (tag == "Door")
                            grid[gridPos] = new GridNode(gridPos, worldPos, true);
                        else if (tag == "Wall" && existing.walkable)
                            grid[gridPos] = new GridNode(gridPos, worldPos, false);
                    }
                }
            }
        }

        Debug.Log($"✅ Grid built with {grid.Count} nodes.");
    }

    private void BakeNavMeshOnFloors()
    {
        if (navMeshSurface == null)
        {
            Debug.LogError("❌ NavMeshSurface component missing!");
            return;
        }

        // 1. Disable all "Door" and "TopOfDoor" objects in the scene
        List<GameObject> toRestore = new();
        GameObject[] allObjects = FindObjectsOfType<GameObject>();

        foreach (GameObject obj in allObjects)
        {
            if (!obj.activeInHierarchy) continue;

            string name = obj.name;
            if (name.StartsWith("Door") || name.StartsWith("TopOfDoor"))
            {
                obj.SetActive(false);
                toRestore.Add(obj);
            }
        }

        // 2. Bake the NavMesh
        navMeshSurface.BuildNavMesh();
        Debug.Log("✅ NavMesh baked at runtime (with doors temporarily disabled)");

        // 3. Re-enable the objects
        foreach (GameObject obj in toRestore)
        {
            obj.SetActive(true);
        }
    }
}

public class GridNode
{
    public Vector2Int gridPos;
    public Vector3 worldPos;
    public bool walkable;

    public GridNode(Vector2Int gridPos, Vector3 worldPos, bool walkable)
    {
        this.gridPos = gridPos;
        this.worldPos = worldPos;
        this.walkable = walkable;
    }
}