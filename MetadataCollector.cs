public class MetadataCollector
{
    private readonly string _connectionString;
    
    public MetadataCollector(string connectionString)
    {
        _connectionString = connectionString;
    }
    
    public async Task<ApiMetadataReport> CollectCompleteMetadataAsync()
    {
        var analyzer = new ApiMetadataAnalyzer();
        var controllerMetadata = analyzer.AnalyzeControllers();
        
        var sprocAnalyzer = new StoredProcedureAnalyzer(_connectionString);
        
        foreach (var controller in controllerMetadata)
        {
            // Process endpoints
            foreach (var endpoint in controller.Endpoints)
            {
                if (!string.IsNullOrEmpty(endpoint.StoredProcedureName))
                {
                    endpoint.StoredProcedureMetadata = 
                        await sprocAnalyzer.GetStoredProcedureMetadataAsync(endpoint.StoredProcedureName);
                }
            }
            
            // Process manager methods
            if (controller.Manager != null)
            {
                foreach (var method in controller.Manager.Methods)
                {
                    if (!string.IsNullOrEmpty(method.StoredProcedureName))
                    {
                        method.StoredProcedureMetadata = 
                            await sprocAnalyzer.GetStoredProcedureMetadataAsync(method.StoredProcedureName);
                    }
                }
            }
        }
        
        return new ApiMetadataReport
        {
            Controllers = controllerMetadata,
            GeneratedAt = DateTime.Now
        };
    }
}

public class ApiMetadataReport
{
    public List<ControllerMetadata> Controllers { get; set; }
    public DateTime GeneratedAt { get; set; }
}
