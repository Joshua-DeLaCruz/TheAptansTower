using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// The single place the player reads input from. Other player scripts use the properties and
/// events below, so only this file knows about the Input System and its bindings.
/// </summary>
public class PlayerInputReader : MonoBehaviour
{
    // Serialized so the bindings show up (and can be changed) in the Inspector.
    [SerializeField] private InputAction move;
    [SerializeField] private InputAction jump;
    [SerializeField] private InputAction dodge;
    [SerializeField] private InputAction attack;
    [SerializeField] private InputAction throwDagger;

    [Tooltip("Logs presses and changes of the move value to the Console. For testing only.")]
    [SerializeField] private bool logInput;

    // Below this, a stick change is too small to be worth a log line.
    private const float MoveLogThreshold = 0.1f;

    private Vector2 lastLoggedMove;
    private bool subscribed;

    /// <summary>Raw move direction. Keyboard gives -1, 0 or 1 per axis; a stick gives values in between.</summary>
    public Vector2 Move { get; private set; }

    /// <summary>Horizontal part of Move. The platformer only walks left and right, so this is what movement needs.</summary>
    public float MoveX => Move.x;

    /// <summary>True while the jump button is down. Used for variable jump height.</summary>
    public bool JumpHeld => jump != null && jump.IsPressed();

    public event Action JumpPressed;
    /// <summary>
    /// Raised when the jump button is let go. Does not fire if the reader is disabled while Jump is held,
    /// so variable jump height should check JumpHeld every physics step instead of relying only on this event.
    /// </summary>
    public event Action JumpReleased;
    public event Action DodgePressed;
    public event Action AttackPressed;
    public event Action ThrowPressed;

    // ---------------------------------------------------------------- Default bindings

    // All default bindings live here, so they are easy to read and change in one place.
    private static InputAction CreateDefaultMove()
    {
        var action = new InputAction("Move", InputActionType.Value, expectedControlType: "Vector2");
        // mode=1 is Digital: diagonals on the keyboard stay at full strength (1, 1) instead of
        // being shrunk to (0.71, 0.71), so pressing W+D doesn't slow down horizontal movement.
        action.AddCompositeBinding("2DVector(mode=1)")
            .With("Up", "<Keyboard>/w")
            .With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a")
            .With("Right", "<Keyboard>/d");
        action.AddCompositeBinding("2DVector(mode=1)")
            .With("Up", "<Keyboard>/upArrow")
            .With("Down", "<Keyboard>/downArrow")
            .With("Left", "<Keyboard>/leftArrow")
            .With("Right", "<Keyboard>/rightArrow");
        action.AddBinding("<Gamepad>/leftStick");
        action.AddBinding("<Gamepad>/dpad");
        return action;
    }

    private static InputAction CreateDefaultJump() =>
        CreateButton("Jump", "<Keyboard>/space", "<Gamepad>/buttonSouth");

    private static InputAction CreateDefaultDodge() =>
        CreateButton("Dodge", "<Keyboard>/leftShift", "<Gamepad>/buttonEast");

    private static InputAction CreateDefaultAttack() =>
        CreateButton("Attack", "<Keyboard>/j", "<Mouse>/leftButton", "<Gamepad>/buttonWest");

    private static InputAction CreateDefaultThrow() =>
        CreateButton("Throw", "<Keyboard>/k", "<Mouse>/rightButton", "<Gamepad>/buttonNorth");

    private static InputAction CreateButton(string name, params string[] paths)
    {
        var action = new InputAction(name, InputActionType.Button);
        foreach (string path in paths)
            action.AddBinding(path);
        return action;
    }

    // ---------------------------------------------------------------- Unity messages

    // Called by the editor when the component is added or Reset from the context menu.
    // Reset during Play mode is not supported: Unity may swap the serialized fields before Reset
    // runs, which would leave the old actions enabled and subscribed and cause double events.
    private void Reset()
    {
        move = CreateDefaultMove();
        jump = CreateDefaultJump();
        dodge = CreateDefaultDodge();
        attack = CreateDefaultAttack();
        throwDagger = CreateDefaultThrow();
    }

