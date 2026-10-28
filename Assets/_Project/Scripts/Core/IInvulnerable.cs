/// <summary>
/// Anything that can briefly ignore hits: the player's dodge and jump windows now, boss phases later.
/// Health asks only this interface, so it works the same on every object without knowing any Player or Boss class.
/// </summary>
public interface IInvulnerable
{
    /// <summary>True while hits should be ignored. The implementing class says when it is safe to read.</summary>
    bool IsInvulnerable { get; }
}
