using System.Numerics;
using AGH_Voice_Chat_Shared.Packets;
using LiteNetLib.Utils;

namespace AGH.Shared
{
    public static class NetworkRegistration
    {
        public static void RegisterTypes(NetPacketProcessor packetProcessor)
        {
            
            //
            
            // Register Vector2 serializer
            packetProcessor.RegisterNestedType((writer, vec) =>
            {
                writer.Put(vec.X);
                writer.Put(vec.Y);
            }, reader => new Vector2(reader.GetFloat(), reader.GetFloat()));

            // Register Vector2 serializer
            packetProcessor.RegisterNestedType((writer, vec) =>
            {
                writer.Put(vec.X);
                writer.Put(vec.Y);
                writer.Put(vec.Z);
            }, reader => new Vector3(reader.GetFloat(), reader.GetFloat(), reader.GetFloat()));


            // Register connection packets
            packetProcessor.RegisterNestedType<JoinRequestPacket>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new JoinRequestPacket();
                    p.Deserialize(reader);
                    return p;
                });

            packetProcessor.RegisterNestedType<JoinResponsePacket>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new JoinResponsePacket();
                    p.Deserialize(reader);
                    return p;
                });

            // Register input packets
            packetProcessor.RegisterNestedType<InputCommand>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new InputCommand();
                    p.Deserialize(reader);
                    return p;
                });

            // Register snapshot packets
            packetProcessor.RegisterNestedType<WorldSnapshot>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new WorldSnapshot();
                    p.Deserialize(reader);
                    return p;
                });

            packetProcessor.RegisterNestedType<PlayerState>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new PlayerState();
                    p.Deserialize(reader);
                    return p;
                });

            packetProcessor.RegisterNestedType<ProjectileState>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new ProjectileState();
                    p.Deserialize(reader);
                    return p;
                });

            packetProcessor.RegisterNestedType<BoxState>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new BoxState();
                    p.Deserialize(reader);
                    return p;
                });

            // Register ping packets (unchanged)
            packetProcessor.RegisterNestedType<PingPacket>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new PingPacket();
                    p.Deserialize(reader);
                    return p;
                });

            packetProcessor.RegisterNestedType<PongPacket>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new PongPacket();
                    p.Deserialize(reader);
                    return p;
                });

            // Register text message packets
            packetProcessor.RegisterNestedType<PlayerInfoPacket>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new PlayerInfoPacket();
                    p.Deserialize(reader);
                    return p;
                });

            packetProcessor.RegisterNestedType<TextPacket>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new TextPacket();
                    p.Deserialize(reader);
                    return p;
                });

            packetProcessor.RegisterNestedType<ChatMessagePacket>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new ChatMessagePacket();
                    p.Deserialize(reader);
                    return p;
                });

            // Register Dissonance voice packets
            packetProcessor.RegisterNestedType<VoiceDataPacket>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new VoiceDataPacket();
                    p.Deserialize(reader);
                    return p;
                });

            packetProcessor.RegisterNestedType<VoiceDataFromPacket>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new VoiceDataFromPacket();
                    p.Deserialize(reader);
                    return p;
                });

            packetProcessor.RegisterNestedType<VoiceDataToPeerPacket>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new VoiceDataToPeerPacket();
                    p.Deserialize(reader);
                    return p;
                });

            // Register BlockUpdate struct
            packetProcessor.RegisterNestedType(
                (writer, b) =>
                {
                    writer.Put(b.LocalX);
                    writer.Put(b.LocalY);
                    writer.Put(b.LocalZ);
                    writer.Put(b.Exists);       // byte (0 or 1)
                    writer.Put(b.BlockType);    // CRITICAL: Was missing!
                    writer.Put(b.Health);       // CRITICAL: Was missing!
                },
                reader => new BlockUpdate
                {
                    LocalX = reader.GetByte(),
                    LocalY = reader.GetByte(),
                    LocalZ = reader.GetByte(),
                    Exists = reader.GetByte(),
                    BlockType = reader.GetByte(),  // CRITICAL: Was missing!
                    Health = reader.GetByte()       // CRITICAL: Was missing!
                });

            // Register chunk packets
            packetProcessor.RegisterNestedType<ChunkCreatePacket>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new ChunkCreatePacket();
                    p.Deserialize(reader);
                    return p;
                });

            packetProcessor.RegisterNestedType<ChunkUpdatePacket>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new ChunkUpdatePacket();
                    p.Deserialize(reader);
                    return p;
                });

            // Register voxel painting packets
            packetProcessor.RegisterNestedType<VoxelPaintRequestPacket>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new VoxelPaintRequestPacket();
                    p.Deserialize(reader);
                    return p;
                });

            // Register status effect packets
            packetProcessor.RegisterNestedType<StatEffectChanged>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new StatEffectChanged();
                    p.Deserialize(reader);
                    return p;
                });

            // Register inventory packets
            packetProcessor.RegisterNestedType<ItemUseAction>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new ItemUseAction();
                    p.Deserialize(reader);
                    return p;
                });

            packetProcessor.RegisterNestedType<ItemUsedEvent>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new ItemUsedEvent();
                    p.Deserialize(reader);
                    return p;
                });

            packetProcessor.RegisterNestedType<InventorySlotSwitchedAction>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new InventorySlotSwitchedAction();
                    p.Deserialize(reader);
                    return p;
                });

            packetProcessor.RegisterNestedType<InventorySlotSwitchedEvent>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new InventorySlotSwitchedEvent();
                    p.Deserialize(reader);
                    return p;
                });

            packetProcessor.RegisterNestedType<InventoryFullSyncPacket>(
                (writer, p) => p.Serialize(writer),
                reader =>
                {
                    var p = new InventoryFullSyncPacket();
                    p.Deserialize(reader);
                    return p;
                });
        }
    }
}