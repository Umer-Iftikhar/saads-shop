namespace SaadsShop.Api.Common;

/// <summary>
/// Where an order sits in the shop's process. The names match the strings the
/// database CHECK constraint permits — they are stored as text, not ordinals,
/// so inserting a value here can never silently reinterpret existing rows.
/// </summary>
public enum OrderStatus
{
    Placed,
    Measuring,
    Stitching,
    Ready,
    Delivered,
    Cancelled
}

/// <summary>
/// How the customer pays. Card exists so the Settings screen can show it
/// switched off honestly; the shop does not take cards, and no card data is
/// ever collected — see docs/security.md.
/// </summary>
public enum PaymentMethod
{
    CashOnDelivery,
    WhatsApp,
    ReserveInShop,
    Card
}

/// <summary>A column on the stitching floor's board.</summary>
public enum StitchingStage
{
    Measuring,
    Cutting,
    Stitching,
    Ready,
    Done
}

/// <summary>
/// How a fabric is drawn when there is no photograph. Lives here because the
/// storefront, the set builder and the shop panel must render a given cloth
/// identically.
/// </summary>
public enum FabricWeave
{
    Woven,
    Striped,
    Floral
}
