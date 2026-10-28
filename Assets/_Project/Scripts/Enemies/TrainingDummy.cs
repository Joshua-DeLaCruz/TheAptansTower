using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A stationary test target that hurts any damageable body touching it, so Health and the i-frames can be
/// tested without real monsters. A test tool, not an enemy: it has no health or behaviour of its own.
/// </summary>
/// <remarks>
/// Setup: a trigger Collider2D on this object, and no Rigidbody2D. The player's dynamic Rigidbody2D is enough
/// for Unity to send trigger callbacks to both sides. The layer collision matrix (Project Settings > Physics 2D)
/// must let this object's layer (Enemy) touch the target's layer (Player); the default matrix allows every pair.
/// Unity sends no OnTriggerStay2D while the touching Rigidbody2D sleeps, so the builder sets the Player's body to
/// Never Sleep. A target that can sleep is only hit again once it moves.
/// </remarks>
public class TrainingDummy : MonoBehaviour
{
    [Tooltip("Damage per hit.")]
    [Min(1)]
    [SerializeField] private int contactDamage = 10;
    [Tooltip("Seconds between hits on the same target, counted from the last hit that was applied. A hit the target ignored (i-frames) is tried again in the next physics step.")]
    [Min(0f)]
    [SerializeField] private float hitInterval = 1f;
    [Tooltip("Logs every applied hit, and ignored hits at most once per Hit Interval (at least once a second), to the Console. For testing.")]
    [SerializeField] private bool logHits = true;

    // Physics time adds up in float steps that can land a hair under a round number such as 1.0;
    // this tolerance stops that from delaying a hit by one extra step.
    private const float TimeEpsilon = 0.0001f;
    // Ignored hits are logged at most this often, even with a very short Hit Interval.
    private const float MinIgnoredLogInterval = 1f;

    // One timer per target, because two bodies can touch the dummy at once (the player and, in M2, a monster).
    // A single "last target" field would be simpler, but two targets would keep overwriting it and both would be
    // hit in every physics step. Entries are kept after a target leaves, so stepping out and back in can't skip
    // the interval.
    private readonly Dictionary<IDamageable, float> lastHitTimes = new Dictionary<IDamageable, float>();
    // Only for the log: an ignored hit is retried every physics step, which would flood the Console.
    private readonly Dictionary<IDamageable, float> lastIgnoredLogTimes = new Dictionary<IDamageable, float>();

    private void Awake()
    {
        var trigger = GetComponent<Collider2D>();
        if (trigger == null || !trigger.isTrigger)
            Debug.LogWarning("[TrainingDummy] Needs a Collider2D with Is Trigger on, or it never hits anything.", this);
    }

    // Also drops entries of targets that were destroyed meanwhile.
    private void OnDisable()
    {
        lastHitTimes.Clear();
        lastIgnoredLogTimes.Clear();
    }

    // Stay, not Enter: Enter fires only once, when the overlap begins. If that moment falls inside a dodge's
    // i-frames, the hit is dropped, and a player who then stands in the dummy is never hit. Stay runs in every
    // physics step of the overlap, so the dummy keeps trying and lands the hit as soon as the window ends.
    // As a physics callback it runs after every script's FixedUpdate in the step, so IsInvulnerable is up to date.
    private void OnTriggerStay2D(Collider2D other)
    {
        // Unity sends trigger messages to disabled scripts too; without this, unticking the component
        // wouldn't stop the dummy.
        if (!enabled)
            return;

        // Only bodies count. Triggers (an attack hitbox, a sensor) report here too, and the player's sword
        // touching the dummy must not hurt the player.
        if (other.isTrigger)
            return;

        // InParent, so it also works when the collider sits on a child of the object with Health.
        // A lookup every step is fine for a test tool; real hazards should cache it (for example per collider on Enter).
        IDamageable target = other.GetComponentInParent<IDamageable>();
        if (target == null)
            return;

        // Physics time, because this runs once per physics step.
        float now = Time.fixedTime;
        if (lastHitTimes.TryGetValue(target, out float lastHit) && now - lastHit < hitInterval - TimeEpsilon)
            return;

        // The interval only starts after a hit that landed, so a dodged hit doesn't buy a free second.
        if (target.TakeDamage(contactDamage, gameObject))
        {
            lastHitTimes[target] = now;
            if (logHits)
                Debug.Log($"[TrainingDummy] Hit {NameOf(target)} for {contactDamage}.", this);
        }
        else
        {
            LogIgnoredHit(target, now);
        }
    }

    private void LogIgnoredHit(IDamageable target, float now)
    {
        if (!logHits)
            return;
        float logInterval = Mathf.Max(hitInterval, MinIgnoredLogInterval);
        if (lastIgnoredLogTimes.TryGetValue(target, out float lastLog) && now - lastLog < logInterval - TimeEpsilon)
            return;

        lastIgnoredLogTimes[target] = now;
        Debug.Log($"[TrainingDummy] Tried to hit {NameOf(target)} for {contactDamage}, but the hit was ignored " +
                  "(invulnerable, already dead, or Health.TakeDamage not written yet). Trying again every physics step.",
                  this);
    }

    private static string NameOf(IDamageable target)
    {
        return target is Component component ? component.name : target.ToString();
    }
}
