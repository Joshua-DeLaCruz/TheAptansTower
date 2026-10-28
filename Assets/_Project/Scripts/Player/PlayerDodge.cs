using System;
using UnityEngine;

/// <summary>
/// A short, fast, flat ground dodge with invulnerability frames ("i-frames": a window in which hits
/// are ignored). The i-frames are a Dodge window on PlayerInvulnerability, which Health asks.
/// A jump cancels the dodge into a normal jump. While dodging it locks PlayerMovement, so only one
/// script writes the velocity. Other scripts read IsDodging / DodgePressTime / LastDodgeWasCancelled
/// and listen to DodgeStarted / DodgeEnded (the M3 tracker uses these) instead of reaching into this class.
/// </summary>
/// <remarks>
/// Edge cases, decided on purpose:
/// - Ground only: a dodge starts only while grounded. A press in the air is remembered for Dodge Buffer Time,
///   so pressing just before landing still dodges. This script sees IsGrounded one physics step late, so that
///   dodge starts one step after touchdown.
/// - Into a wall: physics stops the player at the wall; the dodge (and its i-frames) still runs its full time.
/// - Off a ledge: the dash stays flat until it ends, then the player falls.
/// - Jump during a dodge: cancels it at once, if a normal jump is allowed (on the ground, or within coyote
///   time after dashing off a ledge). The cancel reads the same checks PlayerMovement.TryJump uses, just before
///   it runs, so a cancel always ends in a real jump. The Dodge window stays open until the jump has opened its
///   own, so the player is never hittable in between. If no jump is allowed, the press just stays in
///   PlayerMovement's jump buffer as usual. Coyote Time must stay at 0.03 or more for the cancel to work.
/// - Jump-cancel speed: the jump keeps going the dodge's way, even with no direction held, at run speed plus
///   Jump Cancel Speed Keep of the dodge's extra speed, fading over Jump Cancel Boost Fade
///   (PlayerMovement.StartJumpCarry). Steering back or landing ends that. Holding the opposite direction at
///   the cancel skips the carry, so "dodge through, then jump back" works; it still counts as a cancel.
/// - Jump on the dodge's last step: the dash has already covered its distance, so it counts as a normal end,
///   not a cancel. The jump still carries like a cancel one step earlier, so pressing a step later doesn't
///   suddenly jump straight up.
/// - Jump and dodge at the same time: a ready jump wins and the dodge doesn't start (the press then expires,
///   because the player is in the air). So a dodge is never started and cancelled in the same step.
/// - Facing: the dodge turns the player to face the way it goes, whatever order input and movement ran in.
/// </remarks>
// Order -10: see "Execution order" in PlayerInvulnerability's remarks.
[DefaultExecutionOrder(-10)]
[RequireComponent(typeof(PlayerMovement), typeof(PlayerInputReader), typeof(Rigidbody2D))]
// Required here (optional for PlayerMovement), because i-frames are the dodge's main job.
[RequireComponent(typeof(PlayerInvulnerability))]
public class PlayerDodge : MonoBehaviour
{
    [Header("Motion")]
    [Tooltip("How far a dodge travels in units, if nothing blocks it.")]
    [Min(0f)]
    [SerializeField] private float dodgeDistance = 3.5f;
    [Tooltip("How long a dodge lasts in seconds. Speed is Distance / Duration, so tune these two instead of a speed.")]
    [Min(0.01f)]
    [SerializeField] private float dodgeDuration = 0.25f;

    [Header("Timing")]
    [Tooltip("Seconds after a dodge ends (or is cancelled) before the next one can start.")]
    [Min(0f)]
    [SerializeField] private float dodgeCooldown = 0.35f;
    [Tooltip("Seconds a dodge press is remembered while it can't start yet (cooldown, or still in the air just before landing). 0 = only the next physics step.")]
    [Min(0f)]
    [SerializeField] private float dodgeBufferTime = 0.1f;

