using System;
using MessagePack;

namespace AGH_Voice_Chat_Client.Game.Items
{
    /// <summary>
    /// Represents a player's inventory with 9 slots (indexed 0-8).
    /// Supports MessagePack serialization for efficient network transmission.
    /// </summary>
    [MessagePackObject]
    public class InventoryComponent
    {
        public const int SlotCount = 9;

        [Key(0)] public ItemStack[] Slots { get; set; }

        [Key(1)] public int ActiveSlotIndex { get; set; }

        [SerializationConstructor]
        public InventoryComponent(ItemStack[] slots, int activeSlotIndex)
        {
            Slots = slots ?? new ItemStack[SlotCount];
            ActiveSlotIndex = activeSlotIndex;
        }

        private InventoryComponent()
        {
            // Initialize with empty slots (also serves as documentation)
            Slots = new ItemStack[SlotCount];
            for (int i = 0; i < SlotCount; i++)
            {
                Slots[i] = ItemStack.Empty;
            }

            ActiveSlotIndex = 0;
        }

        /// <summary>
        /// Gets the currently active item stack.
        /// </summary>
        [IgnoreMember]
        public ItemStack ActiveSlot => Slots[ActiveSlotIndex];

        /// <summary>
        /// Sets an item in a specific slot.
        /// </summary>
        public void SetSlot(int index, ItemStack stack)
        {
            if (index >= 0 && index < SlotCount)
            {
                Slots[index] = stack;
            }
        }

        /// <summary>
        /// Gets an item from a specific slot.
        /// </summary>
        public ItemStack GetSlot(int index)
        {
            if (index >= 0 && index < SlotCount)
            {
                return Slots[index];
            }

            return ItemStack.Empty;
        }

        /// <summary>
        /// Switches to a different inventory slot.
        /// </summary>
        public bool SwitchSlot(int newIndex)
        {
            if (newIndex is >= 0 and < SlotCount)
            {
                ActiveSlotIndex = newIndex;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Creates a default loadout with starting weapons.
        /// Slot 0 → Pistol, Slot 1 → Shotgun, Slot 2 → Sniper, Slot 3 → Grenade Launcher
        /// </summary>
        public static InventoryComponent CreateDefaultLoadout()
        {
            var inventory = new InventoryComponent();
            inventory.Slots[0] = new ItemStack(ItemType.Pistol, 1, 1);
            inventory.Slots[1] = new ItemStack(ItemType.Shotgun, 2, 1);
            inventory.Slots[2] = new ItemStack(ItemType.Sniper, 3, 1);
            inventory.Slots[3] = new ItemStack(ItemType.GrenadeLauncher, 4, 1);
            inventory.ActiveSlotIndex = 0;
            return inventory;
        }

        /// <summary>
        /// Serializes the component to a byte array using MessagePack.
        /// </summary>
        public static byte[] Serialize(InventoryComponent component)
        {
            Console.WriteLine("Serializing {Slots} slots", component?.Slots?.Length ?? 0);
            var result = MessagePackSerializer.Serialize(component);
            Console.WriteLine("Produced {Bytes} bytes", result.Length);
            return result;
        }

        /// <summary>
        /// Deserializes a byte array back to an InventoryComponent using MessagePack.
        /// </summary>
        public static InventoryComponent Deserialize(byte[] data)
        {
            Console.WriteLine("Attempting to deserialize {Bytes} bytes", data.Length);
            try
            {
                var result = MessagePackSerializer.Deserialize<InventoryComponent>(data);
                Console.WriteLine("Result is {Status}, Slots={Slots}",
                    result == null ? "NULL" : "NOT NULL", result?.Slots?.Length ?? 0);
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                throw;
            }
        }
    }
}