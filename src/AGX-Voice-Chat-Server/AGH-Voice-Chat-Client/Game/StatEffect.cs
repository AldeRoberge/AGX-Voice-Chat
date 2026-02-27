namespace AGH.Shared
{
    /// <summary>
    /// Types of status effects that can be applied to players
    /// </summary>
    public enum StatEffectType : byte
    {
        None = 0,
        Swimming = 1,   // When in water block
        Climbing = 2,   // When on ladder block
        Burning = 3,    // Future: when in fire
        Poisoned = 4    // Future: poison effect
    }
}