    [Header("Invulnerability (i-frames)")]
    [Tooltip("Seconds after the dodge starts when the Dodge window opens. Kept between 0 and Invulnerable Until.")]
    [Min(0f)]
    [SerializeField] private float invulnerableFrom = 0f;
    [Tooltip("Seconds after the dodge starts when the Dodge window closes. Kept between 0 and Dodge Duration. Below the duration, the end of the dodge can still be hit (souls-like).")]
    [Min(0f)]
    [SerializeField] private float invulnerableUntil = 0.2f;

    [Header("Jump Cancel")]
    [Tooltip("How much of the dodge's extra speed (above run speed) a jump that cancels the dodge keeps. 0 = run speed in the dodge's direction, like a running jump with the direction held; 1 = the jump starts at full dodge speed. The jump keeps going the dodge's way, even with no direction held, until the player steers back or lands. Holding the opposite direction at the cancel jumps back instead.")]
    [Range(0f, 1f)]
    [SerializeField] private float jumpCancelSpeedKeep = 0.3f;
    [Tooltip("Seconds over which that extra speed fades back to run speed during the jump. Longer = the jump reaches further: with the default speeds, Keep 1 and 0.35 s land about 1 unit further than a normal running jump. Very short fades are limited by air acceleration: at Keep 1, any fade up to about 0.1 s still adds about 0.25 to 0.4 units.")]
    [Min(0f)]
    [SerializeField] private float jumpCancelBoostFade = 0.35f;

    // Stick values smaller than this count as no input, so drift can't pick the dodge direction.
    // Same value as PlayerMovement's facing threshold.
    private const float InputThreshold = 0.1f;
    // Physics time adds up in float steps that can land a hair under a round number such as 0.2;
    // this tolerance stops that from adding one extra step to the dodge or delaying the window by one.
    private const float TimeEpsilon = 0.0001f;

    private PlayerMovement movement;
    private PlayerInputReader input;
    private Rigidbody2D body;
    private PlayerInvulnerability invulnerability;

    // Counted in physics steps (not Time.time differences), so the dodge is the same at any frame rate.
    private float dodgeElapsed;
    private float lastDodgeEndTime = float.NegativeInfinity;
    private float lastDodgePressedTime = float.NegativeInfinity;
    private bool dodgeQueued;
    // True from the start of a dodge until its window has been opened, so a later From still works.
    private bool dodgeWindowPending;

    // Derived, so the dodge is tuned in distance and time.
    private float DodgeSpeed => dodgeDistance / dodgeDuration;

    /// <summary>True from the physics step a dodge starts until the step it ends.</summary>
    public bool IsDodging { get; private set; }

    /// <summary>+1 if the current (or last) dodge went right, -1 if it went left.</summary>
    public int Direction { get; private set; } = 1;

    /// <summary>
    /// Time.time of the button press that started the current (or last) dodge; negative infinity before
    /// the first dodge. Measure reaction time from this value, not from when DodgeStarted fires: the dodge
    /// starts up to one physics step after the press, or later still if the press waited in the buffer.
    /// </summary>
    public float DodgePressTime { get; private set; } = float.NegativeInfinity;

    /// <summary>
    /// True if the last dodge was cut short by a jump. Read it in DodgeEnded. False after a full dodge
    /// (also when the jump came on its last step) and when a dodge is cut short because this component is disabled.
    /// </summary>
    public bool LastDodgeWasCancelled { get; private set; }

    /// <summary>
    /// Raised in the physics step a dodge starts. For reaction time, read DodgePressTime here
    /// instead of Time.time.
    /// </summary>
    public event Action DodgeStarted;

