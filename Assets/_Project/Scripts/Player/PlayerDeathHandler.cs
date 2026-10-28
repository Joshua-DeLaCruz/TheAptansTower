using UnityEngine;

/// <summary>
/// Stops the player when Health raises Died. Kept out of Health, because monsters and bosses die differently.
/// </summary>
[RequireComponent(typeof(Health))]
public class PlayerDeathHandler : MonoBehaviour
{
    [Tooltip("Filled in automatically from this object if left empty.")]
    [SerializeField] private Health health;
    [SerializeField] private PlayerDodge dodge;
    [SerializeField] private PlayerMovement movement;
    [SerializeField] private Rigidbody2D body;

    private void Awake()
    {
        if (health == null) health = GetComponent<Health>();
        if (dodge == null) dodge = GetComponent<PlayerDodge>();
        if (movement == null) movement = GetComponent<PlayerMovement>();
        if (body == null) body = GetComponent<Rigidbody2D>();
    }

    // Subscribed only while enabled, so a disabled or destroyed handler never gets the event.
    private void OnEnable() => health.Died += OnDied;
    private void OnDisable() => health.Died -= OnDied;

    private void OnDied()
    {
        // Dodge first: its OnDisable ends a dodge cleanly and writes a hand-over velocity.
        if (dodge != null) dodge.enabled = false;
        if (movement != null) movement.enabled = false;

        // Stopped last, so that hand-over velocity is cleared too. The material has no friction,
        // so without this the player would slide on.
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }
}
