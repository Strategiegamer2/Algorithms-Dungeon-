using System.Collections.Generic;
using UnityEngine;

public class DungeonGenerator : MonoBehaviour
{
    [Header("Dungeon Settings")]
    public int dungeonWidth = 50;
    public int dungeonHeight = 50;
    public int roomMinSize = 4;
    public int roomMaxSize = 10;
    public int roomCount = 20;

    private List<Room> rooms = new List<Room>();
    private List<GameObject> roomObjects = new List<GameObject>();
    private List<Door> doors = new List<Door>();
    private Camera mainCamera;

    void Start()
    {
        mainCamera = Camera.main;
        GenerateRooms();
        PlaceDoors();
        EnsureConnectivity();
        DrawRooms();
        DrawDoors();
        AdjustCameraView();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.R)) // Press "R" to regenerate dungeon
        {
            RegenerateDungeon();
        }
    }

    void RegenerateDungeon()
    {
        ClearDungeon();
        GenerateRooms();
        PlaceDoors();
        EnsureConnectivity();
        DrawRooms();
        DrawDoors();
        AdjustCameraView();
    }

    void ClearDungeon()
    {
        foreach (GameObject obj in roomObjects)
        {
            Destroy(obj);
        }
        rooms.Clear();
        doors.Clear();
        roomObjects.Clear();
    }

    void GenerateRooms()
    {
        int xOffset = 0, yOffset = 0;
        for (int i = 0; i < roomCount; i++)
        {
            int width = Random.Range(roomMinSize, roomMaxSize + 1);
            int height = Random.Range(roomMinSize, roomMaxSize + 1);

            Room newRoom = new Room(xOffset, yOffset, width, height);

            rooms.Add(newRoom);

            xOffset += width + 1; // Move horizontally
            if (xOffset >= dungeonWidth) // Move to next row if needed
            {
                xOffset = 0;
                yOffset += height + 1;
            }
        }
    }

    void PlaceDoors()
    {
        foreach (Room roomA in rooms)
        {
            foreach (Room roomB in rooms)
            {
                if (roomA == roomB) continue;

                if (roomA.IsAdjacent(roomB, out Vector3 doorPosition))
                {
                    doors.Add(new Door(doorPosition));
                }
            }
        }
    }

    void EnsureConnectivity()
    {
        HashSet<Room> visited = new HashSet<Room>();
        Queue<Room> queue = new Queue<Room>();

        if (rooms.Count > 0)
        {
            queue.Enqueue(rooms[0]); // Start from the first room
            visited.Add(rooms[0]);
        }

        while (queue.Count > 0)
        {
            Room current = queue.Dequeue();
            foreach (Room neighbor in GetConnectedRooms(current))
            {
                if (!visited.Contains(neighbor))
                {
                    visited.Add(neighbor);
                    queue.Enqueue(neighbor);
                }
            }
        }

        if (visited.Count < rooms.Count) // Some rooms are unreachable
        {
            foreach (Room room in rooms)
            {
                if (!visited.Contains(room))
                {
                    Room closest = FindClosestConnectedRoom(room, visited);
                    if (closest != null && room.IsAdjacent(closest, out Vector3 doorPosition))
                    {
                        doors.Add(new Door(doorPosition));
                        visited.Add(room);
                    }
                }
            }
        }
    }

    List<Room> GetConnectedRooms(Room room)
    {
        List<Room> connectedRooms = new List<Room>();
        foreach (Door door in doors)
        {
            foreach (Room other in rooms)
            {
                if (room != other && other.ContainsPoint(door.position))
                {
                    connectedRooms.Add(other);
                }
            }
        }
        return connectedRooms;
    }

    Room FindClosestConnectedRoom(Room room, HashSet<Room> connectedRooms)
    {
        Room closest = null;
        float minDist = float.MaxValue;
        foreach (Room r in connectedRooms)
        {
            float distance = Vector3.Distance(new Vector3(room.x, 0, room.y), new Vector3(r.x, 0, r.y));
            if (distance < minDist)
            {
                minDist = distance;
                closest = r;
            }
        }
        return closest;
    }

    void DrawRooms()
    {
        foreach (Room room in rooms)
        {
            GameObject roomObj = new GameObject($"Room ({room.x}, {room.y})");
            LineRenderer lr = roomObj.AddComponent<LineRenderer>();

            lr.startWidth = 0.2f;
            lr.endWidth = 0.2f;
            lr.positionCount = 5;
            lr.useWorldSpace = true;

            Vector3[] points = new Vector3[]
            {
                new Vector3(room.x, 0, room.y),
                new Vector3(room.x + room.width, 0, room.y),
                new Vector3(room.x + room.width, 0, room.y + room.height),
                new Vector3(room.x, 0, room.y + room.height),
                new Vector3(room.x, 0, room.y)
            };

            lr.SetPositions(points);
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = Color.blue;
            lr.endColor = Color.blue;

            roomObjects.Add(roomObj);
        }
    }

    void DrawDoors()
    {
        foreach (Door door in doors)
        {
            GameObject doorObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
            doorObj.transform.position = door.position;
            doorObj.transform.localScale = new Vector3(0.5f, 1, 0.5f);
            doorObj.GetComponent<Renderer>().material.color = Color.red;
            roomObjects.Add(doorObj);
        }
    }

    void AdjustCameraView()
    {
        if (mainCamera == null) return;

        float minX = float.MaxValue, minY = float.MaxValue;
        float maxX = float.MinValue, maxY = float.MinValue;

        foreach (Room room in rooms)
        {
            minX = Mathf.Min(minX, room.x);
            minY = Mathf.Min(minY, room.y);
            maxX = Mathf.Max(maxX, room.x + room.width);
            maxY = Mathf.Max(maxY, room.y + room.height);
        }

        Vector3 center = new Vector3((minX + maxX) / 2, 20, (minY + maxY) / 2);
        mainCamera.transform.position = center;

        float width = maxX - minX;
        float height = maxY - minY;
        float maxDimension = Mathf.Max(width, height);

        if (mainCamera.orthographic)
        {
            mainCamera.orthographicSize = maxDimension / 2 + 5;
        }
        else
        {
            mainCamera.transform.position = new Vector3(center.x, maxDimension, center.z - maxDimension);
            mainCamera.transform.LookAt(center);
        }
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

    public bool IsAdjacent(Room other, out Vector3 doorPosition)
    {
        doorPosition = new Vector3((x + other.x) / 2, 0, (y + other.y) / 2);
        return true;
    }

    public bool ContainsPoint(Vector3 point)
    {
        return x <= point.x && point.x <= x + width && y <= point.z && point.z <= y + height;
    }
}

public class Door
{
    public Vector3 position;
    public Door(Vector3 pos) { position = pos; }
}
