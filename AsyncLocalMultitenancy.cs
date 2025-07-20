// Recommended Approach
// Use AsyncLocal with TenantContext because it:

// Automatically flows through async call chains
// Is thread-safe for concurrent operations
// Doesn't require changing every method signature
// Works seamlessly with dependency injection
// Provides clean separation of concerns

This approach allows your application to handle multiple tenants concurrently while keeping the tenant context easily accessible throughout your call stack without polluting method signatures.



// AsyncLocal<T> is a .NET mechanism that provides thread-local storage that flows with async operations. It's perfect for multi-tenant scenarios because it maintains context isolation across concurrent tenant operations.

// How AsyncLocal Works

public class AsyncLocalDemo
{
    private static readonly AsyncLocal<string> _context = new AsyncLocal<string>();
    
    public static async Task DemonstrateFlow()
    {
        _context.Value = "Tenant-A";
        
        await Task.Run(async () =>
        {
            // This still sees "Tenant-A" even in a different task
            Console.WriteLine(_context.Value); // Outputs: Tenant-A
            
            await SomeAsyncMethod();
        });
    }
    
    private static async Task SomeAsyncMethod()
    {
        // Context automatically flows here too
        Console.WriteLine(_context.Value); // Still outputs: Tenant-A
    }
}

// Complete Multi-Tenant Implementation
// 1. Tenant Context Structure

public class TenantContext
{
    public string TenantId { get; init; }
    public string Environment { get; init; }
    public string ConnectionString { get; init; }
    public Dictionary<string, object> Properties { get; init; } = new();
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    
    public static TenantContext Create(string tenantId, string environment)
    {
        return new TenantContext 
        { 
            TenantId = tenantId, 
            Environment = environment,
            ConnectionString = BuildConnectionString(tenantId, environment)
        };
    }
    
    private static string BuildConnectionString(string tenantId, string environment)
    {
        return $"Server=server;Database=App_{tenantId}_{environment};";
    }
}

// 2. AsyncLocal-Based Context Accessor

public interface ITenantContextAccessor
{
    TenantContext Current { get; set; }
    bool HasContext { get; }
    void Clear();
}

public class TenantContextAccessor : ITenantContextAccessor
{
    private static readonly AsyncLocal<TenantContextHolder> _tenantContextCurrent = new();

    public TenantContext Current
    {
        get => _tenantContextCurrent.Value?.Context;
        set
        {
            var holder = _tenantContextCurrent.Value;
            if (holder != null)
            {
                holder.Context = value;
            }
            else if (value != null)
            {
                _tenantContextCurrent.Value = new TenantContextHolder { Context = value };
            }
        }
    }

    public bool HasContext => _tenantContextCurrent.Value?.Context != null;
    
    public void Clear()
    {
        var holder = _tenantContextCurrent.Value;
        if (holder != null)
        {
            holder.Context = null;
        }
    }

    private class TenantContextHolder
    {
        public TenantContext Context;
    }
}


// Practical Usage Patterns
// 1. Background Task Processing

public class TenantTaskProcessor
{
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly IServiceProvider _serviceProvider;

    public async Task ProcessTenantsAsync(List<(string tenantId, string environment)> tenants)
    {
        // Process multiple tenants concurrently
        var tasks = tenants.Select(tenant => ProcessTenantAsync(tenant.tenantId, tenant.environment));
        await Task.WhenAll(tasks);
    }

    private async Task ProcessTenantAsync(string tenantId, string environment)
    {
        // Each task gets its own execution context
        using var scope = _serviceProvider.CreateScope();
        var tenantAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        
        // Set tenant context - this flows through all async calls in this task
        tenantAccessor.Current = TenantContext.Create(tenantId, environment);

        var processor = scope.ServiceProvider.GetRequiredService<IDataProcessor>();
        await processor.ProcessDataAsync(); // Automatically has tenant context
        
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();
        await notifications.SendCompletionNotificationAsync(); // Also has context
    }
}

// 2. Repository Pattern with Automatic Tenant Filtering

public class CustomerRepository : ICustomerRepository
{
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

