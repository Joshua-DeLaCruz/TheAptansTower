using UnityEngine;

/// <summary>
/// Anything that can be hurt: the player now, monsters and bosses later.
/// Attackers (the training dummy, the player's attacks, hazards, bosses) only know this interface, so they can
/// damage anything without knowing its concrete class. Today that class is Health; a boss could later have its
/// own version (phases, a shield) and every attack would still work on it unchanged.
/// </summary>
public interface IDamageable
{
    /// <summary>
    /// Asks this object to take a hit. The receiver decides what really happens: it may ignore the hit
    /// (invulnerable, already dead) or, later, reduce it with armour.
    /// </summary>
    /// <param name="amount">Damage before any reduction. Expected to be above 0.</param>
    /// <param name="source">What dealt the hit (the dummy, an attack, a hazard), for later use such as
    /// knockback direction or the tracker.</param>
    /// <returns>
    /// True if the hit was applied, false if it was ignored. Attackers use this to decide whether to try again:
    /// the dummy waits its full interval after a hit that landed, but tries again in the next physics step after
    /// one that was ignored. Later, the melee attack can use it to play hit stop only on real hits.
    /// </returns>
    /// <remarks>
    /// When to call it (for anyone writing an attack or hazard): the receiver may ask an IInvulnerable whether it
    /// can be hit, and the player's answer is only up to date at certain moments in a physics step (see the
    /// remarks in PlayerInvulnerability). Safe: physics callbacks (OnTriggerEnter2D/Stay2D, OnCollisionEnter2D),
    /// which Unity runs after every script's FixedUpdate in the step, so the dodge (order -10) and PlayerMovement
    /// (order 0) have already opened that step's windows; also Update/LateUpdate, or a FixedUpdate with
    /// [DefaultExecutionOrder] above 0. Not safe: a FixedUpdate with order 0 or lower. It can run before them, so in
    /// the very step a dodge or jump starts the window isn't open yet, and the window's first step lets a hit through.
    /// </remarks>
    bool TakeDamage(int amount, GameObject source);
}
