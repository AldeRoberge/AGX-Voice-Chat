namespace AGH.Shared.Items
{
    /// <summary>
    /// Defines basic metadata for an item (not an ECS component).
    /// </summary>
    public class ItemComponent
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        
        /// <summary>
        /// Placeholder color for rendering the item sprite.
        /// Format: R, G, B (0-255)
        /// </summary>
        public (byte R, byte G, byte B) Color { get; set; }

        public ItemComponent(string name, string description, byte r, byte g, byte b)
        {
            Name = name;
            Description = description;
            Color = (r, g, b);
        }
    }
}
