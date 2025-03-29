public class StoredProcedureParameter
{
    public string Name { get; set; }
    public string DataType { get; set; }
    public bool IsOutput { get; set; }
    public int? MaxLength { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public int ParameterOrder { get; set; }
    public bool IsNullable { get; set; }
    public object DefaultValue { get; set; }
}

public async Task<StoredProcedureMetadata> GetStoredProcedureMetadataAsync(string sprocName, string connectionString)
{
    var metadata = new StoredProcedureMetadata { Name = sprocName };
    
    using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    
    // Query to get parameter information with nullable and default value information
    string sql = @"
        SELECT 
            p.name AS ParameterName,
            t.name AS DataType,
            p.is_output AS IsOutput,
            p.max_length AS MaxLength,
            p.precision AS Precision,
            p.scale AS Scale,
            p.parameter_id AS ParameterOrder,
            p.is_nullable AS IsNullable,
            p.default_value AS DefaultValue
        FROM sys.procedures sp
        INNER JOIN sys.parameters p ON sp.object_id = p.object_id
        INNER JOIN sys.types t ON p.user_type_id = t.user_type_id
        WHERE sp.name = @SprocName
        ORDER BY p.parameter_id";
    
    using var command = new SqlCommand(sql, connection);
    command.Parameters.AddWithValue("@SprocName", sprocName);
    
    using var reader = await command.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        metadata.Parameters.Add(new StoredProcedureParameter
        {
            Name = reader["ParameterName"].ToString(),
            DataType = reader["DataType"].ToString(),
            IsOutput = (bool)reader["IsOutput"],
            MaxLength = reader["MaxLength"] != DBNull.Value ? (int?)reader["MaxLength"] : null,
            Precision = reader["Precision"] != DBNull.Value ? (int?)reader["Precision"] : null,
            Scale = reader["Scale"] != DBNull.Value ? (int?)reader["Scale"] : null,
            ParameterOrder = (int)reader["ParameterOrder"],
            IsNullable = (bool)reader["IsNullable"],
            DefaultValue = reader["DefaultValue"] != DBNull.Value ? reader["DefaultValue"] : null
        });
    }
    
    return metadata;
}

// Method to execute stored procedure with default values
public async Task<DataTable> ExecuteStoredProcedureWithDefaultsAsync(StoredProcedureMetadata sprocMetadata, string connectionString)
{
    using var connection = new SqlConnection(connectionString);
    await connection.OpenAsync();
    
    using var command = new SqlCommand(sprocMetadata.Name, connection);
    command.CommandType = CommandType.StoredProcedure;
    
    // Add parameters with default values
    foreach (var param in sprocMetadata.Parameters)
    {
        var sqlParam = command.Parameters.Add(param.Name, GetSqlDbType(param.DataType));
        
        // Set properties
        if (param.MaxLength.HasValue && param.MaxLength.Value != -1)
            sqlParam.Size = param.MaxLength.Value;
            
        if (param.Precision.HasValue)
            sqlParam.Precision = (byte)param.Precision.Value;
            
        if (param.Scale.HasValue)
            sqlParam.Scale = (byte)param.Scale.Value;
            
        sqlParam.Direction = param.IsOutput ? ParameterDirection.Output : ParameterDirection.Input;
        
        // Set default value
        if (param.DefaultValue != null)
        {
            sqlParam.Value = param.DefaultValue;
        }
        else if (param.IsNullable)
        {
            sqlParam.Value = DBNull.Value;
        }
        else
        {
            // Provide type-specific default values for non-nullable parameters
            sqlParam.Value = GetDefaultValueForType(param.DataType);
        }
    }
    
    // Execute and return results
    var dataTable = new DataTable();
    using var adapter = new SqlDataAdapter(command);
    adapter.Fill(dataTable);
    
    return dataTable;
}

private SqlDbType GetSqlDbType(string sqlTypeName)
{
    return sqlTypeName.ToLower() switch
    {
        "int" => SqlDbType.Int,
        "bigint" => SqlDbType.BigInt,
        "bit" => SqlDbType.Bit,
        "char" => SqlDbType.Char,
        "datetime" => SqlDbType.DateTime,
        "datetime2" => SqlDbType.DateTime2,
        "decimal" => SqlDbType.Decimal,
        "float" => SqlDbType.Float,
        "nchar" => SqlDbType.NChar,
        "nvarchar" => SqlDbType.NVarChar,
        "real" => SqlDbType.Real,
        "smallint" => SqlDbType.SmallInt,
        "text" => SqlDbType.Text,
        "tinyint" => SqlDbType.TinyInt,
        "uniqueidentifier" => SqlDbType.UniqueIdentifier,
        "varbinary" => SqlDbType.VarBinary,
        "varchar" => SqlDbType.VarChar,
        "xml" => SqlDbType.Xml,
        "date" => SqlDbType.Date,
        "time" => SqlDbType.Time,
        "datetimeoffset" => SqlDbType.DateTimeOffset,
        _ => SqlDbType.Variant
    };
}

private object GetDefaultValueForType(string sqlTypeName)
{
    return sqlTypeName.ToLower() switch
    {
        "int" => 0,
        "bigint" => 0L,
        "bit" => false,
        "char" => string.Empty,
        "datetime" => DateTime.Now,
        "datetime2" => DateTime.Now,
        "decimal" => 0m,
        "float" => 0.0,
        "nchar" => string.Empty,
        "nvarchar" => string.Empty,
        "real" => 0f,
        "smallint" => (short)0,
        "text" => string.Empty,
        "tinyint" => (byte)0,
        "uniqueidentifier" => Guid.Empty,
        "varbinary" => new byte[0],
        "varchar" => string.Empty,
        "xml" => string.Empty,
        "date" => DateTime.Today,
        "time" => TimeSpan.Zero,
        "datetimeoffset" => DateTimeOffset.Now,
        _ => DBNull.Value
    };
}

// Example integration flow
public async Task TestAllControllerEndpoints()
{
    // 1. Use reflection to get all controller endpoints and their sproc names
    var endpointSprocMappings = ReflectControllerEndpoints();
    
    foreach (var mapping in endpointSprocMappings)
    {
        // 2. Get sproc metadata from the database
        var sprocMetadata = await GetStoredProcedureMetadataAsync(mapping.SprocName, _connectionString);
        
        // 3. Execute the sproc with default values
        var results = await ExecuteStoredProcedureWithDefaultsAsync(sprocMetadata, _connectionString);
        
        // 4. Log or validate results
        LogResults(mapping.EndpointName, results);
    }
}
