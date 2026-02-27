using System;
using LiteNetLib.Utils;

namespace AGH_Voice_Chat_Shared;

public class WorldSnapshot : INetSerializable
{
    public uint Tick { get; set; }
    public PlayerState[] Players { get; set; } = Array.Empty<PlayerState>();

    public void Serialize(NetDataWriter writer)
    {
        writer.Put(Tick);
        writer.Put((ushort)Players.Length);
        foreach (var p in Players)
            p.Serialize(writer);
    }
    public void Deserialize(NetDataReader reader)
    {
        Tick = reader.GetUInt();
        ushort n = reader.GetUShort();
        Players = new PlayerState[n];
        for (int i = 0; i < n; i++)
        {
            Players[i] = new PlayerState();
            Players[i].Deserialize(reader);
        }
    }
}