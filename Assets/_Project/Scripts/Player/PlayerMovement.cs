using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runs, jumps and falls. Reads input from PlayerInputReader and writes Rigidbody2D velocity
/// once per physics step. Other scripts read IsGrounded / FacingDirection and listen to
/// Jumped / Landed instead of reaching into this class.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(CapsuleCollider2D), typeof(PlayerInputReader))]
public class PlayerMovement : MonoBehaviour
{
    [Header("Run")]
    [Tooltip("Top horizontal speed in units per second.")]
    [SerializeField] private float runSpeed = 8f;
    [Tooltip("How fast speed builds up on the ground, in units/s per second.")]
    [SerializeField] private float groundAcceleration = 80f;
    [Tooltip("How fast the player stops on the ground when input is released, in units/s per second.")]
    [SerializeField] private float groundDeceleration = 90f;
    [Tooltip("Acceleration in the air. Lower than ground so jumps have some commitment.")]
    [SerializeField] private float airAcceleration = 55f;
    [Tooltip("Slow-down in the air when input is released. Low, so momentum carries through jumps.")]
    [SerializeField] private float airDeceleration = 30f;

    [Header("Jump")]
    [Tooltip("Peak height of a full (held) jump in units. 4.4 clears the 3.75 step up to the gold platform with some margin.")]
    [SerializeField] private float jumpHeight = 4.4f;
    [Tooltip("Upward speed is multiplied by this when Jump is released while rising (once per jump).")]
    [Range(0f, 1f)]
    [SerializeField] private float jumpCutMultiplier = 0.5f;
    [Tooltip("Seconds after take-off before a jump can be cut. Keeps a buffered tap as high as a normal tap.")]
    [SerializeField] private float minJumpTime = 0.08f;
    [Tooltip("Seconds after walking off a ledge in which a jump still works.")]
    [SerializeField] private float coyoteTime = 0.1f;
    [Tooltip("Seconds a jump press is remembered before landing.")]
    [SerializeField] private float jumpBufferTime = 0.12f;

    [Header("Fall")]
    [Tooltip("Gravity is multiplied by this while falling, so jumps feel snappy instead of floaty.")]
    [SerializeField] private float fallGravityMultiplier = 1.6f;
    [Tooltip("Maximum falling speed in units per second.")]
    [SerializeField] private float maxFallSpeed = 20f;

