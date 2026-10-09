using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class PlayerController : MonoBehaviour
{
    [Header("Movimento")]
    [Min(0)] public float moveSpeed = 4f;
    [Min(0)] public float turnSpeed = 720f;
    public float gravity = -20f;

    private CharacterController controller;
    private InputAction moveAction;
    private float verticalVelocity;

    void Awake()
    {
        controller = GetComponent<CharacterController>();

        moveAction = new InputAction("Move", InputActionType.Value);

        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");

        moveAction.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");

        moveAction.AddBinding("<Gamepad>/leftStick");
    }

    void OnEnable()
    {
        moveAction.Enable();
    }

    void OnDisable()
    {
        moveAction.Disable();
    }

    void OnDestroy()
    {
        moveAction.Dispose();
    }

    void Update()
    {
        // A câmera fica sempre voltada para +Z, então o input é relativo ao mundo.
        Vector2 input = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);
        Vector3 horizontal = new Vector3(input.x, 0f, input.y) * moveSpeed;

        if (controller.isGrounded && verticalVelocity < 0f)
            verticalVelocity = -2f;

        verticalVelocity += gravity * Time.deltaTime;

        Vector3 velocity = horizontal;
        velocity.y = verticalVelocity;
        controller.Move(velocity * Time.deltaTime);

        if (input.sqrMagnitude > 0.01f)
        {
            Quaternion target = Quaternion.LookRotation(new Vector3(input.x, 0f, input.y));
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation,
                target,
                turnSpeed * Time.deltaTime);
        }
    }
}
