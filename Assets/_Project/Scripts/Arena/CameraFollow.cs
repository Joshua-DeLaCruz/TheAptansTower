using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Smoothly follows a target with a small dead zone, and can keep the view inside the level's
/// left and right edges. Note: with Grid Snapping set to None, PixelPerfectCamera snaps the
/// rendered view to screen pixels through the camera matrix; it never moves transform.position.
/// If sprites shimmer while the camera moves, try Grid Snapping "Upscale Render Texture" later.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("What the camera follows, usually the Player.")]
    [SerializeField] private Transform target;
    [Tooltip("Camera position relative to the target, in units. Positive Y shows more above the player.")]
    [SerializeField] private Vector2 offset = new Vector2(0f, 2f);

    [Header("Smoothing")]
    [Tooltip("Roughly the seconds the camera takes to catch up. 0 = locked to the target.")]
    [SerializeField] private float smoothTime = 0.15f;
    [Tooltip("Half-size of the box (in units) the target can move in without the camera moving. Stops small hops from shaking the view.")]
    [SerializeField] private Vector2 deadZone = new Vector2(0.5f, 1f);

    [Header("Horizontal Clamp")]
    [Tooltip("Keep the view between Level Min X and Level Max X.")]
    [SerializeField] private bool clampHorizontal = true;
    [Tooltip("Left edge of the level in world units. The screen's left edge never goes past this.")]
    [SerializeField] private float levelMinX = -21f;
    [Tooltip("Right edge of the level in world units. The screen's right edge never goes past this.")]
    [SerializeField] private float levelMaxX = 21f;

    private Camera cam;
    private PixelPerfectCamera pixelPerfect;
    private Vector2 focus;
    private Vector3 velocity;

    private void Awake()
    {
        cam = GetComponent<Camera>();
        pixelPerfect = GetComponent<PixelPerfectCamera>();
    }

    private void Start()
    {
        if (target == null)
            return;

        // Start on the target instead of swooping in from wherever the camera was placed.
        focus = target.position;
        transform.position = GetDesiredPosition();
    }

    // LateUpdate, so the (interpolated) player has already moved this frame.
    private void LateUpdate()
    {
        if (target == null)
            return;

        UpdateFocus();
        transform.position = Vector3.SmoothDamp(transform.position, GetDesiredPosition(), ref velocity, smoothTime);
    }

    // The focus only moves when the target leaves the dead zone around it.
    private void UpdateFocus()
    {
        Vector2 delta = (Vector2)target.position - focus;
        if (Mathf.Abs(delta.x) > deadZone.x)
            focus.x += delta.x - Mathf.Sign(delta.x) * deadZone.x;
        if (Mathf.Abs(delta.y) > deadZone.y)
            focus.y += delta.y - Mathf.Sign(delta.y) * deadZone.y;
    }

    private Vector3 GetDesiredPosition()
    {
        Vector2 desired = focus + offset;
        if (clampHorizontal)
            desired.x = ClampX(desired.x);
        return new Vector3(desired.x, desired.y, transform.position.z);
    }

    // Read every time, because the visible width depends on the window's aspect ratio.
    // PixelPerfectCamera only writes Camera.orthographicSize at render time, so in Start
    // the camera's own value can still be wrong; its computed size is ready after its Awake.
    private float ClampX(float x)
    {
        bool usePixelPerfect = pixelPerfect != null && pixelPerfect.isActiveAndEnabled && pixelPerfect.orthographicSize > 0f;
        float size = usePixelPerfect ? pixelPerfect.orthographicSize : cam.orthographicSize;
        float halfWidth = size * cam.aspect;
        float min = levelMinX + halfWidth;
        float max = levelMaxX - halfWidth;
        // Level narrower than the view: just centre it.
        if (min > max)
            return (levelMinX + levelMaxX) * 0.5f;
        return Mathf.Clamp(x, min, max);
    }
}
