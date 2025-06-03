using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class DungeonGenerator : MonoBehaviour
{
    [Header("Dungeon Settings")]
    public int dungeonWidth = 50;
    public int dungeonHeight = 50;
    public int roomMinSize = 6;
    public float splitDelay = 0.05f;
    public float lineDrawDelay = 0.02f;
    public float doorDelay = 0.01f;

    [Range(0, 100)]
    public int percentRoomsToRemove = 10;

    [Header("Seed Settings")]
    public int seed = 1337;

    [Header("Prefabs")]
    public GameObject floorPrefab;
    public GameObject doorPrefab;
    public GameObject wallPrefab;
    public GameObject doorTopPrefab;
    public GameObject playerPrefab;

    private List<Room> rooms = new List<Room>();
    private List<GameObject> roomVisuals = new List<GameObject>();
    private List<Leaf> activeLeaves = new List<Leaf>();
    private Dictionary<Room, List<Room>> graph = new Dictionary<Room, List<Room>>();
    private System.Random rng;
    private GameObject spawnedPlayer;

    void Start()
    {
        rng = new System.Random(seed);
        DrawDungeonBounds();
        StartCoroutine(GenerateBSPDungeonAnimated());
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
            seed = UnityEngine.Random.Range(0, int.MaxValue);
            StopAllCoroutines();
            RegenerateDungeon();
        }
    }

    void RegenerateDungeon()
    {
        rng = new System.Random(seed);

        foreach (GameObject go in roomVisuals)
            Destroy(go);
        roomVisuals.Clear();
        rooms.Clear();
        activeLeaves.Clear();
        graph.Clear();

        if (spawnedPlayer != null)
        {
            Destroy(spawnedPlayer);
            spawnedPlayer = null;
        }

        StartCoroutine(GenerateBSPDungeonAnimated());
    }


    void DrawDungeonBounds()
    {
        GameObject boundsObj = new GameObject("DungeonBounds");
        LineRenderer lr = boundsObj.AddComponent<LineRenderer>();

        lr.positionCount = 5;
        lr.startWidth = 0.3f;
        lr.endWidth = 0.3f;
        lr.useWorldSpace = true;

        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.startColor = Color.yellow;
        lr.endColor = Color.yellow;

        Vector3[] points = new Vector3[]
        {
            new Vector3(0, 0, 0),
            new Vector3(dungeonWidth, 0, 0),
            new Vector3(dungeonWidth, 0, dungeonHeight),
            new Vector3(0, 0, dungeonHeight),
            new Vector3(0, 0, 0)
        };

        lr.SetPositions(points);
    }

    IEnumerator GenerateBSPDungeonAnimated()
    {
        bool connected = false;
        int attempt = 0;
        int maxAttempts = 5;

        while (!connected)
        {
            attempt++;

            if (attempt > maxAttempts)
            {
                // Generate a new seed and reinit RNG
                seed = UnityEngine.Random.Range(0, int.MaxValue);
                rng = new System.Random(seed);
                attempt = 1;
                Debug.Log($"Using seed: {seed} (attempt {attempt})");
                Debug.LogWarning($"Retry limit reached. New seed: {seed}");
            }

            // Clean up old data
            rooms.Clear();
            activeLeaves.Clear();
            graph.Clear();

            // Clear previous visualizations
            foreach (GameObject go in roomVisuals)
                Destroy(go);
            roomVisuals.Clear();

            // Regenerate
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
                {
                    Room room = new Room(leaf.x, leaf.y, leaf.width, leaf.height);
                    rooms.Add(room);
                    graph.Add(room, new List<Room>());
                }
            }

            rooms.Sort((a, b) => {
                int ay = a.y + a.height;
                int by = b.y + b.height;
                if (ay != by) return by.CompareTo(ay);
                return a.x.CompareTo(b.x);
            });

            // Try to prune smallest rooms
            List<Room> sortedByArea = rooms.OrderBy(r => r.width * r.height).ToList();
            int countToRemove = Mathf.FloorToInt(rooms.Count * (percentRoomsToRemove / 100f));
            List<Room> candidatesToRemove = sortedByArea.Take(countToRemove).ToList();
            List<Room> remainingRooms = rooms.Except(candidatesToRemove).ToList();

            // Build new graph for remaining rooms
            Dictionary<Room, List<Room>> newGraph = BuildGraph(remainingRooms);

            List<Room> finalRooms = rooms;
            Dictionary<Room, List<Room>> finalGraph = graph;

            if (IsGraphConnected(newGraph))
            {
                finalRooms = remainingRooms;
                finalGraph = newGraph;
            }
            else
            {
                Debug.Log("Could not prune rooms while maintaining connectivity.");
                finalGraph = new Dictionary<Room, List<Room>>();
                foreach (Room r in rooms)
                    finalGraph[r] = new List<Room>();
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

            // Build connections
            Dictionary<Room, List<Room>> actualDoorGraph = new();
            foreach (Room r in finalRooms) actualDoorGraph[r] = new List<Room>();

            yield return StartCoroutine(PlaceDoorsAnimated(finalRooms, finalGraph, actualDoorGraph));

            rooms = finalRooms;
            graph = actualDoorGraph; // Only rooms with doors get connections now


            // Now check if the graph is connected
            connected = IsGraphConnected(graph);

            if (!connected)
            {
                Debug.Log("Generated dungeon was not connected. Retrying...");
                yield return null; // Prevents infinite loop hang
            }
        }

        Debug.Log("Dungeon generated with full connectivity.");
        yield return StartCoroutine(DrawRoomsAnimated());
        yield return StartCoroutine(SpawnFloorTiles());
        yield return StartCoroutine(SpawnWalls(rooms, graph));
        yield return StartCoroutine(DrawGraphConnections());
        SpawnPlayerInRandomRoom();
    }


    IEnumerator DrawRoomsAnimated()
    {
        foreach (Room room in rooms)
        {
            GameObject roomObj = new GameObject($"Room ({room.x}, {room.y})");
            roomVisuals.Add(roomObj);

            LineRenderer lr = roomObj.AddComponent<LineRenderer>();
            lr.startWidth = 0.2f;
            lr.endWidth = 0.2f;
            lr.useWorldSpace = true;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = Color.blue;
            lr.endColor = Color.blue;

            Vector3[] corners = new Vector3[]
            {
                new Vector3(room.x, 0, room.y),
                new Vector3(room.x + room.width, 0, room.y),
                new Vector3(room.x + room.width, 0, room.y + room.height),
                new Vector3(room.x, 0, room.y + room.height),
                new Vector3(room.x, 0, room.y)
            };

            lr.positionCount = 0;
            for (int i = 0; i < corners.Length; i++)
            {
                lr.positionCount++;
                lr.SetPosition(i, corners[i]);
                yield return new WaitForSeconds(lineDrawDelay);
            }
        }
    }

    IEnumerator SpawnFloorTiles()
    {
        foreach (Room room in rooms)
        {
            Vector3 center = new Vector3(room.x + room.width / 2f, 0f, room.y + room.height / 2f);
            Vector3 scale = new Vector3(room.width, 0.1f, room.height);

            GameObject floor = Instantiate(floorPrefab, center, Quaternion.identity);
            floor.name = $"Floor ({room.x}, {room.y})";
            floor.transform.localScale = scale;
            roomVisuals.Add(floor);

            yield return null;
        }
    }

    IEnumerator SpawnWalls(List<Room> rooms, Dictionary<Room, List<Room>> graph)
    {
        HashSet<Vector2Int> doorPositions = new();
        foreach (GameObject go in roomVisuals)
        {
            if (go.name.StartsWith("Door"))
            {
                string[] parts = go.name.Replace("Door (", "").Replace(")", "").Split(',');
                int x = int.Parse(parts[0]);
                int y = int.Parse(parts[1]);
                doorPositions.Add(new Vector2Int(x, y));
            }
        }

        HashSet<Edge> allEdges = new();
        foreach (Room room in rooms)
        {
            for (int x = room.x; x < room.x + room.width; x++)
            {
                allEdges.Add(new Edge(x, room.y, EdgeDirection.Horizontal)); // bottom
                allEdges.Add(new Edge(x, room.y + room.height, EdgeDirection.Horizontal)); // top
            }
            for (int y = room.y; y < room.y + room.height; y++)
            {
                allEdges.Add(new Edge(room.x, y, EdgeDirection.Vertical)); // left
                allEdges.Add(new Edge(room.x + room.width, y, EdgeDirection.Vertical)); // right
            }
        }

        // Remove duplicates (shared edges)
        HashSet<Edge> uniqueEdges = new();
        foreach (Edge e in allEdges)
        {
            if (!uniqueEdges.Add(e))
                uniqueEdges.Remove(e);
        }

        // Remove edges that are where doors are
        uniqueEdges.RemoveWhere(e =>
            doorPositions.Contains(e.Position) ||
            (e.Direction == EdgeDirection.Horizontal && doorPositions.Contains(new Vector2Int(e.Position.x - 1, e.Position.y))) ||
            (e.Direction == EdgeDirection.Vertical && doorPositions.Contains(new Vector2Int(e.Position.x, e.Position.y - 1)))
        );

        // Group by axis
        var horizontalGroups = uniqueEdges.Where(e => e.Direction == EdgeDirection.Horizontal).GroupBy(e => e.Position.y);
        var verticalGroups = uniqueEdges.Where(e => e.Direction == EdgeDirection.Vertical).GroupBy(e => e.Position.x);

        foreach (var group in horizontalGroups)
            yield return StartCoroutine(SpawnWallSegments(group.OrderBy(e => e.Position.x).ToList(), true));

        foreach (var group in verticalGroups)
            yield return StartCoroutine(SpawnWallSegments(group.OrderBy(e => e.Position.y).ToList(), false));
    }

    IEnumerator SpawnWallSegments(List<Edge> edges, bool horizontal)
    {
        int segmentStart = -99999;
        int lastCoord = -99999;

        foreach (var edge in edges)
        {
            int coord = horizontal ? edge.Position.x : edge.Position.y;
            if (segmentStart == -99999)
            {
                segmentStart = coord;
            }
            else if (coord != lastCoord + 1)
            {
                yield return SpawnWallSegment(segmentStart, lastCoord + 1, horizontal, horizontal ? edge.Position.y : edge.Position.x);
                segmentStart = coord;
            }
            lastCoord = coord;
        }

        if (segmentStart != -99999)
        {
            yield return SpawnWallSegment(segmentStart, lastCoord + 1, horizontal, horizontal ? edges[0].Position.y : edges[0].Position.x);
        }
    }

    IEnumerator SpawnWallSegment(int start, int end, bool horizontal, int fixedCoord)
    {
        int length = end - start;
        if (length <= 0) yield break;

        Vector3 scale = new Vector3(length, 3f, 1f);
        Vector3 position = horizontal
            ? new Vector3((start + end) / 2f, 1.5f, fixedCoord + 0.5f)
            : new Vector3(fixedCoord + 0.5f, 1.5f, (start + end) / 2f);

        Quaternion rotation = horizontal ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);

        GameObject wall = Instantiate(wallPrefab, position, rotation);
        wall.transform.localScale = scale;
        wall.name = $"Wall ({start}-{end}, {fixedCoord})";
        roomVisuals.Add(wall);

        yield return null;
    }

    class Edge
    {
        public Vector2Int Position;
        public EdgeDirection Direction;

        public Edge(int x, int y, EdgeDirection direction)
        {
            Position = new Vector2Int(x, y);
            Direction = direction;
        }

        public override bool Equals(object obj)
        {
            if (obj is not Edge other) return false;
            return Position == other.Position && Direction == other.Direction;
        }

        public override int GetHashCode()
        {
            return Position.GetHashCode() ^ Direction.GetHashCode();
        }
    }

    enum EdgeDirection
    {
        Horizontal,
        Vertical
    }

    IEnumerator PlaceDoorsAnimated(List<Room> finalRooms, Dictionary<Room, List<Room>> finalGraph, Dictionary<Room, List<Room>> actualDoorGraph)
    {
        for (int i = 0; i < finalRooms.Count; i++)
        {
            for (int j = i + 1; j < finalRooms.Count; j++)
            {
                Room a = finalRooms[i];
                Room b = finalRooms[j];

                // Only place a door if they're connected in the graph
                if (!finalGraph[a].Contains(b)) continue;

                if (a.x == b.x + b.width || b.x == a.x + a.width)
                {
                    int yMin = Mathf.Max(a.y, b.y) + 1;
                    int yMax = Mathf.Min(a.y + a.height, b.y + b.height) - 1;
                    if (yMax > yMin)
                    {
                        int doorY = rng.Next(yMin, yMax);
                        int doorX = (a.x == b.x + b.width) ? a.x : b.x;
                        PlaceDoorOnWall(doorX, doorY, true);

                        // ✅ Log actual connection because a door was placed
                        actualDoorGraph[a].Add(b);
                        actualDoorGraph[b].Add(a);

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

                        // ✅ Log actual connection because a door was placed
                        actualDoorGraph[a].Add(b);
                        actualDoorGraph[b].Add(a);

                        yield return new WaitForSeconds(doorDelay);
                    }
                }
            }
        }
    }



    IEnumerator DrawGraphConnections()
    {
        foreach (Room a in graph.Keys)
        {
            Vector3 centerA = new Vector3(a.x + a.width / 2f, 0.06f, a.y + a.height / 2f);
            foreach (Room b in graph[a])
            {
                if (a.GetHashCode() < b.GetHashCode())
                {
                    Vector3 centerB = new Vector3(b.x + b.width / 2f, 0.06f, b.y + b.height / 2f);

                    GameObject lineObj = new GameObject("GraphEdge");
                    LineRenderer lr = lineObj.AddComponent<LineRenderer>();
                    lr.positionCount = 2;
                    lr.SetPosition(0, centerA);
                    lr.SetPosition(1, centerB);
                    lr.material = new Material(Shader.Find("Sprites/Default"));
                    lr.startColor = Color.green;
                    lr.endColor = Color.green;
                    lr.startWidth = 0.15f;
                    lr.endWidth = 0.15f;
                    roomVisuals.Add(lineObj);
                    yield return null;
                }
            }
        }
    }

    bool IsGraphConnected(Dictionary<Room, List<Room>> g)
    {
        if (g.Count == 0) return true;

        Queue<Room> queue = new Queue<Room>();
        HashSet<Room> visited = new HashSet<Room>();

        Room start = g.Keys.First();
        queue.Enqueue(start);
        visited.Add(start);

        while (queue.Count > 0)
        {
            Room current = queue.Dequeue();
            foreach (Room neighbor in g[current])
            {
                if (!visited.Contains(neighbor))
                {
                    visited.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }
        }

        return visited.Count == g.Count;
    }

    bool HasRoomBelow(Room current)
    {
        return rooms.Any(r => r != current &&
            r.x < current.x + current.width &&
            r.x + r.width > current.x &&
            r.y + r.height == current.y);
    }

    bool HasRoomLeft(Room current)
    {
        return rooms.Any(r => r != current &&
            r.y < current.y + current.height &&
            r.y + r.height > current.y &&
            r.x + r.width == current.x);
    }

    void PlaceDoorOnWall(int x, int y, bool isVertical)
    {
        Vector3 doorPos = new Vector3(x + 0.5f, 1f, y + 0.5f);
        Quaternion rotation = isVertical ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;

        // Place the door
        GameObject door = Instantiate(doorPrefab, doorPos, rotation);
        door.name = $"Door ({x},{y})";
        roomVisuals.Add(door);

        // Place the block directly above the door
        Vector3 topPos = new Vector3(doorPos.x, 2.5f, doorPos.z); // Y = 2.5 to sit above 2-unit-tall door
        GameObject top = Instantiate(doorTopPrefab, topPos, Quaternion.identity);
        top.name = $"TopOfDoor ({x},{y})";
        roomVisuals.Add(top);
    }

    void SpawnPlayerInRandomRoom()
    {
        if (rooms == null || rooms.Count == 0)
        {
            Debug.LogWarning("No rooms available to spawn the player.");
            return;
        }

        Room randomRoom = rooms[Random.Range(0, rooms.Count)];
        float centerX = randomRoom.x + randomRoom.width / 2f;
        float centerZ = randomRoom.y + randomRoom.height / 2f;
        Vector3 spawnPosition = new Vector3(centerX, 1f, centerZ); // Y = 1 to stand on floor

        // Destroy previous player if it exists
        if (spawnedPlayer != null)
        {
            Destroy(spawnedPlayer);
        }

        spawnedPlayer = Instantiate(playerPrefab, spawnPosition, Quaternion.identity);
        spawnedPlayer.name = "Player";
    }

    bool Overlap(int aStart, int aSize, int bStart, int bSize)
    {
        return Mathf.Max(aStart, bStart) < Mathf.Min(aStart + aSize, bStart + bSize);
    }
}

public class Room
{
    public int x, y, width, height;

    public Room(int x, int y, int width, int height)
    {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }
}

public class Leaf
{
    public int x, y, width, height;
    public Leaf left, right;

    public Leaf(int x, int y, int width, int height)
    {
        this.x = x; this.y = y;
        this.width = width;
        this.height = height;
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
