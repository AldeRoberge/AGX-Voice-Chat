using System.Numerics;
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

    public struct NameComponent : IComponent
    {
        public string Name;
    }
}