// BSP Dungeon Generator - Packed layout with door placement
using System.Collections.Generic;
using UnityEngine;

public class DungeonGenerator : MonoBehaviour
{
    [Header("Dungeon Settings")]
    public int dungeonWidth = 50;
    public int dungeonHeight = 50;
    public int roomMinSize = 6;
    public int roomMaxSize = 20;
    public int maxSplits = 5;

    private List<Room> rooms = new List<Room>();
    private List<GameObject> roomVisuals = new List<GameObject>();

    void Start()
    {
        DrawDungeonBounds();
        GenerateBSPDungeon();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R))
        {
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
        GenerateBSPDungeon();
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

    void GenerateBSPDungeon()
    {
        Leaf root = new Leaf(0, 0, dungeonWidth, dungeonHeight);
        List<Leaf> leaves = new List<Leaf> { root };

        bool didSplit = true;
        for (int i = 0; i < maxSplits && didSplit; i++)
        {
            didSplit = false;
            List<Leaf> newLeaves = new List<Leaf>();

            foreach (Leaf leaf in leaves)
            {
                if (leaf.left == null && leaf.right == null)
                {
                    if (leaf.Split(roomMinSize, roomMaxSize))
                    {
                        newLeaves.Add(leaf.left);
                        newLeaves.Add(leaf.right);
                        didSplit = true;
                    }
                }
            }

            leaves.AddRange(newLeaves);
        }

        root.CreateRooms(rooms);
        DrawRooms();
        PlaceDoors();
    }

    void DrawRooms()
    {
        foreach (Room room in rooms)
        {
            GameObject roomObj = new GameObject($"Room ({room.x}, {room.y})");
            roomVisuals.Add(roomObj);

            LineRenderer lr = roomObj.AddComponent<LineRenderer>();
            lr.startWidth = 0.2f;
            lr.endWidth = 0.2f;
            lr.useWorldSpace = true;

            List<Vector3> points = new List<Vector3>();

            if (room.y > 0)
                points.Add(new Vector3(room.x, 0, room.y));
            if (room.y > 0)
                points.Add(new Vector3(room.x + room.width, 0, room.y));
            if (room.x + room.width < dungeonWidth)
                points.Add(new Vector3(room.x + room.width, 0, room.y + room.height));
            if (room.y + room.height < dungeonHeight)
                points.Add(new Vector3(room.x, 0, room.y + room.height));
            if (room.x > 0)
                points.Add(new Vector3(room.x, 0, room.y));

            lr.positionCount = points.Count;
            lr.SetPositions(points.ToArray());
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = Color.blue;
            lr.endColor = Color.blue;
        }
    }

    void PlaceDoors()
    {
        for (int i = 0; i < rooms.Count; i++)
        {
            for (int j = i + 1; j < rooms.Count; j++)
            {
                Room a = rooms[i];
                Room b = rooms[j];

                if (a.x == b.x + b.width || b.x == a.x + a.width) // Vertical neighbors
                {
                    int yMin = Mathf.Max(a.y, b.y) + 1;
                    int yMax = Mathf.Min(a.y + a.height, b.y + b.height) - 1;
                    if (yMax > yMin)
                    {
                        int doorY = Random.Range(yMin, yMax);
                        int doorX = (a.x == b.x + b.width) ? a.x : b.x;
                        PlaceDoor(doorX, doorY);
                    }
                }
                else if (a.y == b.y + b.height || b.y == a.y + a.height) // Horizontal neighbors
                {
                    int xMin = Mathf.Max(a.x, b.x) + 1;
                    int xMax = Mathf.Min(a.x + a.width, b.x + b.width) - 1;
                    if (xMax > xMin)
                    {
                        int doorX = Random.Range(xMin, xMax);
                        int doorY = (a.y == b.y + b.height) ? a.y : b.y;
                        PlaceDoor(doorX, doorY);
                    }
                }
            }
        }
    }

    void PlaceDoor(int x, int y)
    {
        GameObject doorObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        doorObj.name = $"Door ({x},{y})";
        doorObj.transform.position = new Vector3(x + 0.5f, 0.1f, y + 0.5f);
        doorObj.transform.localScale = new Vector3(0.5f, 0.2f, 0.5f);
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
    public Room room;

    public Leaf(int x, int y, int width, int height)
    {
        this.x = x; this.y = y;
        this.width = width; this.height = height;
    }

    public bool Split(int minSize, int maxSize)
    {
        if (left != null || right != null) return false;

        bool splitH = Random.value > 0.5f;
        if (width > height && width / height >= 1.25f) splitH = false;
        else if (height > width && height / width >= 1.25f) splitH = true;

        int max = (splitH ? height : width) - minSize;
        if (max <= minSize) return false;

        int split = Random.Range(minSize, max);
        if (splitH)
        {
            left = new Leaf(x, y, width, split);
            right = new Leaf(x, y + split, width, height - split);
        }
        else
        {
            left = new Leaf(x, y, split, height);
            right = new Leaf(x + split, y, width - split, height);
        }

        return true;
    }

    public void CreateRooms(List<Room> rooms)
    {
        if (left != null || right != null)
        {
            left?.CreateRooms(rooms);
            right?.CreateRooms(rooms);
        }
        else
        {
            room = new Room(x, y, width, height);
            rooms.Add(room);
        }
    }
}
