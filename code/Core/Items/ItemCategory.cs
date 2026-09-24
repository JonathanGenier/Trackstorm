namespace Trackstorm.Core.Items;

/// <summary>Stable category identities; new items share their category's allocation.</summary>
public enum ItemCategory : byte
{
    Weapon = 1,
    Consumable = 2,
    Droppable = 3,
}