    /// <summary>
    /// Raised when a dodge ends: after its full time, when a jump cancels it (LastDodgeWasCancelled is
    /// then true), or when it is cut short because this component is disabled.
    /// Don't lock or disable PlayerMovement from this event: on a cancel it still has to do the jump in this step.
    /// </summary>
    public event Action DodgeEnded;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        input = GetComponent<PlayerInputReader>();
        body = GetComponent<Rigidbody2D>();
        invulnerability = GetComponent<PlayerInvulnerability>();

        // RequireComponent only adds it when the dodge is added, not to a Player built before it existed.
        if (invulnerability == null)
            Debug.LogWarning("[PlayerDodge] No PlayerInvulnerability on the Player, so dodges have no i-frames. " +
                             "Rebuild the M1 test scene or add the component.", this);
    }

    // Keeps the i-frame window inside the dodge, so the Inspector values always mean what they say.
    private void OnValidate()
    {
        invulnerableUntil = Mathf.Clamp(invulnerableUntil, 0f, dodgeDuration);
        invulnerableFrom = Mathf.Clamp(invulnerableFrom, 0f, invulnerableUntil);
    }

    private void OnEnable()
    {
        input.DodgePressed += OnDodgePressed;
        movement.Jumped += OnJumped;
    }

    private void OnDisable()
    {
        input.DodgePressed -= OnDodgePressed;
        movement.Jumped -= OnJumped;
        dodgeQueued = false;

        // Never leave PlayerMovement locked or a Dodge window open while this is off.
        if (IsDodging)
            EndDodge(cancelled: false, carryIntoJump: false);
        // Also covers a cancelled dodge whose jump hasn't happened yet.
        if (invulnerability != null)
            invulnerability.Close(InvulnerabilitySource.Dodge);
    }

    private void OnDodgePressed()
    {
        // Only remembered here; FixedUpdate decides whether the dodge can start, like the jump does.
        lastDodgePressedTime = Time.time;
        dodgeQueued = true;
    }

    // TryJump opens the Jump window before raising Jumped, so closing the Dodge window here never leaves a
    // hittable moment. Only a cancelled dodge can still have its window open at this point.
    private void OnJumped()
    {
        if (invulnerability != null)
            invulnerability.Close(InvulnerabilitySource.Dodge);
    }

    private void FixedUpdate()
    {
        // Start and cancel are separate branches, so they can never happen in the same step.
        if (IsDodging)
            StepDodge();
        else
            TryStartQueuedDodge();

        // After the start attempt, so every press gets at least one try, even with a buffer of 0.
        // Also while dodging, so a press made early in a long dodge can't fire long after it was made.
        if (dodgeQueued && Time.time - lastDodgePressedTime >= dodgeBufferTime)
            dodgeQueued = false;
    }

    // Exactly the checks PlayerMovement.TryJump makes. Its ground check runs after this script and can
    // only make CanJump more true, so when this says yes, PlayerMovement really jumps in this step.
    private bool IsJumpReady()
    {
        return movement.isActiveAndEnabled && movement.HasBufferedJump && movement.CanJump;
    }

    // ---------------------------------------------------------------- Start

    private void TryStartQueuedDodge()
    {
        if (!dodgeQueued || !CanStartDodge())
            return;

        dodgeQueued = false;
        StartDodge();
    }

    private bool CanStartDodge()
    {
        // PlayerMovement gives gravity back after the dodge; without it the player would float.
        if (!movement.isActiveAndEnabled)
            return false;
        // Another system (a later attack or knockback) owns the lock; the dodge would release it early
        // when it ends. If several systems need the lock, make it a counter or a set of owners.
        if (movement.IsControlLocked)
            return false;
        if (!movement.IsGrounded)
            return false;
        // A ready jump wins: it would cancel this dodge in the very next step anyway.
        if (IsJumpReady())
            return false;
        return Time.time - lastDodgeEndTime >= dodgeCooldown;
    }

    private void StartDodge()
    {
        float moveX = input.MoveX;
        if (Mathf.Abs(moveX) > InputThreshold)
            Direction = moveX > 0f ? 1 : -1;
        else
            Direction = movement.FacingDirection;
        movement.SetFacing(Direction);

        IsDodging = true;
        LastDodgeWasCancelled = false;
        dodgeElapsed = 0f;
        dodgeWindowPending = true;
        DodgePressTime = lastDodgePressedTime;
        movement.SetControlLocked(true);
        // No gravity and no vertical speed keep the dash flat, also after running off a ledge.
        body.gravityScale = 0f;

        TryOpenDodgeWindow();
        ApplyDodgeVelocity();

        DodgeStarted?.Invoke();
    }

    // ---------------------------------------------------------------- During and end

    private void StepDodge()
    {
        dodgeElapsed += Time.fixedDeltaTime;

        // The full-time check comes first: on the last step the dash has already covered its distance,
        // so a jump there is a normal end, not a cancel. It still carries, like a cancel one step earlier.
        if (dodgeDuration - dodgeElapsed <= TimeEpsilon)
        {
            EndDodge(cancelled: false, carryIntoJump: IsJumpReady());
        }
        else if (IsJumpReady())
        {
            // PlayerMovement runs right after this script and does the jump in this same step.
            EndDodge(cancelled: true, carryIntoJump: true);
        }
        else
        {
            TryOpenDodgeWindow();
            ApplyDodgeVelocity();
        }
    }

    // Opened once Invulnerable From is reached, for the time left until Invulnerable Until.
    private void TryOpenDodgeWindow()
    {
        if (!dodgeWindowPending || dodgeElapsed + TimeEpsilon < invulnerableFrom)
            return;

        dodgeWindowPending = false;
        if (invulnerability != null)
            invulnerability.Open(InvulnerabilitySource.Dodge, invulnerableUntil - dodgeElapsed);
    }

    // Written every step, because a collision changes the velocity and the dash should keep pushing evenly.
    private void ApplyDodgeVelocity()
    {
        // The last step is usually only partly inside the duration; a full step would overshoot the distance.
        float stepFraction = Mathf.Min(1f, (dodgeDuration - dodgeElapsed) / Time.fixedDeltaTime);
        body.linearVelocity = new Vector2(Direction * DodgeSpeed * stepFraction, 0f);
    }

    // Used for every ending: full time, a jump cancel, or this component being disabled.
    // carryIntoJump arms PlayerMovement's carry for the jump that follows in this same step.
    private void EndDodge(bool cancelled, bool carryIntoJump)
    {
        IsDodging = false;
        LastDodgeWasCancelled = cancelled;
        dodgeWindowPending = false;
        lastDodgeEndTime = Time.time;

        // On a cancel the Dodge window stays open until the jump has opened its own (OnJumped closes it),
        // so there is no hittable moment in between. If no jump follows, it still ends by itself at
        // Invulnerable Until.
        if (!cancelled && invulnerability != null)
            invulnerability.Close(InvulnerabilitySource.Dodge);

        // Hand over at the speed PlayerMovement is aiming for: holding the dodge direction flows straight
        // into running, letting go stops at once. Keeping the dash speed would slide about 3 units in the air.
        // A carried jump replaces this speed at take-off; a jump back (opposite held) starts from it.
        float handOverSpeed = Mathf.Max(0f, input.MoveX * Direction) * movement.RunSpeed;
        if (body != null)
            body.linearVelocity = new Vector2(Direction * handOverSpeed, 0f);

        // Unlocking also makes PlayerMovement restore its own gravity. The carry is armed after it, because
        // PlayerMovement ignores it while locked; its TryJump then decides on it later in this same step.
        movement.SetControlLocked(false);
        if (carryIntoJump)
        {
            float extraSpeed = jumpCancelSpeedKeep * Mathf.Max(0f, DodgeSpeed - movement.RunSpeed);
            movement.StartJumpCarry(Direction, extraSpeed, jumpCancelBoostFade);
        }

        DodgeEnded?.Invoke();
    }
}
