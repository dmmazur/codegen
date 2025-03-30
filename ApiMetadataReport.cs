// In a service or command line tool
public async Task GenerateApiMetadataReportAsync()
{
    var collector = new MetadataCollector("YourConnectionString");
    var report = await collector.CollectCompleteMetadataAsync();
    
    // You can save to JSON
    var json = JsonSerializer.Serialize(report, new JsonSerializerOptions
    {
        WriteIndented = true,
        ReferenceHandler = ReferenceHandler.Preserve
    });
    
    await File.WriteAllTextAsync("api-metadata-report.json", json);
    
    // Or use the data for validation/testing
    foreach (var controller in report.Controllers)
    {
        foreach (var endpoint in controller.Endpoints)
        {
            if (endpoint.StoredProcedureMetadata != null)
            {
                await ValidateEndpointParametersMatchSprocAsync(endpoint);
            }
        }
    }
}
