using System;
using UnityEngine;

/// <summary>
/// Hit points for anything that can be hurt. In Core, not Player, because monsters and bosses will reuse it.
/// TakeDamage is the one place where damage is decided: invulnerability is checked there, and in M2 armour will
/// lower the amount there before it is applied. Other scripts never change health directly; they call
/// TakeDamage or Heal and listen to the events.
/// </summary>
/// <remarks>
/// OUTLINE for David to fill in (M1 step 5). Each TODO says what a part should do and why, not how.
/// Until then it compiles and does nothing: TakeDamage ignores every hit and returns false, so the training
/// dummy only logs its attempts. Unity also shows a few yellow warnings for this file (an event or a field that
/// is never used). They are expected and go away once your code uses that part, so they work as a partial
/// checklist: Unity hides some unused-field warnings, so a missing warning doesn't mean that part is done.
/// </remarks>
// Attackers find Health with GetComponentInParent; a second one on the same object would never be found.
[DisallowMultipleComponent]
public class Health : MonoBehaviour, IDamageable
{
    [Tooltip("Health at the start, and the most Heal can restore.")]
    [Min(1)]
    // The M1 scene builder sets this field by name. If you rename it, update M1TestSceneBuilder too.
    [SerializeField] private int maxHealth = 100;

    [Tooltip("The live value, shown so you can watch it in Play mode. Set by the script; typing a value here skips the rules and events.")]
    
    [SerializeField] private int currentHealth;

    private IInvulnerable invulnerability;

    public int CurrentHealth => currentHealth;

    public int MaxHealth => maxHealth;

    public bool IsDead => currentHealth <= 0;

    /// <summary>
    /// Raised when a hit is applied, with the damage really taken (on the final blow this can be less than the
    /// hit's amount). Never raised for an ignored hit. The M3 tracker counts "hits taken" through this event,
    /// so it must fire exactly once per hit that landed, and never for a hit that was dodged.
    /// </summary>
    public event Action<int> Damaged;

    /// <summary>
    /// Raised once when health reaches 0, right after the Damaged of the final hit. What dying does
    /// (stopping the controls, an animation, a respawn) is up to the listeners.
    /// </summary>
    public event Action Died;

    /// <summary>Raised when Heal or ResetHealth restores health.</summary>
    public event Action<int> Healed;

    private void Awake()
    {
        currentHealth = maxHealth;
        TryGetComponent(out invulnerability);
    }

    /// <summary>Applies a hit unless it is ignored. Returns true if it was applied (see IDamageable).</summary>
    public bool TakeDamage(int amount, GameObject source)
    {
        if (amount <= 0)
            return false;

        if (IsDead)
        {
            Debug.Log("Ignored: dead");
            return false;
        }

        if (invulnerability != null && invulnerability.IsInvulnerable)
            return false;

        int taken = Mathf.Min(amount, currentHealth);
        currentHealth -= taken;

        bool killed = currentHealth == 0;
        Damaged?.Invoke(taken);
        if (killed)
            Die();

        return true;
    }

    /// <summary>Restores health, up to MaxHealth. Does nothing while dead.</summary>
    public void Heal(int amount)
    {
        if (amount <= 0 || IsDead)
            return;

        int restored = Mathf.Min(amount, maxHealth - currentHealth);
        if (restored <= 0)
            return;

        currentHealth += restored;
        Healed?.Invoke(restored);
    }

    /// <summary>Back to full health and alive again, for respawns later. Optional for M1.</summary>
    public void ResetHealth()
    {
        int restored = maxHealth - currentHealth;
        currentHealth = maxHealth;

        if (restored > 0)
            Healed?.Invoke(restored);
    }

    // Only TakeDamage calls this, once, on the hit that brings health to 0.
    private void Die()
    {
        Died?.Invoke();
    }
}
