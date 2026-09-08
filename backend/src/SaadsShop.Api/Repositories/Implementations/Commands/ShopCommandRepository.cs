using SaadsShop.Api.Constants;
using SaadsShop.Api.Data;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Repositories.Interfaces.Commands;

namespace SaadsShop.Api.Repositories.Implementations.Commands;

public sealed class ShopCommandRepository(ISqlConnectionFactory connectionFactory)
    : RepositoryBase(connectionFactory), IShopCommandRepository
{
    public Task<ProcedureResult<bool>> UpdateSettingsAsync(
        SettingsUpdateRequest request, string normalisedWhatsApp, string? actorUserId,
        CancellationToken ct = default)
        => ExecuteAsync(
            StoredProcedures.SettingsUpdate,
            new
            {
                request.ShopName,
                request.City,
                request.AddressLine,
                WhatsAppNumber = normalisedWhatsApp,
                request.BannerText,
                request.OpeningHours,
                request.DeliveryCharge,
                request.FreeDeliveryThreshold,
                request.CashOnDeliveryEnabled,
                request.WhatsAppOrdersEnabled,
                request.ReserveInShopEnabled,
                request.CardPaymentEnabled,
                ActorUserId = actorUserId
            },
            ct);
}
