using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Simple fly camera for the demo scene. Hold the right mouse button to look around; WASD moves,
/// Q/E go down and up, Shift speeds up, the scroll wheel changes the base speed.
/// </summary>
[RequireComponent(typeof(Camera))]
public sealed class FlyCamera : MonoBehaviour
{
    [Tooltip("Movement speed in metres per second.")]
    public float speed = 6f;
    [Tooltip("Speed multiplier while Shift is held.")]
    public float sprintMultiplier = 3f;
    [Tooltip("Degrees of turn per mouse pixel.")]
    public float lookSensitivity = 0.12f;
    [Tooltip("Look around only while the right mouse button is held.")]
    public bool holdRightMouseToLook = true;

    float m_Yaw, m_Pitch;

    void OnEnable()
    {
        var e = transform.eulerAngles;
        m_Yaw = e.y;
        m_Pitch = e.x > 180f ? e.x - 360f : e.x;
    }

    void Update()
    {
        var kb = Keyboard.current;
        var mouse = Mouse.current;
        if (kb == null) return;

        // Look.
        bool looking = mouse != null && (!holdRightMouseToLook || mouse.rightButton.isPressed);
        if (looking)
        {
            Vector2 delta = mouse.delta.ReadValue();
            m_Yaw += delta.x * lookSensitivity;
            m_Pitch = Mathf.Clamp(m_Pitch - delta.y * lookSensitivity, -89f, 89f);
            transform.rotation = Quaternion.Euler(m_Pitch, m_Yaw, 0f);
        }
        if (mouse != null) Cursor.lockState = looking && holdRightMouseToLook ? CursorLockMode.Locked : CursorLockMode.None;

        // Scroll wheel adjusts the base speed.
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) speed = Mathf.Clamp(speed * (scroll > 0f ? 1.2f : 1f / 1.2f), 0.5f, 100f);
        }

        // Move.
        Vector3 move = Vector3.zero;
        if (kb.wKey.isPressed) move += Vector3.forward;
        if (kb.sKey.isPressed) move += Vector3.back;
        if (kb.aKey.isPressed) move += Vector3.left;
        if (kb.dKey.isPressed) move += Vector3.right;
        if (kb.eKey.isPressed) move += Vector3.up;
        if (kb.qKey.isPressed) move += Vector3.down;
        if (move.sqrMagnitude > 0f)
        {
            float s = speed * (kb.leftShiftKey.isPressed || kb.rightShiftKey.isPressed ? sprintMultiplier : 1f);
            transform.position += transform.TransformDirection(move.normalized) * (s * Time.unscaledDeltaTime);
        }
    }
}
