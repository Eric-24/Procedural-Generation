using UnityEngine;
using UnityEngine.InputSystem;
public class FreeFlyCam : MonoBehaviour
{
    public float moveSpeed = 50f;
    public float lookSpeed = 0.2f;
    private float pitch = 0f;
    private float yaw = 0f;

    void Update()
    {
        if (Keyboard.current == null || Mouse.current == null) return;

        if (Mouse.current.rightButton.isPressed)
        {
            Vector2 mouseDelta = Mouse.current.delta.ReadValue();
            yaw += mouseDelta.x * lookSpeed;
            pitch -= mouseDelta.y * lookSpeed;
            pitch = Mathf.Clamp(pitch, -90f, 90f);
            transform.eulerAngles = new Vector3(pitch, yaw, 0f);
        }

        Vector3 move = Vector3.zero;
        if (Keyboard.current.wKey.isPressed) move += transform.forward;
        if (Keyboard.current.sKey.isPressed) move -= transform.forward;
        if (Keyboard.current.dKey.isPressed) move += transform.right;
        if (Keyboard.current.aKey.isPressed) move -= transform.right;
        if (Keyboard.current.eKey.isPressed) move += transform.up;
        if (Keyboard.current.qKey.isPressed) move -= transform.up;

        float currentSpeed = Keyboard.current.leftShiftKey.isPressed ? moveSpeed * 3f : moveSpeed;
        transform.position += move.normalized * currentSpeed * Time.deltaTime;
    }
}
