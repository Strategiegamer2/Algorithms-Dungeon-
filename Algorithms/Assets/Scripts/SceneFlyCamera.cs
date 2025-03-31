using UnityEngine;

public class SceneFlyCamera : MonoBehaviour
{
    [Header("Movement")]
    public float moveSpeed = 10f;
    public float boostMultiplier = 3f;

    [Header("Rotation")]
    public float rotationSpeed = 3f;

    [Header("Zoom")]
    public float zoomSpeed = 10f;
    public float minZoom = 2f;
    public float maxZoom = 100f;

    private float yaw = 0f;
    private float pitch = 0f;

    void Start()
    {
        Vector3 angles = transform.eulerAngles;
        yaw = angles.y;
        pitch = angles.x;
        Cursor.lockState = CursorLockMode.None;
    }

    void Update()
    {
        HandleLookRotation();
        HandleMovement();
        HandleZoom();
    }

    void HandleLookRotation()
    {
        if (Input.GetMouseButton(1)) // RMB held
        {
            Cursor.lockState = CursorLockMode.Locked;
            yaw += Input.GetAxis("Mouse X") * rotationSpeed;
            pitch -= Input.GetAxis("Mouse Y") * rotationSpeed;
            pitch = Mathf.Clamp(pitch, -89f, 89f);
            transform.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
        else
        {
            Cursor.lockState = CursorLockMode.None;
        }
    }

    void HandleMovement()
    {
        float speed = moveSpeed * (Input.GetKey(KeyCode.LeftShift) ? boostMultiplier : 1f);

        Vector3 input = new Vector3(
            Input.GetAxis("Horizontal"), // A/D
            (Input.GetKey(KeyCode.E) ? 1 : 0) - (Input.GetKey(KeyCode.Q) ? 1 : 0), // Q/E up/down
            Input.GetAxis("Vertical")   // W/S
        );

        Vector3 direction = transform.TransformDirection(input);
        transform.position += direction * speed * Time.deltaTime;
    }

    void HandleZoom()
    {
        float scroll = Input.GetAxis("Mouse ScrollWheel");
        transform.position += transform.forward * scroll * zoomSpeed;
    }
}
