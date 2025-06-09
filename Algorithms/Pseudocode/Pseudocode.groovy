1. Initialize Dungeon: Set dungeonWidth, dungeonHeight, roomMinSize, roomMaxSize, maxRooms, seed

public int dungeonWidth = 50;
public int dungeonHeight = 50;
public int roomMinSize = 5;
public int roomMaxSize = 12;
public int maxRooms = 10;
public int seed = 12345; // optional random seed

2. Generate BSP Tree: Create root node covering the entire dungeon
Recursively split the space until max depth or min room size is reached

BSPNode root = new BSPNode(0, 0, dungeonWidth, dungeonHeight);
SplitNode(root, depth = 0);

void SplitNode(BSPNode node, int depth)
{
    if (depth > maxDepth || node.Width < minSize * 2 || node.Height < minSize * 2)
        return;

    bool splitHorizontally = Random.value > 0.5f;
    if (splitHorizontally)
    {
        int splitY = Random.Range(minSize, node.Height - minSize);
        node.left = new BSPNode(node.x, node.y, node.width, splitY);
        node.right = new BSPNode(node.x, node.y + splitY, node.width, node.height - splitY);
    }
    else
    {
        int splitX = Random.Range(minSize, node.Width - minSize);
        node.left = new BSPNode(node.x, node.y, splitX, node.height);
        node.right = new BSPNode(node.x + splitX, node.y, node.width - splitX, node.height);
    }

    SplitNode(node.left, depth + 1);
    SplitNode(node.right, depth + 1);
}

3. Create Rooms in Leaf Nodes: For each leaf node, generate a room with random size within bounds
Store in list of rooms

List<Room> rooms = new List<Room>();

void CreateRooms(BSPNode node)
{
    if (node.left == null && node.right == null)
    {
        int roomWidth = Random.Range(roomMinSize, node.width);
        int roomHeight = Random.Range(roomMinSize, node.height);
        int roomX = Random.Range(node.x, node.x + node.width - roomWidth);
        int roomY = Random.Range(node.y, node.y + node.height - roomHeight);

        Room room = new Room(roomX, roomY, roomWidth, roomHeight);
        node.room = room;
        rooms.Add(room);
    }
    else
    {
        if (node.left != null) CreateRooms(node.left);
        if (node.right != null) CreateRooms(node.right);
    }
}

4. Prune Small Rooms (Optional): Randomly remove some rooms based on pruning chance, keeping the graph connected

float pruneChance = 0.3f;

void PruneRooms()
{
    for (int i = rooms.Count - 1; i >= 0; i--)
    {
        if (Random.value < pruneChance)
        {
            // Ensure not isolating graph
            if (IsStillConnectedWithoutRoom(rooms[i]))
            {
                rooms.RemoveAt(i);
            }
        }
    }
}

5. Generate Graph Connections Between Rooms: Build graph from room centers
Connect nearest neighbors using Delaunay/MST or simple closest connections

Graph graph = new Graph();

void BuildGraph()
{
    foreach (Room room in rooms)
    {
        graph.AddNode(room.center);
    }

    foreach (Node a in graph.nodes)
    {
        Node b = FindNearestUnconnectedNode(a);
        if (b != null)
        {
            graph.Connect(a, b);
        }
    }
}

6. Carve Corridors: For each edge in the graph, carve a corridor between the two room centers

void CarveCorridors(Graph graph)
{
    foreach (Edge edge in graph.edges)
    {
        Vector2Int a = edge.nodeA.position;
        Vector2Int b = edge.nodeB.position;

        // Carve L-shaped corridor (first X then Y)
        for (int x = Mathf.Min(a.x, b.x); x <= Mathf.Max(a.x, b.x); x++)
        {
            floorTiles.Add(new Vector2Int(x, a.y));
        }
        for (int y = Mathf.Min(a.y, b.y); y <= Mathf.Max(a.y, b.y); y++)
        {
            floorTiles.Add(new Vector2Int(b.x, y));
        }
    }
}

7. Spawn Tiles: Place floor tiles, then walls around them (excluding door positions)

foreach (Vector2Int pos in floorTiles)
{
    Instantiate(floorPrefab, new Vector3(pos.x, 0, pos.y), Quaternion.identity);
}

// Place walls around floor where there's no adjacent floor
foreach (Vector2Int pos in floorTiles)
{
    foreach (Vector2Int offset in cardinalDirections)
    {
        Vector2Int neighbor = pos + offset;
        if (!floorTiles.Contains(neighbor))
        {
            Instantiate(wallPrefab, new Vector3(neighbor.x, 0, neighbor.y), Quaternion.identity);
        }
    }
}

8. Fill Missing Walls Post-Pruning: After pruning rooms, some expected walls may be missing.
Loop around the edges of each room:
- Store all expected wall positions.
Check which positions are unoccupied.
Place filler wall segments only where gaps exist.
Use grouping to merge adjacent gaps into longer walls.
Avoid filling entire dungeon, only wall gaps.

IEnumerator FillUnclaimedWallSegments() {
    HashSet<Vector2Int> expectedWalls = CalculateRoomEdges();
    foreach (Vector2Int pos in expectedWalls) {
        if (!IsOccupied(pos)) {
            PlaceFillerWall(pos);
        }
    }
    yield return null;
}

9. Spawn the Player in a Random Room: Choose a random room from the list of non-pruned rooms.
Generate a random coordinate within that rooms bounds.
Spawn the player prefab at the center of that tile.
Store the spawn position as the starting node for pathfinding.

void SpawnPlayerInRandomRoom() {
    Room chosenRoom = GetRandomRoom(); // from list of kept rooms
    Vector2Int spawnTile = RandomTileInsideRoom(chosenRoom);
    Vector3 spawnPos = new Vector3(spawnTile.x + 0.5f, 0, spawnTile.y + 0.5f);
    Instantiate(playerPrefab, spawnPos, Quaternion.identity);
}

10. Build the Pathfinding Grid: Initialize an empty pathfinding grid.
Loop through all objects in the scene tagged as Floor, Door, Wall, or Player.
Use their colliders to determine occupied tiles.
For each tile inside bounds, decide if its walkable:
- Wall = false
- Floor, Door, Player = true
Walls override Floors. Doors override Walls.
Store tile position and walkability in a dictionary.

public void BuildGridFromScene() {
    foreach (GameObject obj in FindObjectsOfType<GameObject>()) {
        if (!obj.activeInHierarchy) continue;
        // ... check tag, get bounds, shrink Wall bounds
        for (int x = ... ) {
            for (int z = ... ) {
                Vector2Int pos = new Vector2Int(x, z);
                bool walkable = tag != "Wall";
                // Apply priority logic
                grid[pos] = new GridNode(pos, worldPos, walkable);
            }
        }
    }
}

11. Enable Point-and-Click Movement: Listen for mouse input every frame.
When the player left-clicks:
- Raycast to the clicked tile.
- Convert hit position to grid coordinates.
- Validate that its walkable.
If valid, use A* to generate a path from the player to the clicked tile.
Move the player step-by-step along the path using `MoveTowards`.

void Update() {
    if (Input.GetMouseButtonDown(0)) {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (Physics.Raycast(ray, out RaycastHit hit)) {
            Vector2Int target = WorldToGrid(hit.point);
            if (grid[target].walkable) {
                List<Vector3> path = FindPath(start, target);
                StartCoroutine(MoveAlong(path));
            }
        }
    }
}