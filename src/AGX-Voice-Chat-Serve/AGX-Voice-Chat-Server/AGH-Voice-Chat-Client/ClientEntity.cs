using System;
using System.Collections.Generic;
using System.Numerics;
using AGH.Shared;

namespace AGH_VOice_Chat_Client
{
    /// <summary>
    /// Represents an entity in the client world (player or projectile).
    /// Simplified to be a plain state container for the new architecture.
    /// </summary>
    public class ClientEntity
    {
        public Guid Id { get; set; }
        public Vector3 Position { get; set; }
        public Vector3 Velocity { get; set; }
        public float Rotation { get; set; } // Aiming direction (toward mouse cursor)
        public float VisualRotation { get; set; } // Visual facing direction (last movement direction)
        public bool IsLocalPlayer { get; set; }
        public string Name { get; set; } = string.Empty;
        public EntityType Type { get; set; } = EntityType.Player;

        // For projectiles

        public Guid OwnerId { get; set; }
        public uint ProjectileId { get; set; }
        public float Lifetime { get; set; }

        // Health - using shared component system
        public HealthComponent Health { get; set; } = new(100, 100);
        
        // Status effects - server authoritative
        public List<StatEffectType> StatusEffects { get; set; } = new();
        
        // Damage visual effect
        public float DamageBlinkTimer { get; set; }

        // Debug
        public Vector3 ServerPosition { get; set; }
        public List<ReconciliationInfo> ReconciliationHistory { get; } = [];
    }

    public struct ReconciliationInfo
    {
        public DateTime Timestamp;
        public Vector3 ExpectedPosition;
        public Vector3 ActualPosition;
    }

    public enum EntityType
    {
        Player,
        Projectile,
        Box
    }
}