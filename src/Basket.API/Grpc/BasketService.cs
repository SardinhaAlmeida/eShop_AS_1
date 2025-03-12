using System.Diagnostics.CodeAnalysis;
using eShop.Basket.API.Repositories;
using eShop.Basket.API.Extensions;
using eShop.Basket.API.Model;
using System.Diagnostics.Metrics;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;


namespace eShop.Basket.API.Grpc;

public class BasketService(
    IBasketRepository repository,
    ILogger<BasketService> logger, 
    Meter _meter) : Basket.BasketBase


{
    private static readonly ActivitySource ActivitySource = new("Basket.API");
    private readonly Counter<int> addToCartCounter = _meter.CreateCounter<int>("basket_add_to_cart_total", "items", "Total number of items added to the cart");
    private readonly Counter<int> activeBasketsCounter = _meter.CreateCounter<int>("baskets_created_total","baskets","Total number of active baskets");

    [AllowAnonymous]
    public override async Task<CustomerBasketResponse> GetBasket(GetBasketRequest request, ServerCallContext context)
    {
        var userId = context.GetUserIdentity();
        if (string.IsNullOrEmpty(userId))
        {
            return new();
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Begin GetBasketById call from method {Method} for basket id {Id}", context.Method, userId);
        }

        var data = await repository.GetBasketAsync(userId);

        if (data is not null)
        {
            return MapToCustomerBasketResponse(data);
        }

        return new();
    }

    public override async Task<CustomerBasketResponse> UpdateBasket(UpdateBasketRequest request, ServerCallContext context)
    {
        using var activity = ActivitySource.StartActivity("AddToCart", ActivityKind.Server);
        var userId = context.GetUserIdentity();
        if (string.IsNullOrEmpty(userId))
        {
            ThrowNotAuthenticated();
        }

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Begin UpdateBasket call from method {Method} for basket id {Id}", context.Method, userId);
        }

        string maskedUserId = MaskUserId(userId);
        activity?.SetTag("user.id", maskedUserId);
        activity?.SetTag("basket.item_count", request.Items.Count);

        var recent = await repository.GetBasketAsync(userId);
        int previous = recent?.Items.Sum(item => item.Quantity) ?? 0;

        bool isNewBasket = recent == null; 

        var customerBasket = MapToCustomerBasket(userId, request);
        var response = await repository.UpdateBasketAsync(customerBasket);

        int new_total = request.Items.Sum(item => item.Quantity);
        int difference = new_total - previous; 

        if (difference > 0)
        {
            addToCartCounter.Add(difference);
            logger.LogDebug($"Added {difference} items to cart (previous: {previous}, new: {new_total})");
        }

        if (isNewBasket)
        {
            activeBasketsCounter.Add(1);
            logger.LogDebug($"New basket created for user {maskedUserId}");
        }

        if (response is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error);
            ThrowBasketDoesNotExist(userId);
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Ok);
        }

        return MapToCustomerBasketResponse(response);
    }

    public override async Task<DeleteBasketResponse> DeleteBasket(DeleteBasketRequest request, ServerCallContext context)
    {
        var userId = context.GetUserIdentity();
        if (string.IsNullOrEmpty(userId))
        {
            ThrowNotAuthenticated();
        }

        await repository.DeleteBasketAsync(userId);
        return new();
    }


    [DoesNotReturn]
    private static void ThrowNotAuthenticated() => throw new RpcException(new Status(StatusCode.Unauthenticated, "The caller is not authenticated."));

    [DoesNotReturn]
    private static void ThrowBasketDoesNotExist(string userId) => throw new RpcException(new Status(StatusCode.NotFound, $"Basket with buyer id {userId} does not exist"));

    private static CustomerBasketResponse MapToCustomerBasketResponse(CustomerBasket customerBasket)
    {
        var response = new CustomerBasketResponse();

        foreach (var item in customerBasket.Items)
        {
            response.Items.Add(new BasketItem()
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
            });
        }

        return response;
    }

    private static CustomerBasket MapToCustomerBasket(string userId, UpdateBasketRequest customerBasketRequest)
    {
        var response = new CustomerBasket
        {
            BuyerId = userId
        };

        foreach (var item in customerBasketRequest.Items)
        {
            response.Items.Add(new()
            {
                ProductId = item.ProductId,
                Quantity = item.Quantity,
            });
        }

        return response;
    }
    private static string MaskUserId(string userId)
    {
        using var sha256 = SHA256.Create();
        byte[] hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(userId));
        return Convert.ToBase64String(hashedBytes).Substring(0, 10); // Shorten for readability
    }
}
