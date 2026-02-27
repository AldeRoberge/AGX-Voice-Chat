using System.Collections.Generic;

namespace AGH_Voice_Chat_Client.Game.Items
{
    /// <summary>
    /// Complete item definition combining metadata and use effects.
    /// </summary>
    public class ItemDefinition(ItemComponent component, ItemUseEffectComponent useEffect)
    {
        public ItemComponent Component { get; set; } = component;
        public ItemUseEffectComponent UseEffect { get; set; } = useEffect;
    }

    /// <summary>
    /// Static registry of all item definitions.
    /// </summary>
    public static class ItemDefinitions
    {
        private static readonly Dictionary<ItemType, ItemDefinition> _definitions = new()
        {
            {
                ItemType.None,
                new ItemDefinition(
                    new ItemComponent("None", "No item", 128, 128, 128),
                    new ItemUseEffectComponent(false, false, 0f)
                )
            },
            {
                ItemType.Pistol,
                new ItemDefinition(
                    new ItemComponent("Pistol", "A reliable sidearm with moderate fire rate", 200, 200, 200),
                    new ItemUseEffectComponent(false, true, 0.3f)
                )
            },
            {
                ItemType.Shotgun,
                new ItemDefinition(
                    new ItemComponent("Shotgun", "Close-range powerhouse with slower fire rate", 180, 100, 50),
                    new ItemUseEffectComponent(false, true, 0.8f)
                )
            },
            {
                ItemType.Sniper,
                new ItemDefinition(
                    new ItemComponent("Sniper", "High-precision long-range rifle", 50, 150, 200),
                    new ItemUseEffectComponent(false, true, 1.5f)
                )
            },
            {
                ItemType.GrenadeLauncher,
                new ItemDefinition(
                    new ItemComponent("Grenade Launcher", "Explosive area damage weapon", 220, 50, 50),
                    new ItemUseEffectComponent(false, true, 2.0f)
                )
            }
        };

        public static ItemDefinition Get(ItemType itemType)
        {
            return _definitions.TryGetValue(itemType, out var definition) ? definition : _definitions[ItemType.None];
        }

        public static bool TryGet(ItemType itemType, out ItemDefinition? definition)
        {
            return _definitions.TryGetValue(itemType, out definition);
        }
    }
}