    public async Task<Customer> GetByIdAsync(int customerId)
    {
        var tenant = _tenantAccessor.Current ?? throw new InvalidOperationException("No tenant context");
        
        using var dbContext = _dbContextFactory.CreateDbContext();
        dbContext.Database.SetConnectionString(tenant.ConnectionString);
        
        return await dbContext.Customers
            .Where(c => c.TenantId == tenant.TenantId)
            .FirstOrDefaultAsync(c => c.Id == customerId);
    }

    public async Task<List<Customer>> GetAllAsync()
    {
        var tenant = _tenantAccessor.Current ?? throw new InvalidOperationException("No tenant context");
        
        using var dbContext = _dbContextFactory.CreateDbContext();
        dbContext.Database.SetConnectionString(tenant.ConnectionString);
        
        return await dbContext.Customers
            .Where(c => c.TenantId == tenant.TenantId)
            .ToListAsync();
    }
}

// Service Layer Usage

public class OrderService : IOrderService
{
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly ICustomerRepository _customerRepository;
    private readonly IEmailService _emailService;

    public async Task<Order> CreateOrderAsync(CreateOrderRequest request)
    {
        // No need to pass tenant context - it's automatically available
        var tenant = _tenantAccessor.Current;
        
        // All these calls automatically operate in the current tenant context
        var customer = await _customerRepository.GetByIdAsync(request.CustomerId);
        var order = new Order 
        { 
            TenantId = tenant.TenantId,
            CustomerId = customer.Id,
            // ... other properties
        };
        
        await _orderRepository.CreateAsync(order);
        await _emailService.SendOrderConfirmationAsync(order.Id);
        
        return order;
    }
}


// Context Isolation in Concurrent Scenarios


public class ConcurrentTenantDemo
{
    public async Task DemonstrateConcurrentIsolation()
    {
        var tasks = new List<Task>
        {
            ProcessTenantDataAsync("tenant-1", "production"),
            ProcessTenantDataAsync("tenant-2", "staging"),
            ProcessTenantDataAsync("tenant-3", "development")
        };

        await Task.WhenAll(tasks); // Each maintains its own tenant context
    }

    private async Task ProcessTenantDataAsync(string tenantId, string environment)
    {
        using var scope = _serviceProvider.CreateScope();
        var tenantAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        
        // Set context for this execution path
        tenantAccessor.Current = TenantContext.Create(tenantId, environment);
        
        // Simulate work - context flows through all async operations
        await Task.Delay(100);
        await DoSomeWorkAsync(); // Has access to tenant context
        await DoMoreWorkAsync(); // Still has access to tenant context
        
        Console.WriteLine($"Processed {tenantAccessor.Current.TenantId}");
    }
}


// Benefits of AsyncLocal for Multi-Tenancy
// ✅ Automatic Context Flow

// Context flows through async/await automatically
// No need to pass tenant parameters through method chains
// Works with Task.Run, Task.WhenAll, etc.

// ✅ Thread Safety

// Each async operation gets its own context
// No cross-contamination between concurrent tenant operations
// Safe for high-concurrency scenarios

// ✅ Clean Code

// No cluttered method signatures with tenant parameters
// Services focus on business logic, not context management
// Easy to retrofit existing applications

// ✅ Performance

// Minimal overhead compared to passing parameters
// No boxing/unboxing of value types
// Efficient memory usage


// Context Lifetime Management

public async Task ProcessWithProperLifetime()
{
    using var scope = _serviceProvider.CreateScope();
    var tenantAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
    
    try
    {
        tenantAccessor.Current = TenantContext.Create("tenant-1", "prod");
        await DoWork();
    }
    finally
    {
        tenantAccessor.Clear(); // Clean up context
    }
}

// Testing Considerations
[Test]
public async Task TestWithTenantContext()
{
    // Arrange
    var tenantAccessor = new TenantContextAccessor();
    tenantAccessor.Current = TenantContext.Create("test-tenant", "test");
    
    var service = new CustomerService(tenantAccessor, mockRepository);
    
    // Act & Assert
    var result = await service.GetCustomersAsync();
    
    // Clean up
    tenantAccessor.Clear();
}


// AsyncLocal provides the most elegant solution for multi-tenant context management in .NET applications, especially when you need to support concurrent tenant operations while maintaining clean, readable code.
