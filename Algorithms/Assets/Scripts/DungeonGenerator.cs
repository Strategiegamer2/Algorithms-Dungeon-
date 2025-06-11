using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.VisualScripting;
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
    private List<Room> allCandidateRooms = new List<Room>();
    private List<GameObject> splitVisuals = new();

    private Transform roomContainer;
    private Transform floorContainer;
    private Transform wallContainer;
    private Transform doorContainer;
    private Transform graphContainer;
    private Transform playerContainer;


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

        // Destroy previous dungeon visual tree
        GameObject previous = GameObject.Find("DungeonVisuals");
        if (previous != null)
            Destroy(previous);

        // Clear pathfinding grid & nav mesh
        FindObjectOfType<PathfindingManager>()?.ClearNavMesh();

        // Clear internal state
        roomVisuals.Clear();
        splitVisuals.Clear();
        rooms.Clear();
        activeLeaves.Clear();
        graph.Clear();
        allCandidateRooms.Clear();

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
        roomContainer = new GameObject("Rooms").transform;
        floorContainer = new GameObject("Floors").transform;
        wallContainer = new GameObject("Walls").transform;
        doorContainer = new GameObject("Doors").transform;
        graphContainer = new GameObject("GraphEdges").transform;

        Transform master = new GameObject("DungeonVisuals").transform;
        roomContainer.parent = master;
        floorContainer.parent = master;
        wallContainer.parent = master;
        doorContainer.parent = master;
        graphContainer.parent = master;


        bool connected = false;
        int attempt = 0;
        int maxAttempts = 3;

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

                // Binary Space Partitioning (BSP): Recursively splits leaf nodes horizontally or vertically
                // This is a divide-and-conquer approach to generate room spaces efficiently
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

            yield return StartCoroutine(DrawRoomsAnimated(activeLeaves));

            // Sort rooms by area (smallest first) to prioritize pruning small rooms
            // Step 1: Sort by area (smallest first)
            List<Room> sortedByArea = rooms.OrderBy(r => r.width * r.height).ToList();

            // Step 2: Calculate how many to prune
            int countToRemove = Mathf.FloorToInt(sortedByArea.Count * (percentRoomsToRemove / 100f));

            // Step 3: Select rooms to remove
            List<Room> candidatesToRemove = sortedByArea.Take(countToRemove).ToList();

            Debug.Log($"Pruned {candidatesToRemove.Count} out of {rooms.Count} rooms ({(100f * candidatesToRemove.Count / rooms.Count):0.#}%)");

            // Step 4: Keep the rest
            List<Room> remainingRooms = sortedByArea.Skip(countToRemove).ToList();

            if (countToRemove == 0)
                continue;

            foreach (Room pruned in candidatesToRemove)
            {
                GameObject prunedParentObj = new GameObject($"PrunedRoom ({pruned.x}, {pruned.y})");
                prunedParentObj.transform.parent = roomContainer;

                // White filled area (Quad)
                GameObject fill = GameObject.CreatePrimitive(PrimitiveType.Quad);
                fill.name = "WhiteFill";
                fill.transform.parent = prunedParentObj.transform;
                fill.transform.position = new Vector3(pruned.x + pruned.width / 2f, 0.005f, pruned.y + pruned.height / 2f);
                fill.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // Make it face up
                fill.transform.localScale = new Vector3(pruned.width, pruned.height, 1f);

                // Optional: make sure it's using a plain white material
                Material whiteMat = new Material(Shader.Find("Unlit/Color"));
                whiteMat.color = Color.white;
                fill.GetComponent<MeshRenderer>().material = whiteMat;

                // Black outline
                GameObject outline = new GameObject("Outline");
                outline.transform.parent = prunedParentObj.transform;

                LineRenderer lr = outline.AddComponent<LineRenderer>();
                lr.positionCount = 5;
                lr.useWorldSpace = true;
                lr.material = new Material(Shader.Find("Sprites/Default"));
                lr.startColor = lr.endColor = Color.black;
                lr.startWidth = lr.endWidth = 0.15f;

                Vector3[] corners = new Vector3[]
                {
                    new Vector3(pruned.x, 0.01f, pruned.y),
                    new Vector3(pruned.x + pruned.width, 0.01f, pruned.y),
                    new Vector3(pruned.x + pruned.width, 0.01f, pruned.y + pruned.height),
                    new Vector3(pruned.x, 0.01f, pruned.y + pruned.height),
                    new Vector3(pruned.x, 0.01f, pruned.y)
                };
                lr.SetPositions(corners);
            }


            allCandidateRooms = new List<Room>(rooms); // Store all rooms before pruning

            // Use remainingRooms instead of sorting rooms again
            remainingRooms.Sort((a, b) => {
                int ay = a.y + a.height;
                int by = b.y + b.height;
                if (ay != by) return by.CompareTo(ay);
                return a.x.CompareTo(b.x);
            });

            // Adjacency list used for sparse graph; efficient for dungeon connectivity checks.
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
                Debug.Log("Could not prune rooms while maintaining connectivity. Retrying...");

                // Clean up failed pruned visuals
                List<GameObject> toDestroy = new();
                foreach (Transform child in roomContainer)
                {
                    if (child.name.StartsWith("PrunedRoom"))
                    {
                        toDestroy.Add(child.gameObject);
                    }
                }
                foreach (var go in toDestroy)
                {
                    Destroy(go);
                }

                continue;
            }


            // O(n²) pairwise comparison of rooms to build an adjacency graph
            // Two rooms are connected if they touch horizontally or vertically with overlapping edges
            // This could be optimized with spatial partitioning, but n is small so this is fine
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

            Dictionary<Room, List<Room>> actualDoorGraph = new();
            foreach (Room r in finalRooms)
                actualDoorGraph[r] = new List<Room>();

            List<Vector2Int> doorPositions = new();
            yield return StartCoroutine(BuildGraphAnimated(finalRooms, finalGraph, actualDoorGraph, doorPositions));

            rooms = remainingRooms.Where(r => actualDoorGraph.ContainsKey(r)).ToList();
            graph = actualDoorGraph;
            connected = IsGraphConnected(graph);


            // Now check if the graph is connected
            connected = IsGraphConnected(graph);

            if (connected)
            {
                Debug.Log("Dungeon generated with full connectivity.");

                foreach (GameObject split in splitVisuals)
                {
                    Destroy(split);
                }
                splitVisuals.Clear();

                yield return StartCoroutine(SpawnFloorTiles());

                // Remove leaf outlines (yellow)
                foreach (GameObject obj in roomVisuals.ToList())
                {
                    if (obj.name.StartsWith("Leaf"))
                    {
                        Destroy(obj);
                        roomVisuals.Remove(obj);
                    }
                }

                yield return StartCoroutine(PlaceDoorsVisuals(doorPositions));
                yield return StartCoroutine(SpawnWalls(rooms, graph));
                yield return StartCoroutine(FillUnclaimedWallSegments());
                yield return StartCoroutine(DrawGraphConnections());
                SpawnPlayerInRandomRoom();
                FindObjectOfType<PathfindingManager>()?.BuildGridFromScene();
            }
            else
            {
                Debug.Log("Generated dungeon was not connected. Retrying...");
                yield return null;
            }
        }
    }

    IEnumerator DrawRoomsAnimated(List<Leaf> leafs)
    {
        foreach (Leaf leaf in leafs)
        {
            GameObject leafObj = new GameObject($"Leaf ({leaf.x}, {leaf.y})");
            leafObj.transform.parent = roomContainer;
            roomVisuals.Add(leafObj);

            LineRenderer lr = leafObj.AddComponent<LineRenderer>();
            lr.startWidth = 0.2f;
            lr.endWidth = 0.2f;
            lr.useWorldSpace = true;
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = Color.yellow;
            lr.endColor = Color.yellow;

            Vector3[] corners = new Vector3[]
            {
            new Vector3(leaf.x, 0, leaf.y),
            new Vector3(leaf.x + leaf.width, 0, leaf.y),
            new Vector3(leaf.x + leaf.width, 0, leaf.y + leaf.height),
            new Vector3(leaf.x, 0, leaf.y + leaf.height),
            new Vector3(leaf.x, 0, leaf.y)
            };

            lr.positionCount = corners.Length;
            lr.SetPositions(corners);

            yield return new WaitForSeconds(lineDrawDelay); // Only once per room
        }
    }

    IEnumerator SpawnFloorTiles()
    {
        foreach (Room room in rooms)
        {
            Vector3 center = new Vector3(room.x + room.width / 2f, 0f, room.y + room.height / 2f);
            Vector3 scale = new Vector3(room.width, 0.1f, room.height);

            GameObject floor = Instantiate(floorPrefab, center, Quaternion.identity, floorContainer);
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
            if (!go.name.StartsWith("Door")) continue;

            string[] parts = go.name.Replace("Door (", "").Replace(")", "").Split(',');
            int x = int.Parse(parts[0]);
            int y = int.Parse(parts[1]);

            // Extra validation: is this door between two active rooms?
            Vector2Int doorPos = new Vector2Int(x, y);
            bool isValid = false;
            foreach (Room room in rooms)
            {
                if (x >= room.x && x < room.x + room.width &&
                    y >= room.y && y < room.y + room.height)
                {
                    isValid = true;
                    break;
                }
            }

            if (isValid)
                doorPositions.Add(doorPos);
        }


        HashSet<Vector2Int> wallOccupied = new();

        foreach (Room room in rooms)
        {
            // Only place top and right walls (standardized)
            yield return StartCoroutine(SpawnWallLine(room.x, room.x + room.width, room.y + room.height, true, doorPositions, wallOccupied)); // top
            yield return StartCoroutine(SpawnWallLine(room.y, room.y + room.height, room.x + room.width, false, doorPositions, wallOccupied)); // right

            // Only place bottom if no other room adjacent below
            if (!rooms.Any(r => r != room &&
                r.x < room.x + room.width &&
                r.x + r.width > room.x &&
                r.y + r.height == room.y))
            {
                yield return StartCoroutine(SpawnWallLine(room.x, room.x + room.width, room.y, true, doorPositions, wallOccupied)); // bottom
            }


            // Only place left if no other room adjacent on the left
            if (!rooms.Any(r => r != room &&
                r.y < room.y + room.height &&
                r.y + r.height > room.y &&
                r.x + r.width == room.x))
            {
                yield return StartCoroutine(SpawnWallLine(room.y, room.y + room.height, room.x, false, doorPositions, wallOccupied)); // left
            }

            yield return null;
        }
    }


    IEnumerator SpawnWallLine(int start, int end, int fixedCoord, bool horizontal, HashSet<Vector2Int> doorPositions, HashSet<Vector2Int> wallOccupied)
    {
        int segmentStart = -1;

        for (int i = start; i <= end; i++)
        {
            Vector2Int pos = horizontal
                ? new Vector2Int(i, fixedCoord)
                : new Vector2Int(fixedCoord, i);

            bool isDoor = doorPositions.Contains(pos);
            bool isTaken = wallOccupied.Contains(pos);

            if (!isDoor && !isTaken && segmentStart == -1)
            {
                segmentStart = i;
            }
            else if ((isDoor || isTaken) && segmentStart != -1)
            {
                int segmentEnd = (isDoor || isTaken) ? i : i + 1;
                int length = segmentEnd - segmentStart;

                if (length > 0)
                {
                    // Mark positions as occupied
                    for (int j = segmentStart; j < segmentEnd; j++)
                    {
                        Vector2Int p = horizontal ? new Vector2Int(j, fixedCoord) : new Vector2Int(fixedCoord, j);
                        wallOccupied.Add(p);
                    }

                    // Place wall
                    Vector3 position = horizontal
                        ? new Vector3((segmentStart + segmentEnd) / 2f, 1.5f, fixedCoord + 0.5f)
                        : new Vector3(fixedCoord + 0.5f, 1.5f, (segmentStart + segmentEnd) / 2f);

                    Quaternion rotation = horizontal ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
                    Vector3 scale = new Vector3(length, 3f, 1f);

                    GameObject wall = Instantiate(wallPrefab, position, rotation, wallContainer);
                    wall.transform.localScale = scale;
                    wall.name = $"Wall ({segmentStart}-{segmentEnd}, {fixedCoord})";
                    roomVisuals.Add(wall);
                }

                segmentStart = -1;
            }
        }

        if (segmentStart != -1 && segmentStart < end)
        {
            int segmentEnd = end;
            int length = segmentEnd - segmentStart;

            if (length > 0)
            {
                for (int j = segmentStart; j < segmentEnd; j++)
                {
                    Vector2Int p = horizontal ? new Vector2Int(j, fixedCoord) : new Vector2Int(fixedCoord, j);
                    wallOccupied.Add(p);
                }

                Vector3 position = horizontal
                    ? new Vector3((segmentStart + segmentEnd) / 2f, 1.5f, fixedCoord + 0.5f)
                    : new Vector3(fixedCoord + 0.5f, 1.5f, (segmentStart + segmentEnd) / 2f);

                Quaternion rotation = horizontal ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
                Vector3 scale = new Vector3(length, 3f, 1f);

                GameObject wall = Instantiate(wallPrefab, position, rotation, wallContainer);
                wall.transform.localScale = scale;
                wall.name = $"Wall ({segmentStart}-{segmentEnd}, {fixedCoord})";
                roomVisuals.Add(wall);
            }
        }

        yield return null;
    }

    IEnumerator BuildGraphAnimated(List<Room> finalRooms, Dictionary<Room, List<Room>> finalGraph, Dictionary<Room, List<Room>> actualDoorGraph, List<Vector2Int> doorPositions)
    {
        for (int i = 0; i < finalRooms.Count; i++)
        {
            for (int j = i + 1; j < finalRooms.Count; j++)
            {
                Room roomA = finalRooms[i];
                Room roomB = finalRooms[j];

                if (!finalGraph[roomA].Contains(roomB)) continue;

                if (roomA.x == roomB.x + roomB.width || roomB.x == roomA.x + roomA.width)
                {
                    int yMin = Mathf.Max(roomA.y, roomB.y) + 1;
                    int yMax = Mathf.Min(roomA.y + roomA.height, roomB.y + roomB.height) - 1;
                    if (yMax > yMin)
                    {
                        int doorY = rng.Next(yMin, yMax);
                        int doorX = (roomA.x == roomB.x + roomB.width) ? roomA.x : roomB.x;
                        actualDoorGraph[roomA].Add(roomB);
                        actualDoorGraph[roomB].Add(roomA);
                        doorPositions.Add(new Vector2Int(doorX, doorY)); // Save for later
                        yield return new WaitForSeconds(doorDelay);
                    }
                }
                else if (roomA.y == roomB.y + roomB.height || roomB.y == roomA.y + roomA.height)
                {
                    int xMin = Mathf.Max(roomA.x, roomB.x) + 1;
                    int xMax = Mathf.Min(roomA.x + roomA.width, roomB.x + roomB.width) - 1;
                    if (xMax > xMin)
                    {
                        int doorX = rng.Next(xMin, xMax);
                        int doorY = (roomA.y == roomB.y + roomB.height) ? roomA.y : roomB.y;
                        actualDoorGraph[roomA].Add(roomB);
                        actualDoorGraph[roomB].Add(roomA);
                        doorPositions.Add(new Vector2Int(doorX, doorY)); // Save for later
                        yield return new WaitForSeconds(doorDelay);
                    }
                }
            }
        }
    }

    IEnumerator PlaceDoorsVisuals(List<Vector2Int> doorPositions)
    {
        foreach (Vector2Int pos in doorPositions)
        {
            // Determine direction (basic assumption — you may want to store it in step 1 if needed)
            bool isVertical = false; // You can improve this with better logic later if needed
            PlaceDoorOnWall(pos.x, pos.y, isVertical);
            yield return new WaitForSeconds(doorDelay);
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
                    lineObj.transform.parent = graphContainer;
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
        StartCoroutine(HideGraphEdgesAfterDelay(3f));
    }
    IEnumerator HideGraphEdgesAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);

        if (graphContainer != null)
        {
            Destroy(graphContainer.gameObject);
        }
    }

    IEnumerator FillUnclaimedWallSegments()
    {
        HashSet<Vector2Int> wallOccupied = new();
        HashSet<Vector2Int> doorPositions = new();
        HashSet<Vector2Int> expectedWalls = new();

        // Collect existing wall & door positions
        foreach (GameObject gameObject in roomVisuals)
        {
            if (!gameObject.name.StartsWith("Wall") && !gameObject.name.StartsWith("Door")) continue;

            int start = gameObject.name.IndexOf('(');
            int end = gameObject.name.IndexOf(')');

            if (start == -1 || end == -1 || end <= start) continue;

            string[] parts = gameObject.name.Substring(start + 1, end - start - 1).Split(',');
            if (parts.Length != 2) continue;

            if (!int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y)) continue;

            Vector2Int pos = new(x, y);
            if (gameObject.name.StartsWith("Wall")) wallOccupied.Add(pos);
            if (gameObject.name.StartsWith("Door")) doorPositions.Add(pos);
        }

        // Determine all expected wall edges from room bounds
        foreach (Room room in rooms)
        {
            int x0 = room.x;
            int x1 = room.x + room.width;
            int y0 = room.y;
            int y1 = room.y + room.height;

            for (int x = x0; x < x1; x++)
            {
                expectedWalls.Add(new Vector2Int(x, y0)); // bottom
                expectedWalls.Add(new Vector2Int(x, y1)); // top
            }

            for (int y = y0; y < y1; y++)
            {
                expectedWalls.Add(new Vector2Int(x0, y)); // left
                expectedWalls.Add(new Vector2Int(x1, y)); // right
            }
        }

        // Horizontal pass (z = fixed)
        for (int z = 0; z <= dungeonHeight; z++)
        {
            int segmentStart = -1;
            for (int x = 0; x <= dungeonWidth; x++)
            {
                Vector2Int pos = new(x, z);
                bool shouldFill = expectedWalls.Contains(pos) &&
                                  !wallOccupied.Contains(pos) &&
                                  !doorPositions.Contains(pos) &&
                                  !IsWallAlreadyPresent(pos);

                if (shouldFill && segmentStart == -1)
                {
                    segmentStart = x;
                }
                else if ((!shouldFill || x == dungeonWidth) && segmentStart != -1)
                {
                    int segmentEnd = (shouldFill && x == dungeonWidth) ? x + 1 : x;
                    PlaceWallSegment(new Vector2Int(segmentStart, z), segmentEnd - segmentStart, true);
                    segmentStart = -1;
                }
            }
        }

        // Vertical pass (x = fixed)
        for (int x = 0; x <= dungeonWidth; x++)
        {
            int segmentStart = -1;
            for (int z = 0; z <= dungeonHeight; z++)
            {
                Vector2Int pos = new(x, z);
                bool shouldFill = expectedWalls.Contains(pos) &&
                                  !wallOccupied.Contains(pos) &&
                                  !doorPositions.Contains(pos) &&
                                  !IsWallAlreadyPresent(pos);

                if (shouldFill && segmentStart == -1)
                {
                    segmentStart = z;
                }
                else if ((!shouldFill || z == dungeonHeight) && segmentStart != -1)
                {
                    int segmentEnd = (shouldFill && z == dungeonHeight) ? z + 1 : z;
                    PlaceWallSegment(new Vector2Int(x, segmentStart), segmentEnd - segmentStart, false);
                    segmentStart = -1;
                }
            }
        }

        yield return null;

        // Check the scene for any GameObject tagged "Wall" near that position
        bool IsWallAlreadyPresent(Vector2Int pos)
        {
            Vector3 center = new Vector3(pos.x + 0.5f, 1.5f, pos.y + 0.5f);
            float radius = 0.4f;

            Collider[] hits = Physics.OverlapBox(center, new Vector3(radius, 1.5f, radius));
            foreach (var hit in hits)
            {
                if (hit.CompareTag("Wall"))
                    return true;
            }
            return false;
        }

        void PlaceWallSegment(Vector2Int start, int length, bool horizontal)
        {
            if (length <= 0) return;

            Vector3 position = horizontal
                ? new Vector3(start.x + length / 2f, 1.5f, start.y + 0.5f)
                : new Vector3(start.x + 0.5f, 1.5f, start.y + length / 2f);

            Quaternion rotation = horizontal ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
            Vector3 scale = new Vector3(length, 3f, 1f);

            GameObject wall = Instantiate(wallPrefab, position, rotation, wallContainer);
            wall.transform.localScale = scale;
            wall.name = $"Wall (fill merged) ({start.x},{start.y})";
            wall.tag = "Wall"; // make sure it's tagged
            roomVisuals.Add(wall);

            // Track it to prevent overlapping next time
            for (int i = 0; i < length; i++)
            {
                Vector2Int pos = horizontal
                    ? new Vector2Int(start.x + i, start.y)
                    : new Vector2Int(start.x, start.y + i);
                wallOccupied.Add(pos);
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


    void PlaceDoorOnWall(int x, int y, bool isVertical)
    {
        Vector3 doorPos = new Vector3(x + 0.5f, 1f, y + 0.5f);
        Quaternion rotation = isVertical ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;

        // Place the door
        GameObject door = Instantiate(doorPrefab, doorPos, rotation, doorContainer);
        door.name = $"Door ({x},{y})";
        roomVisuals.Add(door);

        // Place the block directly above the door
        Vector3 topPos = new Vector3(doorPos.x, 2.5f, doorPos.z); // Y = 2.5 to sit above 2-unit-tall door
        GameObject top = Instantiate(doorTopPrefab, topPos, Quaternion.identity, wallContainer);
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