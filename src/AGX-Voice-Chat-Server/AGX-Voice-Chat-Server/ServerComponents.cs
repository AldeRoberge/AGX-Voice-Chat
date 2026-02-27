using System.Numerics;
using AGH.Shared;
using AGH.Shared.Items;
using Friflo.Engine.ECS;

namespace AGX_Voice_Chat_Server
{
    public struct IdComponent : IComponent
    {
        public Guid Value;
    }

    public struct PositionComponent : IComponent
    {
        public Vector3 Value;
    }

    public struct VelocityComponent : IComponent
    {
        public Vector3 Value;
    }

    public struct RotationComponent : IComponent
    {
        public float Value; // Rotation in radians (aiming direction, toward mouse cursor)
    }

    public struct VisualRotationComponent : IComponent
    {
        public float Value; // Visual/facing rotation in radians (last movement direction)
    }

    public struct NameComponent : IComponent
    {
        public string Name;
    }

    /// <summary>
    /// Tracks the last input tick processed for this player.
    /// Used to inform client which inputs have been acknowledged.
    /// </summary>
    public struct LastProcessedInputComponent : IComponent
    {
        public uint Tick;
    }

    /// <summary>
    /// Input queue for this player. Server processes inputs tick-by-tick in order.
    /// </summary>
    public struct InputQueueComponent : IComponent
    {
        public List<InputCommand> Queue { get; init; }
    }

    /// <summary>
    /// Tracks the last tick when the player fired a projectile.
    /// Used to enforce fire rate cooldown.
    /// </summary>
    public struct LastFireTickComponent : IComponent
    {
        public uint Tick;
    }

    // ============================================================================
    // PROJECTILE COMPONENTS
    // ============================================================================

    /// <summary>
    /// Marks an entity as a projectile (for querying).
    /// </summary>
    public struct ProjectileComponent : IComponent
    {
        public uint Id; // Unique projectile ID (server-assigned)
    }

    /// <summary>
    /// Tracks the owner of an entity (e.g., player who fired a projectile).
    /// Can be reused for any entity that needs ownership tracking.
    /// </summary>
    public struct OwnerComponent : IComponent
    {
        public Guid Value; // ID of the owning entity
    }

    /// <summary>
    /// Tracks how long an entity has been alive in seconds.
    /// Can be used for projectiles, temporary effects, etc.
    /// </summary>
    public struct LifetimeComponent : IComponent
    {
        public float Value; // Time alive in seconds
    }

    /// <summary>
    /// Wrapper for the shared HealthComponent to make it compatible with Friflo ECS.
    /// The actual health logic is in the shared HealthComponent class.
    /// </summary>
    public struct HealthComponentWrapper : IComponent
    {
        public HealthComponent Health { get; init; }
    }

    /// <summary>
    /// Tracks active status effects on an entity.
    /// Server is authoritative for status effects.
    /// </summary>
    public struct StatusEffectComponent : IComponent
    {
        public List<StatEffectType> ActiveEffects { get; set; }
    }

    // ============================================================================
    // INVENTORY COMPONENTS
    // ============================================================================

    /// <summary>
    /// Wrapper for the shared InventoryComponent to make it compatible with Friflo ECS.
    /// Contains the player's inventory state (9 slots, active slot, items).
    /// </summary>
    public struct InventoryComponentWrapper : IComponent
    {
        public InventoryComponent Inventory { get; init; }
    }

    /// <summary>
    /// Tracks cooldowns for each item type per player.
    /// Stores the server tick when each item was last used.
    /// </summary>
    public struct ItemCooldownsComponent : IComponent
    {
        public Dictionary<ItemType, uint> LastUseTicks { get; init; }
    }

    // ============================================================================
    // SHARED COMPONENT SYSTEM
    // ============================================================================

    public struct BoxComponent : IComponent
    {
        public uint Id;
    }
}