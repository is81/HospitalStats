namespace HospitalStats.QueryEngine;

/// <summary>
/// Oracle query execution engine. Executes dynamic SQL against Oracle databases,
/// handling US7ASCII encoding, pagination (ROWNUM for Oracle 10g), and Excel export.
/// Zero ASP.NET / EF Core dependency.
/// </summary>
public interface IQueryEngine
{
    /// <summary>
    /// Execute a query against an Oracle database. Returns paginated results.
    /// </summary>
    Task<QueryEngineResult> ExecuteAsync(QueryEngineRequest request, CancellationToken ct = default);

    /// <summary>
    /// Execute and produce an Excel file (.xlsx) as a byte array.
    /// </summary>
    Task<byte[]> ExportExcelAsync(QueryEngineRequest request, CancellationToken ct = default);

    /// <summary>
    /// Get distinct values for a filter column's drop-down options.
    /// </summary>
    Task<List<string>> GetDistinctValuesAsync(DistinctValuesRequest request, CancellationToken ct = default);
}
