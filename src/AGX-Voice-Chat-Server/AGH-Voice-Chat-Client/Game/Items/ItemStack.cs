using MessagePack;

namespace AGH_Voice_Chat_Client.Game.Items
{
    /// <summary>
    /// Represents a stack of items with a specific type, ID, and quantity.
    /// </summary>
    [MessagePackObject]
    public struct ItemStack(ItemType itemType, uint itemId, int quantity)
    {
        [Key(0)]
        public ItemType ItemType { get; set; } = itemType;

        [Key(1)]
        public uint ItemId { get; set; } = itemId;

        [Key(2)]
        public int Quantity { get; set; } = quantity;

        [IgnoreMember]
        public bool IsEmpty => ItemType == ItemType.None || Quantity <= 0;

        public static ItemStack Empty => new ItemStack(ItemType.None, 0, 0);
    }
}