    [Header("Ground Check")]
    [Tooltip("Layers the player can stand on.")]
    [SerializeField] private LayerMask groundLayers;
    [Tooltip("Width of the check box as a fraction of the capsule width. Below 1 so touching a wall never counts as ground.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float groundCheckWidthFactor = 0.7f;
    [Tooltip("Height of the check box in units, centred on the bottom of the capsule.")]
    [SerializeField] private float groundCheckDepth = 0.1f;

    [Header("Visual")]
    [Tooltip("The child holding the sprite. Only this is flipped, never the root.")]
    [SerializeField] private Transform visual;

    // Above this, the player counts as rising, so the ground box still touching the floor
    // right after take-off doesn't count as grounded (which would allow a double jump).
    private const float RisingThreshold = 0.01f;
    // Stick values smaller than this don't change facing.
    private const float FacingThreshold = 0.1f;

    private Rigidbody2D body;
    private CapsuleCollider2D capsule;
    private PlayerInputReader input;

    private float baseGravityScale;
    private float lastGroundedTime = float.NegativeInfinity;
    private float lastJumpPressedTime = float.NegativeInfinity;
    private float jumpStartTime = float.NegativeInfinity;
    private bool hasTouchedGround;
    private bool jumpCutAvailable;
    private bool controlLocked;

    private ContactFilter2D groundFilter;
    private readonly List<Collider2D> groundHits = new List<Collider2D>(1);

    public bool IsGrounded { get; private set; }

    /// <summary>+1 when facing right, -1 when facing left. Attacks and the dagger read this.</summary>
    public int FacingDirection { get; private set; } = 1;

    public bool IsControlLocked => controlLocked;

    public event Action Jumped;
    public event Action Landed;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        capsule = GetComponent<CapsuleCollider2D>();
        input = GetComponent<PlayerInputReader>();

        // The Inspector value on the Rigidbody is the "rising" gravity; falling gravity is derived from it.
        baseGravityScale = body.gravityScale;

        groundFilter = new ContactFilter2D();
        groundFilter.SetLayerMask(groundLayers);
        groundFilter.useTriggers = false;

        ApplyFacing();
    }

    private void OnEnable()
    {
        input.JumpPressed += OnJumpPressed;
    }

    private void OnDisable()
    {
        input.JumpPressed -= OnJumpPressed;
        // Don't leave the body stuck with the stronger falling gravity while this is off.
        if (body != null)
            body.gravityScale = baseGravityScale;
    }

    private void OnJumpPressed()
    {
        // Only remembered here; FixedUpdate decides whether a jump can happen.
        lastJumpPressedTime = Time.time;
    }

    private void Update()
    {
        // Facing is visual and read by attacks, so it reacts every frame, not only per physics step.
        if (controlLocked)
            return;

        float moveX = input.MoveX;
        if (moveX > FacingThreshold && FacingDirection != 1)
        {
            FacingDirection = 1;
            ApplyFacing();
        }
        else if (moveX < -FacingThreshold && FacingDirection != -1)
        {
            FacingDirection = -1;
            ApplyFacing();
        }
    }

    private void FixedUpdate()
    {
        UpdateGrounded();

        // While locked, another component (the dodge) owns velocity and gravity.
        if (controlLocked)
            return;

        Vector2 velocity = body.linearVelocity;

        TryJump(ref velocity);
        ApplyJumpCut(ref velocity);
        ApplyGravity(velocity);
        velocity.x = MoveHorizontal(velocity.x);
        velocity.y = Mathf.Max(velocity.y, -maxFallSpeed);

        body.linearVelocity = velocity;
    }

    // ---------------------------------------------------------------- Public API

    /// <summary>
    /// While locked, this component stops writing velocity and gravity scale and ignores input,
    /// so e.g. the dodge can set its own velocity. Ground checks and Landed keep working.
    /// A jump pressed during the lock is still buffered and can fire right after unlocking.
    /// </summary>
    public void SetControlLocked(bool locked)
    {
        controlLocked = locked;
        // A jump cut after the lock ends would chop whatever velocity the dodge left behind.
        if (locked)
            jumpCutAvailable = false;
    }

    // ---------------------------------------------------------------- Steps

    private void UpdateGrounded()
    {
        bool wasGrounded = IsGrounded;
        GetGroundCheckBox(out Vector2 centre, out Vector2 size);
        bool touching = Physics2D.OverlapBox(centre, size, 0f, groundFilter, groundHits) > 0;

        IsGrounded = touching && body.linearVelocity.y <= RisingThreshold;

        if (IsGrounded)
        {
            lastGroundedTime = Time.time;
            // The first touchdown is the player settling onto the floor at scene start, not a landing.
            if (!wasGrounded && hasTouchedGround)
                Landed?.Invoke();
            hasTouchedGround = true;
        }
    }

    private void TryJump(ref Vector2 velocity)
    {
        bool jumpBuffered = Time.time - lastJumpPressedTime <= jumpBufferTime;
        bool canJump = Time.time - lastGroundedTime <= coyoteTime;
        if (!jumpBuffered || !canJump)
            return;

        // Height-based jump: from v^2 = 2 * g * h, the launch speed that reaches height h
        // under gravity g is sqrt(2 * g * h). Uses the rising gravity, because that is
        // the gravity acting on the way up (fall gravity only starts at the top).
        // Physics moves in fixed steps and applies gravity before moving, which loses about
        // half a step of gravity in height; the extra term adds it back so jumpHeight is the real peak.
        float gravity = Mathf.Abs(Physics2D.gravity.y * baseGravityScale);
        velocity.y = Mathf.Sqrt(2f * gravity * jumpHeight) + 0.5f * gravity * Time.fixedDeltaTime;

        // Consume both, so one press gives one jump and coyote time can't give a second one.
        lastJumpPressedTime = float.NegativeInfinity;
        lastGroundedTime = float.NegativeInfinity;
        jumpCutAvailable = true;
        jumpStartTime = Time.time;
        IsGrounded = false;

        Jumped?.Invoke();
    }

    // Polled every step (not on JumpReleased), so it also works if the release event was missed.
    private void ApplyJumpCut(ref Vector2 velocity)
    {
        if (!jumpCutAvailable)
            return;

        if (velocity.y <= 0f)
        {
            jumpCutAvailable = false;
            return;
        }

        // Without this, a buffered jump whose button was already released is cut in its very first step.
        if (Time.time - jumpStartTime < minJumpTime)
            return;

        if (!input.JumpHeld)
        {
            velocity.y *= jumpCutMultiplier;
            jumpCutAvailable = false;
        }
    }

    private void ApplyGravity(Vector2 velocity)
    {
        bool falling = !IsGrounded && velocity.y < 0f;
        body.gravityScale = falling ? baseGravityScale * fallGravityMultiplier : baseGravityScale;
    }

    private float MoveHorizontal(float currentX)
    {
        float target = input.MoveX * runSpeed;
        bool hasInput = Mathf.Abs(target) > 0.01f;

        float rate;
        if (IsGrounded)
            rate = hasInput ? groundAcceleration : groundDeceleration;
        else
            rate = hasInput ? airAcceleration : airDeceleration;

        return Mathf.MoveTowards(currentX, target, rate * Time.fixedDeltaTime);
    }

    private void ApplyFacing()
    {
        if (visual == null)
            return;

        Vector3 scale = visual.localScale;
        scale.x = Mathf.Abs(scale.x) * FacingDirection;
        visual.localScale = scale;
    }

    // ---------------------------------------------------------------- Ground check shape

    // Uses the body position (not the interpolated transform), so the check matches the physics pose.
    private void GetGroundCheckBox(out Vector2 centre, out Vector2 size)
    {
        Vector2 position = body != null ? body.position : (Vector2)transform.position;
        Vector2 capsuleCentre = position + capsule.offset;
        float bottom = capsuleCentre.y - capsule.size.y * 0.5f;

        centre = new Vector2(capsuleCentre.x, bottom);
        size = new Vector2(capsule.size.x * groundCheckWidthFactor, groundCheckDepth);
    }

    private void OnDrawGizmosSelected()
    {
        if (capsule == null)
            capsule = GetComponent<CapsuleCollider2D>();
        if (capsule == null)
            return;

        GetGroundCheckBox(out Vector2 centre, out Vector2 size);
        Gizmos.color = Application.isPlaying && IsGrounded ? Color.green : Color.yellow;
        Gizmos.DrawWireCube(centre, size);
    }
}
