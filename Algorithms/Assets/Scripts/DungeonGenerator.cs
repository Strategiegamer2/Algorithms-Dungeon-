using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public class DungeonGenerator : MonoBehaviour
{
    [Header("Dungeon Settings")]
    public int dungeonWidth = 50;
    public int dungeonHeight = 50;
    public int roomMinSize = 6;
    public float splitDelay = 0.05f;
    public float lineDrawDelay = 0.02f;
    public float doorDelay = 0.01f;
    public int seed = 1337;
    [Range(0f, 1f)]
    public float percentRoomsToRemove = 0.1f;

    [Header("Prefabs")]
    public GameObject wallPrefab;
    public GameObject floorPrefab;
    public GameObject doorPrefab;

    private List<Room> rooms = new List<Room>();
    private List<GameObject> roomVisuals = new List<GameObject>();
    private List<Leaf> activeLeaves = new List<Leaf>();
    private Dictionary<Room, List<Room>> graph = new Dictionary<Room, List<Room>>();
    private System.Random rng;
    private HashSet<Vector3> occupiedPositions = new HashSet<Vector3>();
    private HashSet<Vector2Int> doorPositions = new HashSet<Vector2Int>();

    void Start()
    {
        rng = new System.Random(seed);
        StartCoroutine(GenerateBSPDungeonAnimated());
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            StopAllCoroutines();
            RegenerateDungeon();
        }
    }

    void RegenerateDungeon()
    {
        foreach (GameObject go in roomVisuals)
            Destroy(go);

        roomVisuals.Clear();
        rooms.Clear();
        activeLeaves.Clear();
        graph.Clear();
        occupiedPositions.Clear();
        doorPositions.Clear();

        rng = new System.Random(seed);
        StartCoroutine(GenerateBSPDungeonAnimated());
    }

    IEnumerator GenerateBSPDungeonAnimated()
    {
        Leaf root = new Leaf(0, 0, dungeonWidth, dungeonHeight);
        activeLeaves.Add(root);

        Queue<Leaf> queue = new Queue<Leaf>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            Leaf current = queue.Dequeue();
            bool splitSuccess = false;

            if (current.height >= roomMinSize * 2)
                splitSuccess = current.Split(true, roomMinSize, rng);
            else if (current.width >= roomMinSize * 2)
                splitSuccess = current.Split(false, roomMinSize, rng);

            if (splitSuccess)
            {
                queue.Enqueue(current.left);
                queue.Enqueue(current.right);
                activeLeaves.Add(current.left);
                activeLeaves.Add(current.right);
                yield return new WaitForSeconds(splitDelay);
            }
        }

        foreach (Leaf leaf in activeLeaves)
        {
            if (leaf.left == null && leaf.right == null)
                rooms.Add(new Room(leaf.x, leaf.y, leaf.width, leaf.height));
        }

        List<Room> sorted = rooms.OrderBy(r => r.width * r.height).ToList();
        int countToRemove = Mathf.FloorToInt(rooms.Count * percentRoomsToRemove);
        List<Room> candidates = sorted.Take(countToRemove).ToList();
        List<Room> candidateFreeRooms = rooms.Except(candidates).ToList();

        Dictionary<Room, List<Room>> tempGraph = BuildGraph(candidateFreeRooms);

        if (IsGraphConnected(tempGraph))
        {
            foreach (Room r in candidates)
                r.isRemoved = true;
            graph = tempGraph;
            rooms = candidateFreeRooms;
        }
        else
        {
            graph = BuildGraph(rooms);
        }

        rooms = rooms.OrderByDescending(r => r.y + r.height).ThenBy(r => r.x).ToList();

        yield return StartCoroutine(SpawnRoomMeshes());
        yield return StartCoroutine(PlaceDoorsAnimated());
        yield return StartCoroutine(SpawnStretchedWalls());
    }

    IEnumerator SpawnRoomMeshes()
    {
        foreach (Room room in rooms)
        {
            for (int x = room.x; x < room.x + room.width; x++)
            {
                for (int y = room.y; y < room.y + room.height; y++)
                {
                    Vector3 pos = new Vector3(x, 0, y);
                    if (!occupiedPositions.Contains(pos))
                    {
                        GameObject f = Instantiate(floorPrefab, pos, Quaternion.identity);
                        roomVisuals.Add(f);
                        occupiedPositions.Add(pos);
                    }
                }
            }
        }
        yield return null;
    }

    IEnumerator PlaceDoorsAnimated()
    {
        foreach (Room a in graph.Keys)
        {
            foreach (Room b in graph[a])
            {
                if (a.GetHashCode() < b.GetHashCode())
                {
                    if (a.x == b.x + b.width || b.x == a.x + a.width)
                    {
                        int yMin = Mathf.Max(a.y, b.y) + 1;
                        int yMax = Mathf.Min(a.y + a.height, b.y + b.height) - 1;
                        if (yMax > yMin)
                        {
                            int doorY = rng.Next(yMin, yMax);
                            int doorX = (a.x == b.x + b.width) ? a.x : b.x;
                            PlaceDoorOnWall(doorX, doorY, true);
                            yield return new WaitForSeconds(doorDelay);
                        }
                    }
                    else if (a.y == b.y + b.height || b.y == a.y + a.height)
                    {
                        int xMin = Mathf.Max(a.x, b.x) + 1;
                        int xMax = Mathf.Min(a.x + a.width, b.x + b.width) - 1;
                        if (xMax > xMin)
                        {
                            int doorX = rng.Next(xMin, xMax);
                            int doorY = (a.y == b.y + b.height) ? a.y : b.y;
                            PlaceDoorOnWall(doorX, doorY, false);
                            yield return new WaitForSeconds(doorDelay);
                        }
                    }
                }
            }
        }
    }

    void PlaceDoorOnWall(int x, int y, bool isVertical)
    {
        Vector3 pos = new Vector3(x + (isVertical ? 0f : 0.5f), 0f, y + (isVertical ? 0.5f : 0f));
        Vector2Int grid = new(x, y);
        if (!doorPositions.Contains(grid))
        {
            GameObject door = Instantiate(doorPrefab, pos, Quaternion.identity);
            roomVisuals.Add(door);
            doorPositions.Add(grid);
        }
    }

    IEnumerator SpawnStretchedWalls()
    {
        HashSet<Vector2Int> wallSpots = new();
        foreach (Room room in rooms)
        {
            int x0 = room.x - 1, x1 = room.x + room.width;
            int y0 = room.y - 1, y1 = room.y + room.height;

            for (int x = room.x; x < room.x + room.width; x++)
            {
                wallSpots.Add(new Vector2Int(x, y0));
                wallSpots.Add(new Vector2Int(x, y1));
            }
            for (int y = room.y; y < room.y + room.height; y++)
            {
                wallSpots.Add(new Vector2Int(x0, y));
                wallSpots.Add(new Vector2Int(x1, y));
            }
        }

        foreach (var group in wallSpots.Except(doorPositions).GroupBy(p => p.y))
        {
            var line = group.OrderBy(p => p.x).ToList();
            for (int i = 0; i < line.Count;)
            {
                int startX = line[i].x;
                int y = line[i].y;
                int length = 1;
                while (i + length < line.Count && line[i + length].x == startX + length) length++;
                Vector3 pos = new Vector3(startX + (length / 2f) - 0.5f, 0.5f, y);
                GameObject wall = Instantiate(wallPrefab, pos, Quaternion.identity);
                wall.transform.localScale = new Vector3(length, 1f, 1f);
                roomVisuals.Add(wall);
                i += length;
                yield return null;
            }
        }

        foreach (var group in wallSpots.Except(doorPositions).GroupBy(p => p.x))
        {
            var line = group.OrderBy(p => p.y).ToList();
            for (int i = 0; i < line.Count;)
            {
                int startY = line[i].y;
                int x = line[i].x;
                int length = 1;
                while (i + length < line.Count && line[i + length].y == startY + length) length++;
                Vector3 pos = new Vector3(x, 0.5f, startY + (length / 2f) - 0.5f);
                GameObject wall = Instantiate(wallPrefab, pos, Quaternion.identity);
                wall.transform.localScale = new Vector3(1f, 1f, length);
                roomVisuals.Add(wall);
                i += length;
                yield return null;
            }
        }
    }

    Dictionary<Room, List<Room>> BuildGraph(List<Room> source)
    {
        Dictionary<Room, List<Room>> g = new();
        foreach (Room r in source) g[r] = new List<Room>();

        for (int i = 0; i < source.Count; i++)
        {
            for (int j = i + 1; j < source.Count; j++)
            {
                Room a = source[i];
                Room b = source[j];

                if (a.x == b.x + b.width || b.x == a.x + a.width)
                {
                    if (Overlap(a.y, a.height, b.y, b.height))
                    {
                        g[a].Add(b);
                        g[b].Add(a);
                    }
                }
                else if (a.y == b.y + b.height || b.y == a.y + a.height)
                {
                    if (Overlap(a.x, a.width, b.x, b.width))
                    {
                        g[a].Add(b);
                        g[b].Add(a);
                    }
                }
            }
        }
        return g;
    }

    bool Overlap(int aStart, int aSize, int bStart, int bSize)
    {
        return Mathf.Max(aStart, bStart) < Mathf.Min(aStart + aSize, bStart + bSize);
    }

    bool IsGraphConnected(Dictionary<Room, List<Room>> g)
    {
        if (g.Count == 0) return true;
        Queue<Room> q = new();
        HashSet<Room> visited = new();
        Room start = g.Keys.First();
        visited.Add(start);
        q.Enqueue(start);

        while (q.Count > 0)
        {
            Room curr = q.Dequeue();
            foreach (Room n in g[curr])
            {
                if (!visited.Contains(n))
                {
                    visited.Add(n);
                    q.Enqueue(n);
                }
            }
        }
        return visited.Count == g.Count;
    }
}

public class Room
{
    public int x, y, width, height;
    public bool isRemoved = false;
    public Room(int x, int y, int width, int height)
    {
        this.x = x; this.y = y;
        this.width = width; this.height = height;
    }
}

public class Leaf
{
    public int x, y, width, height;
    public Leaf left, right;
    public Leaf(int x, int y, int width, int height)
    {
        this.x = x; this.y = y;
        this.width = width; this.height = height;
    }

    public bool Split(bool horizontal, int minSize, System.Random rng)
    {
        if (horizontal)
        {
            if (height < minSize * 2) return false;
            int split = rng.Next(minSize, height - minSize);
            left = new Leaf(x, y, width, split);
            right = new Leaf(x, y + split, width, height - split);
        }
        else
        {
            if (width < minSize * 2) return false;
            int split = rng.Next(minSize, width - minSize);
            left = new Leaf(x, y, split, height);
            right = new Leaf(x + split, y, width - split, height);
        }
        return true;
    }
}


