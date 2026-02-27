namespace AGH.Shared.Items
{
    /// <summary>
    /// Defines the effect of using an item (not an ECS component).
    /// </summary>
    public class ItemUseEffectComponent(bool consumeOnUse, bool spawnProjectileOnUse, float cooldownSeconds)
    {
        /// <summary>
        /// Whether the item is consumed when used.
        /// </summary>
        public bool ConsumeOnUse { get; set; } = consumeOnUse;

        /// <summary>
        /// Whether using this item spawns a projectile.
        /// </summary>
        public bool SpawnProjectileOnUse { get; set; } = spawnProjectileOnUse;

        /// <summary>
        /// Cooldown in seconds before the item can be used again.
        /// </summary>
        public float CooldownSeconds { get; set; } = cooldownSeconds;
    }
}
