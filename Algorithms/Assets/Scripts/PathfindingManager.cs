using System.Collections.Generic;
using UnityEngine;

public class PathfindingManager : MonoBehaviour
{
    public Dictionary<Vector2Int, GridNode> grid = new();
    public float tileSize = 1f; // default 1 unit per tile

    public void BuildGridFromScene()
    {
        grid.Clear();

        GameObject[] allObjects = FindObjectsOfType<GameObject>();

        foreach (GameObject obj in allObjects)
        {
            if (!obj.activeInHierarchy) continue;

            string tag = obj.tag;
            if (tag != "Floor" && tag != "Door" && tag != "Wall" && tag != "Player") continue;

            if (!obj.TryGetComponent<Collider>(out Collider col)) continue;

            Bounds bounds = col.bounds;

            // Shrink bounds slightly to avoid overlap issues
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
                    float groundedY = 0.05f; // consistent grounded height
                    Vector3 worldPos = new Vector3(x + 0.5f, groundedY, z + 0.5f);

                    // Apply priority rules
                    if (!grid.TryGetValue(gridPos, out GridNode existing))
                    {
                        // Not set yet — initialize
                        grid[gridPos] = new GridNode(gridPos, worldPos, tag != "Wall");
                    }
                    else
                    {
                        // Already exists — apply priority
                        if (tag == "Door")
                        {
                            // Highest priority — override all
                            grid[gridPos] = new GridNode(gridPos, worldPos, true);
                        }
                        else if (tag == "Wall" && existing.walkable)
                        {
                            // Override only if current is walkable
                            grid[gridPos] = new GridNode(gridPos, worldPos, false);
                        }
                    }
                }
            }
        }

        Debug.Log($"✅ Grid built with {grid.Count} nodes.");
    }

    private void OnDrawGizmos()
    {
        if (grid == null) return;

        foreach (var node in grid.Values)
        {
            Gizmos.color = node.walkable ? Color.green : Color.red;
            Gizmos.DrawCube(node.worldPos + Vector3.up * 0.1f, Vector3.one * 0.3f);
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