    private void Awake()
    {
        // Unity creates empty actions for serialized fields when Reset never ran (for example the
        // component was added before these defaults existed). Those would silently do nothing.
        move = WithDefaultsIfEmpty(move, CreateDefaultMove);
        jump = WithDefaultsIfEmpty(jump, CreateDefaultJump);
        dodge = WithDefaultsIfEmpty(dodge, CreateDefaultDodge);
        attack = WithDefaultsIfEmpty(attack, CreateDefaultAttack);
        throwDagger = WithDefaultsIfEmpty(throwDagger, CreateDefaultThrow);
    }

    private void OnEnable()
    {
        if (subscribed)
            return;

        jump.performed += OnJumpPerformed;
        jump.canceled += OnJumpCanceled;
        dodge.performed += OnDodgePerformed;
        attack.performed += OnAttackPerformed;
        throwDagger.performed += OnThrowPerformed;

        move.Enable();
        jump.Enable();
        dodge.Enable();
        attack.Enable();
        throwDagger.Enable();
        subscribed = true;
    }

    private void OnDisable()
    {
        if (!subscribed)
            return;

        jump.performed -= OnJumpPerformed;
        jump.canceled -= OnJumpCanceled;
        dodge.performed -= OnDodgePerformed;
        attack.performed -= OnAttackPerformed;
        throwDagger.performed -= OnThrowPerformed;

        move.Disable();
        jump.Disable();
        dodge.Disable();
        attack.Disable();
        throwDagger.Disable();
        subscribed = false;

        // Otherwise a held direction would stay "stuck" while the component is off.
        Move = Vector2.zero;
    }

    // Actions created in code hold internal state until disposed; OnDisable has already disabled them.
    private void OnDestroy()
    {
        move?.Dispose();
        jump?.Dispose();
        dodge?.Dispose();
        attack?.Dispose();
        throwDagger?.Dispose();
    }

    private void Update()
    {
        // Polled instead of event-driven: movement wants the current value every frame anyway.
        Move = move.ReadValue<Vector2>();

        if (logInput && MoveChangedEnoughToLog())
        {
            Debug.Log($"[PlayerInputReader] Move {Move}", this);
            lastLoggedMove = Move;
        }
    }

    // A stick changes slightly every frame; only log real changes, but always log letting go.
    private bool MoveChangedEnoughToLog()
    {
        if (Move == lastLoggedMove)
            return false;
        if (Move == Vector2.zero)
            return true;
        return (Move - lastLoggedMove).sqrMagnitude >= MoveLogThreshold * MoveLogThreshold;
    }

    // ---------------------------------------------------------------- Callbacks

    // Named methods instead of lambdas, so OnDisable can unsubscribe them again.
    private void OnJumpPerformed(InputAction.CallbackContext context) => Raise(JumpPressed, "Jump pressed");
    private void OnJumpCanceled(InputAction.CallbackContext context) => Raise(JumpReleased, "Jump released");
    private void OnDodgePerformed(InputAction.CallbackContext context) => Raise(DodgePressed, "Dodge pressed");
    private void OnAttackPerformed(InputAction.CallbackContext context) => Raise(AttackPressed, "Attack pressed");
    private void OnThrowPerformed(InputAction.CallbackContext context) => Raise(ThrowPressed, "Throw pressed");

    private void Raise(Action inputEvent, string logMessage)
    {
        if (logInput)
            Debug.Log($"[PlayerInputReader] {logMessage}", this);
        inputEvent?.Invoke();
    }

    // Also restores the defaults if every binding of an action is deliberately removed in the Inspector.
    private static InputAction WithDefaultsIfEmpty(InputAction action, Func<InputAction> createDefault)
    {
        return action == null || action.bindings.Count == 0 ? createDefault() : action;
    }
}
