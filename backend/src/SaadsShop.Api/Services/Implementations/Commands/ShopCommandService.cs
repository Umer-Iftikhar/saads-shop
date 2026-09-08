using SaadsShop.Api.Common;
using SaadsShop.Api.Constants;
using SaadsShop.Api.DTOs.Internal;
using SaadsShop.Api.DTOs.Request;
using SaadsShop.Api.Repositories.Interfaces.Commands;
using SaadsShop.Api.Services.Interfaces;
using SaadsShop.Api.Services.Interfaces.Commands;

namespace SaadsShop.Api.Services.Implementations.Commands;

public sealed class ShopCommandService(
    IShopCommandRepository repository,
    ICacheService cache,
    ILogger<ShopCommandService> logger) : IShopCommandService
{
    public async Task<OperationResult<bool>> UpdateSettingsAsync(
        SettingsUpdateRequest request, string? actorUserId, CancellationToken ct = default)
    {
        var whatsApp = PhoneNumber.Normalise(request.WhatsAppNumber);

        if (whatsApp is null)
        {
            return OperationResult<bool>.Invalid(new Dictionary<string, string[]>
            {
                [nameof(SettingsUpdateRequest.WhatsAppNumber)] =
                    ["The WhatsApp number should look like 03xx xxx xxxx."]
            });
        }

        var result = await repository.UpdateSettingsAsync(request, whatsApp, actorUserId, ct);

        if (!result.IsSuccess)
            return OperationResult<bool>.Failure(result.ResponseCode, result.ResponseMessage);

        cache.BumpVersion(CacheKeys.SettingsVersion);
        logger.LogInformation("Shop settings updated by {ActorUserId}", actorUserId);

        return OperationResult<bool>.Success(true, result.ResponseMessage);
    }
}
