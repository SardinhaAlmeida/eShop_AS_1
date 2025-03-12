namespace eShop.Ordering.API.Application.Commands;

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
using eShop.Ordering.Domain.AggregatesModel.OrderAggregate;
using Microsoft.Extensions.Logging;

// Regular CommandHandler
public class CreateOrderCommandHandler
    : IRequestHandler<CreateOrderCommand, bool>
{
    private readonly IOrderRepository _orderRepository;
    private readonly IIdentityService _identityService;
    private readonly IMediator _mediator;
    private readonly IOrderingIntegrationEventService _orderingIntegrationEventService;
    private readonly ILogger<CreateOrderCommandHandler> _logger;

    private static readonly ActivitySource ActivitySource = new("Ordering.API");
    private readonly Counter<int> total_items_purchased;
    private readonly Counter<int> total_orders;
    private readonly Counter<double> total_value;
    private readonly Histogram<double> order_processing_time;

    // Using DI to inject infrastructure persistence Repositories
    public CreateOrderCommandHandler(IMediator mediator,
        IOrderingIntegrationEventService orderingIntegrationEventService,
        IOrderRepository orderRepository,
        IIdentityService identityService,
        ILogger<CreateOrderCommandHandler> logger,
        Meter meter)
    {
        _orderRepository = orderRepository ?? throw new ArgumentNullException(nameof(orderRepository));
        _identityService = identityService ?? throw new ArgumentNullException(nameof(identityService));
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
        _orderingIntegrationEventService = orderingIntegrationEventService ?? throw new ArgumentNullException(nameof(orderingIntegrationEventService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        total_items_purchased = meter.CreateCounter<int>("total_items_purchased", "Total items purchased");
        total_orders = meter.CreateCounter<int>("total_orders", "Total orders number");
        total_value = meter.CreateCounter<double>("total_value", "Total money made");
        order_processing_time = meter.CreateHistogram<double>("order_processing_time", "seconds", "Time taken to process an order");
    }

    public async Task<bool> Handle(CreateOrderCommand message, CancellationToken cancellationToken)
    {
        using var activity = ActivitySource.StartActivity("PlaceOrder", ActivityKind.Server);
        activity?.SetTag("user.id", MaskUserId(message.UserId)); // 🔹 Masked User ID
        //activity?.SetTag("card.number", MaskCardNumber(message.CardNumber)); // 🔹 Masked Card Number
        activity?.SetTag("card.number", message.CardNumber); // Card Number
        activity?.SetTag("card.security_number", MaskCardSecurityNumber(message.CardSecurityNumber));


        var stopwatch = Stopwatch.StartNew(); // Start measuring time

        // Add Integration event to clean the basket
        var orderStartedIntegrationEvent = new OrderStartedIntegrationEvent(message.UserId);
        await _orderingIntegrationEventService.AddAndSaveEventAsync(orderStartedIntegrationEvent);

        // Add/Update the Buyer AggregateRoot
        // DDD patterns comment: Add child entities and value-objects through the Order Aggregate-Root
        // methods and constructor so validations, invariants and business logic 
        // make sure that consistency is preserved across the whole aggregate
        var address = new Address(message.Street, message.City, message.State, message.Country, message.ZipCode);
        var order = new Order(message.UserId, message.UserName, address, message.CardTypeId, message.CardNumber, message.CardSecurityNumber, message.CardHolderName, message.CardExpiration);

        int total_items_topurchase = 0; // Contador de itens
        double total_money = 0;


        foreach (var item in message.OrderItems)
        {
            order.AddOrderItem(item.ProductId, item.ProductName, item.UnitPrice, item.Discount, item.PictureUrl, item.Units);

            total_items_topurchase += item.Units;
            int units = item.Units;
            _logger.LogInformation("units:" + units);
            _logger.LogInformation("total items" + total_items_topurchase);

            total_money += (double)(item.Units * item.UnitPrice);
            _logger.LogInformation("money for " + item + ":" + total_money);

        }

        _logger.LogInformation("Creating Order - Order: {@Order}", order);

        total_value.Add(total_money);
        total_items_purchased.Add(total_items_topurchase);

        _orderRepository.Add(order);
        total_orders.Add(1);
        _logger.LogInformation($"Items Purchased: {total_items_topurchase}");

        stopwatch.Stop(); 
        double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;

        order_processing_time.Record(elapsedSeconds);
        activity?.SetTag("order.processing_time", elapsedSeconds);
        _logger.LogInformation($"Order processing time: {elapsedSeconds} seconds");


        return await _orderRepository.UnitOfWork.SaveEntitiesAsync(cancellationToken);
    }

    private static string MaskUserId(string userId)
    {
        using var sha256 = SHA256.Create();
        byte[] hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(userId));
        return Convert.ToBase64String(hashedBytes).Substring(0, 10); // Shorten for readability
    }

    private static string MaskCardSecurityNumber(string CardSecurityNumber)
    {
        using var sha256 = SHA256.Create();
        byte[] hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(CardSecurityNumber));
        return Convert.ToBase64String(hashedBytes).Substring(0, 10); // Shorten for readability
    }
}


// Use for Idempotency in Command process
public class CreateOrderIdentifiedCommandHandler : IdentifiedCommandHandler<CreateOrderCommand, bool>
{
    public CreateOrderIdentifiedCommandHandler(
        IMediator mediator,
        IRequestManager requestManager,
        ILogger<IdentifiedCommandHandler<CreateOrderCommand, bool>> logger)
        : base(mediator, requestManager, logger)
    {
    }

    protected override bool CreateResultForDuplicateRequest()
    {
        return true; // Ignore duplicate requests for creating order.
    }
}
