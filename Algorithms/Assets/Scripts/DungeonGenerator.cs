// BSP Dungeon Generator - Fix Blue Wall Overlap With Yellow Bounds, Remove Diagonal Lines
using System.Collections;
using System.Collections.Generic;
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

    private List<Room> rooms = new List<Room>();
    private List<GameObject> roomVisuals = new List<GameObject>();
    private List<Leaf> activeLeaves = new List<Leaf>();
    private Dictionary<Room, List<Room>> graph = new Dictionary<Room, List<Room>>();

    void Start()
    {
        DrawDungeonBounds();
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
        {
            Destroy(go);
        }
        roomVisuals.Clear();
        rooms.Clear();
        activeLeaves.Clear();
        graph.Clear();
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
        Leaf root = new Leaf(0, 0, dungeonWidth, dungeonHeight);
        activeLeaves.Add(root);

        Queue<Leaf> queue = new Queue<Leaf>();
        queue.Enqueue(root);

        while (queue.Count > 0)
        {
            Leaf current = queue.Dequeue();
            bool splitSuccess = false;

            if (current.height >= roomMinSize * 2)
                splitSuccess = current.Split(true, roomMinSize);
            else if (current.width >= roomMinSize * 2)
                splitSuccess = current.Split(false, roomMinSize);

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

        yield return StartCoroutine(DrawRoomsAnimated());
        yield return StartCoroutine(PlaceDoorsAnimated());
        yield return StartCoroutine(DrawGraphConnections());
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

    IEnumerator PlaceDoorsAnimated()
    {
        for (int i = 0; i < rooms.Count; i++)
        {
            for (int j = i + 1; j < rooms.Count; j++)
            {
                Room a = rooms[i];
                Room b = rooms[j];

                if (a.x == b.x + b.width || b.x == a.x + a.width)
                {
                    int yMin = Mathf.Max(a.y, b.y) + 1;
                    int yMax = Mathf.Min(a.y + a.height, b.y + b.height) - 1;
                    if (yMax > yMin)
                    {
                        int doorY = Random.Range(yMin, yMax);
                        int doorX = (a.x == b.x + b.width) ? a.x : b.x;
                        PlaceDoorOnWall(doorX, doorY, true);
                        graph[a].Add(b);
                        graph[b].Add(a);
                        yield return new WaitForSeconds(doorDelay);
                    }
                }
                else if (a.y == b.y + b.height || b.y == a.y + a.height)
                {
                    int xMin = Mathf.Max(a.x, b.x) + 1;
                    int xMax = Mathf.Min(a.x + a.width, b.x + b.width) - 1;
                    if (xMax > xMin)
                    {
                        int doorX = Random.Range(xMin, xMax);
                        int doorY = (a.y == b.y + b.height) ? a.y : b.y;
                        PlaceDoorOnWall(doorX, doorY, false);
                        graph[a].Add(b);
                        graph[b].Add(a);
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

    void PlaceDoorOnWall(int x, int y, bool isVertical)
    {
        GameObject doorObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        doorObj.name = $"Door ({x},{y})";
        doorObj.transform.position = new Vector3(x + (isVertical ? 0f : 0.5f), 0.05f, y + (isVertical ? 0.5f : 0f));
        doorObj.transform.localScale = new Vector3(isVertical ? 0.1f : 0.6f, 0.1f, isVertical ? 0.6f : 0.1f);
        doorObj.GetComponent<MeshRenderer>().material.color = Color.red;
        roomVisuals.Add(doorObj);
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

    public bool Split(bool horizontal, int minSize)
    {
        if (horizontal)
        {
            if (height < minSize * 2) return false;
            int split = Random.Range(minSize, height - minSize);
            left = new Leaf(x, y, width, split);
            right = new Leaf(x, y + split, width, height - split);
        }
        else
        {
            if (width < minSize * 2) return false;
            int split = Random.Range(minSize, width - minSize);
            left = new Leaf(x, y, split, height);
            right = new Leaf(x + split, y, width - split, height);
        }
        return true;
    }
}
