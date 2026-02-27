using System.Numerics;
using AGH.Shared;
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

    public struct LastProcessedInputComponent : IComponent
    {
        public uint Tick;
    }
    
    public struct InputQueueComponent : IComponent
    {
        public List<InputCommand> Queue { get; init; }
    }

    public struct HealthComponentWrapper : IComponent
    {
        public HealthComponent Health { get; init; }
    }
}