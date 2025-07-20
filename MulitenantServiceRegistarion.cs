
// Service Registration (Program.cs)

var builder = WebApplication.CreateBuilder(args);

// 1. Register Tenant Context Infrastructure
builder.Services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
builder.Services.AddScoped<ITenantResolver, TenantResolver>();
builder.Services.AddScoped<ITenantConfigurationProvider, TenantConfigurationProvider>();

// 2. Register Multi-Tenant Data Access
builder.Services.AddDbContextFactory<AppDbContext>(options =>
{
    // Base configuration - connection string will be set per tenant
    options.UseSqlServer(); // Don't set connection string here
});

// 3. Register Tenant-Aware Services
builder.Services.AddScoped<ICustomerRepository, CustomerRepository>();
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderService, OrderService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<INotificationService, NotificationService>();

// 4. Register Background Services
builder.Services.AddHostedService<TenantTaskProcessor>();
builder.Services.AddScoped<IDataProcessor, DataProcessor>();

// 5. Add HTTP Context Accessor for web scenarios
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// 6. Register Tenant Resolution Middleware (for web apps)
app.UseMiddleware<TenantResolutionMiddleware>();

app.Run();


// Additional Service Implementations
// 1. Tenant Resolver Service

public interface ITenantResolver
{
    Task<TenantContext> ResolveAsync(string tenantId, string environment);
    Task<List<TenantContext>> GetAllTenantsAsync();
}

public class TenantResolver : ITenantResolver
{
    private readonly IConfiguration _configuration;
    
    public TenantResolver(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<TenantContext> ResolveAsync(string tenantId, string environment)
    {
        // In real implementation, you might query a tenant database
        var connectionString = _configuration.GetConnectionString($"Tenant_{tenantId}_{environment}");
        
        return new TenantContext
        {
            TenantId = tenantId,
            Environment = environment,
            ConnectionString = connectionString ?? BuildDefaultConnectionString(tenantId, environment),
            Properties = await LoadTenantPropertiesAsync(tenantId, environment)
        };
    }

    public async Task<List<TenantContext>> GetAllTenantsAsync()
    {
        // Load from configuration or database
        var tenants = _configuration.GetSection("Tenants").Get<List<TenantConfig>>();
        return tenants.Select(t => TenantContext.Create(t.TenantId, t.Environment)).ToList();
    }
    
    private string BuildDefaultConnectionString(string tenantId, string environment)
    {
        var baseConnectionString = _configuration.GetConnectionString("DefaultConnection");
        return baseConnectionString.Replace("{TenantId}", tenantId).Replace("{Environment}", environment);
    }
    
    private async Task<Dictionary<string, object>> LoadTenantPropertiesAsync(string tenantId, string environment)
    {
        // Load tenant-specific configuration
        return new Dictionary<string, object>();
    }
}



// 2. Tenant Configuration Provider

public interface ITenantConfigurationProvider
{
    T GetConfiguration<T>(string key) where T : class;
    string GetConnectionString();
}



public class TenantConfigurationProvider : ITenantConfigurationProvider
{
    private readonly ITenantContextAccessor _tenantAccessor;
    private readonly IConfiguration _configuration;

    public TenantConfigurationProvider(ITenantContextAccessor tenantAccessor, IConfiguration configuration)
    {
        _tenantAccessor = tenantAccessor;
        _configuration = configuration;
    }

    public T GetConfiguration<T>(string key) where T : class
    {
        var tenant = _tenantAccessor.Current ?? throw new InvalidOperationException("No tenant context");
        
        // Try tenant-specific config first
        var tenantKey = $"Tenants:{tenant.TenantId}:{tenant.Environment}:{key}";
        var tenantConfig = _configuration.GetSection(tenantKey).Get<T>();
        
        if (tenantConfig != null)
            return tenantConfig;
            
        // Fall back to default config
        return _configuration.GetSection(key).Get<T>();
    }

    public string GetConnectionString()
    {
        var tenant = _tenantAccessor.Current ?? throw new InvalidOperationException("No tenant context");
        return tenant.ConnectionString;
    }
}

// 3. Enhanced DbContext Factory Usage

public class TenantDbContextFactory
{
    private readonly IDbContextFactory<AppDbContext> _contextFactory;
    private readonly ITenantContextAccessor _tenantAccessor;

    public TenantDbContextFactory(
        IDbContextFactory<AppDbContext> contextFactory, 
        ITenantContextAccessor tenantAccessor)
    {
        _contextFactory = contextFactory;
        _tenantAccessor = tenantAccessor;
    }

    public AppDbContext CreateDbContext()
    {
        var tenant = _tenantAccessor.Current ?? throw new InvalidOperationException("No tenant context");
        
        var context = _contextFactory.CreateDbContext();
        context.Database.SetConnectionString(tenant.ConnectionString);
        
        return context;
    }
}

// Register it as well
builder.Services.AddScoped<TenantDbContextFactory>();





// Configuration Example (appsettings.json)

// {
//   "ConnectionStrings": {
//     "DefaultConnection": "Server=localhost;Database=MultiTenantApp_{TenantId}_{Environment};Trusted_Connection=true;",
//     "Tenant_CompanyA_Production": "Server=prod-server;Database=CompanyA_Prod;User Id=user;Password=pass;",
//     "Tenant_CompanyA_Staging": "Server=staging-server;Database=CompanyA_Stage;User Id=user;Password=pass;",
//     "Tenant_CompanyB_Production": "Server=prod-server;Database=CompanyB_Prod;User Id=user;Password=pass;"
//   },
//   "Tenants": [
//     {
//       "TenantId": "CompanyA",
//       "Environment": "Production"
//     },
//     {
//       "TenantId": "CompanyA", 
//       "Environment": "Staging"
//     },
//     {
//       "TenantId": "CompanyB",
//       "Environment": "Production"
//     }
//   ]
// }



// Service Registration with Options Pattern
// For more advanced configuration:

// 1. Configuration Options
builder.Services.Configure<MultiTenantOptions>(builder.Configuration.GetSection("MultiTenant"));

// 2. Conditional Service Registration
builder.Services.AddScoped<ICustomerRepository>(serviceProvider =>
{
    var tenantAccessor = serviceProvider.GetRequiredService<ITenantContextAccessor>();
    var dbContextFactory = serviceProvider.GetRequiredService<TenantDbContextFactory>();
    
    return new CustomerRepository(tenantAccessor, dbContextFactory);
});

// 3. Tenant-Specific Service Factories
builder.Services.AddScoped<Func<string, IEmailService>>(serviceProvider => tenantId =>
{
    var tenantAccessor = serviceProvider.GetRequiredService<ITenantContextAccessor>();
    var config = serviceProvider.GetRequiredService<ITenantConfigurationProvider>();
    
    return tenantId switch
    {
        "CompanyA" => new SendGridEmailService(config),
        "CompanyB" => new SmtpEmailService(config),
        _ => new DefaultEmailService(config)
    };
});


// Options Configuration Class

public class MultiTenantOptions
{
    public List<TenantConfig> Tenants { get; set; } = new();
    public string DefaultConnectionString { get; set; }
    public bool EnableTenantIsolation { get; set; } = true;
}

public class TenantConfig
{
    public string TenantId { get; set; }
    public string Environment { get; set; }
    public string ConnectionString { get; set; }
    public Dictionary<string, object> Settings { get; set; } = new();
}


// This complete setup provides:

// Proper dependency injection for all multi-tenant services
// Configuration management for different tenant environments
// Flexible service resolution based on tenant context
// Clean separation between tenant infrastructure and business logic
