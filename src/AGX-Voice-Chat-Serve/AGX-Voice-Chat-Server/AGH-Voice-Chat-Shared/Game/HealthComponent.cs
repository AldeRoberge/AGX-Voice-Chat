using MessagePack;

namespace AGH.Shared;

/// <summary>
/// Shared health component used by both client and server.
/// Supports MessagePack serialization for efficient network transmission.
/// </summary>
[MessagePackObject]
public class HealthComponent(int current, int max)
{
    [Key(0)]
    public int Current { get; set; } = current;

    [Key(1)]
    public int Max { get; set; } = max;

    /// <summary>
    /// Serializes the component to a byte array using MessagePack.
    /// </summary>
    public static byte[] Serialize(HealthComponent component)
    {
        return MessagePackSerializer.Serialize(component);
    }

    /// <summary>
    /// Deserializes a byte array back to a HealthComponent using MessagePack.
    /// </summary>
    public static HealthComponent Deserialize(byte[] data)
    {
        return MessagePackSerializer.Deserialize<HealthComponent>(data);
    }
}