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

    /// <summary>
    /// Represents a status effect applied to an entity
    /// </summary>
    public class StatEffect(StatEffectType type, float duration = -1f)
    {
        public StatEffectType Type { get; set; } = type;
        public float Duration { get; set; } = duration; // Remaining duration in seconds, -1 for indefinite
        public float TimeRemaining { get; set; } = duration;
    }
}
