namespace Wms.Modules.Inventory.Domain;

public enum LedgerEntryType
{
    OpeningBalance = 1,
    Reserved = 2,
    ReservationReleased = 3,
    ReservationConsumed = 4,
    RelocatedOut = 5,
    RelocatedIn = 6,
    StatusChangedFrom = 7,
    StatusChangedTo = 8,
    InventoryAdjustment = 9,
}
