using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.Serialization;

/// <summary>
/// Follows a target sideways with a small dead zone, inside the level's left and right edges.
/// Vertically it ignores jumps and only moves after a landing, and it rises only part of the way
/// when the target climbs, so the ground below stays in view longer (drops are followed fully).
/// Whatever those rules say, the target is always kept a margin away from the top and bottom edges.
/// </summary>
/// <remarks>
/// With Grid Snapping set to None, PixelPerfectCamera snaps the rendered view to screen pixels
/// through the camera matrix; it never moves transform.position. If sprites shimmer while the
/// camera moves, try Grid Snapping "Upscale Render Texture" later.
/// </remarks>
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("What the camera follows, usually the Player.")]
    [SerializeField] private Transform target;
    [Tooltip("Camera position relative to the target, in units. Positive Y shows more above the player.")]
    [SerializeField] private Vector2 offset = new Vector2(0f, 2f);

    [Header("Horizontal Follow")]
    [Tooltip("Roughly the seconds the camera takes to catch up sideways. 0 = locked to the target.")]
    [Min(0f)]
    [FormerlySerializedAs("smoothTime")]
    [SerializeField] private float horizontalSmoothTime = 0.15f;
    [Tooltip("How far (in units) the target can move left or right before the camera follows. Stops small steps from shaking the view.")]
    [Min(0f)]
    [SerializeField] private float horizontalDeadZone = 0.5f;

    [Header("Vertical Follow")]
    [Tooltip("The target's PlayerMovement, used to tell standing from jumping. Found on the target if left empty. Without one (or while it is disabled), the camera follows the live height, jumps included.")]
    [SerializeField] private PlayerMovement targetMovement;
    [Tooltip("How much of a climb above the starting height the camera follows. 0 = stays at the starting height, 1 = follows every platform fully. Drops below the starting height are always followed fully.")]
    [Range(0f, 1f)]
    [SerializeField] private float verticalFollowStrength = 0.35f;
    [Tooltip("Roughly the seconds the camera takes to settle up or down after a landing. Higher feels calmer. 0 = moves at once.")]
    [Min(0f)]
    [SerializeField] private float verticalSmoothTime = 0.35f;
    [Tooltip("The camera always keeps the target's centre at least this many units from the top and bottom screen edges, even mid-jump. Overrides the other vertical rules.")]
    [Min(0f)]
    [SerializeField] private float screenEdgeMargin = 1.5f;

    [Header("Horizontal Clamp")]
    [Tooltip("Keep the view between Level Min X and Level Max X.")]
    [SerializeField] private bool clampHorizontal = true;
    [Tooltip("Left edge of the level in world units. The screen's left edge never goes past this.")]
    [SerializeField] private float levelMinX = -21f;
    [Tooltip("Right edge of the level in world units. The screen's right edge never goes past this.")]
    [SerializeField] private float levelMaxX = 21f;

    private Camera cam;
    private PixelPerfectCamera pixelPerfect;
    private float focusX;
    // The target's height the last time it stood on the ground, so jumps don't move the camera.
    private float groundY;
    // The first ground height after Start; climbs above it are only partly followed.
    private float baseY;
    private bool hasBaseY;
    private bool landedSinceLastFrame;
    private float velocityX;
    private float velocityY;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        pixelPerfect = GetComponent<PixelPerfectCamera>();
        if (targetMovement == null && target != null)
            target.TryGetComponent(out targetMovement);
        if (targetMovement == null)
            Debug.LogWarning("[CameraFollow] No PlayerMovement found on the target, so the camera will follow jumps too.", this);
    }

    // Awake runs before OnEnable, so a PlayerMovement found there is already set here.
    private void OnEnable()
    {
        if (targetMovement != null)
            targetMovement.Landed += OnTargetLanded;
    }

    private void OnDisable()
    {
        if (targetMovement != null)
            targetMovement.Landed -= OnTargetLanded;
    }

    private void OnTargetLanded()
    {
        landedSinceLastFrame = true;
    }

    private void Start()
    {
        if (target == null)
            return;

        // Start on the target instead of swooping in from wherever the camera was placed.
        // Until the target first stands on the ground, its live height is both ground and base.
        focusX = target.position.x;
        groundY = baseY = target.position.y;
        float y = KeepTargetInMargin(GetDesiredY());
        transform.position = new Vector3(GetDesiredX(), y, transform.position.z);
    }

    // LateUpdate, so the (interpolated) player has already moved this frame.
    private void LateUpdate()
    {
        // Mathf.SmoothDamp divides by deltaTime when it is already on target, which gives NaN
        // while the game is paused (timeScale 0). Nothing moves then anyway.
        if (target == null || Time.deltaTime <= 0f)
            return;

        UpdateFocusX();
        UpdateGroundY();

        Vector3 position = transform.position;
        position.x = Mathf.SmoothDamp(position.x, GetDesiredX(), ref velocityX, horizontalSmoothTime);
        position.y = Mathf.SmoothDamp(position.y, GetDesiredY(), ref velocityY, verticalSmoothTime);
        // After smoothing, so the target can't slip past the margin while the camera eases.
        position.y = KeepTargetInMargin(position.y);
        transform.position = position;
    }

    // The focus only moves when the target leaves the dead zone around it.
    private void UpdateFocusX()
    {
        float delta = target.position.x - focusX;
        if (Mathf.Abs(delta) > horizontalDeadZone)
            focusX += delta - Mathf.Sign(delta) * horizontalDeadZone;
    }

    // IsGrounded is polled because Landed skips the first touchdown. Landed is needed too: a
    // buffered jump can land and take off in the same physics step, so IsGrounded is never true
    // here, and the ground height would stay on the lower platform when chaining jumps.
    private void UpdateGroundY()
    {
        float y = target.position.y;
        // Without an active PlayerMovement, jumps can't be told apart, so the live height counts as ground.
        bool grounded = targetMovement == null || !targetMovement.isActiveAndEnabled ||
                        targetMovement.IsGrounded || landedSinceLastFrame;
        landedSinceLastFrame = false;

        if (grounded)
        {
            groundY = y;
            if (!hasBaseY)
            {
                baseY = y;
                hasBaseY = true;
            }
        }
        else if (!hasBaseY)
        {
            groundY = baseY = y;
        }
        else if (y < groundY)
        {
            // Dropping off a ledge: follow down right away, so what's below isn't hidden.
            groundY = y;
        }
    }

    private float GetDesiredX()
    {
        float x = focusX + offset.x;
        return clampHorizontal ? ClampX(x) : x;
    }

    // Partial follow above the base keeps the ground in view on high platforms;
    // full follow below it means a pit is never hidden.
    private float GetDesiredY()
    {
        float climb = groundY - baseY;
        float strength = climb > 0f ? verticalFollowStrength : 1f;
        return baseY + offset.y + strength * climb;
    }

    // The safety net over all other vertical rules. A margin bigger than half the screen centres the target.
    private float KeepTargetInMargin(float cameraY)
    {
        float reach = Mathf.Max(0f, GetVisibleHalfHeight() - screenEdgeMargin);
        float targetY = target.position.y;
        return Mathf.Clamp(cameraY, targetY - reach, targetY + reach);
    }

    // Read every time, because the visible width depends on the window's aspect ratio.
    private float ClampX(float x)
    {
        float halfWidth = GetVisibleHalfHeight() * cam.aspect;
        float min = levelMinX + halfWidth;
        float max = levelMaxX - halfWidth;
        // Level narrower than the view: just centre it.
        if (min > max)
            return (levelMinX + levelMaxX) * 0.5f;
        return Mathf.Clamp(x, min, max);
    }

    // PixelPerfectCamera only writes Camera.orthographicSize at render time, so in Start
    // the camera's own value can still be wrong; its computed size is ready after its Awake.
    private float GetVisibleHalfHeight()
    {
        bool usePixelPerfect = pixelPerfect != null && pixelPerfect.isActiveAndEnabled && pixelPerfect.orthographicSize > 0f;
        return usePixelPerfect ? pixelPerfect.orthographicSize : cam.orthographicSize;
    }
}
