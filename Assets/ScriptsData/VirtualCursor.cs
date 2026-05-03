using UnityEngine;

public class VirtualCursor : MonoBehaviour
{
    [Header("Sensitivity Settings")]
    public float normalSensitivity = 1f;
    public float anchorSensitivity = 2f;

    [HideInInspector] public float sensitivity;

    private Vector2 position;
    public Vector2 Position => position;

    void Start()
    {
        sensitivity = normalSensitivity;
        position = new Vector2(Screen.width / 2f, Screen.height / 2f);
    }

    void Update()
    {
        // 🔥 PAUSE SUPPORT — disable virtual cursor movement while paused
        if (PauseManager.IsPaused)
            return;

        float dx = Input.GetAxisRaw("Mouse X") * sensitivity;
        float dy = Input.GetAxisRaw("Mouse Y") * sensitivity;

        position.x = Mathf.Clamp(position.x + dx, 0, Screen.width);
        position.y = Mathf.Clamp(position.y + dy, 0, Screen.height);
    }

    public void SetNormalSensitivity()
    {
        sensitivity = normalSensitivity;
    }

    public void SetAnchorSensitivity()
    {
        sensitivity = anchorSensitivity;
    }

    public void ResetPosition()
    {
        position = new Vector2(Screen.width / 2f, Screen.height / 2f);
    }
}
