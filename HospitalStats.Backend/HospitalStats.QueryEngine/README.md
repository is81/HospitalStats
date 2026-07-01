# HospitalStats.QueryEngine

Oracle query execution engine for HIS systems. Zero ASP.NET dependency.

Handles US7ASCII Chinese text encoding, raw SQL injection, multi-table joins,
ROWNUM pagination (Oracle 10g compatible), and Excel export.

## Quick Start

```csharp
// 1. Register in DI
services.AddSingleton<IQueryEngine, QueryEngine>();

// 2. Build a request
var request = new QueryEngineRequest
{
    ConnectionString = "User Id=scott;Password=tiger;Data Source=ORCL",
    MainTable = new EngineTableDef
    {
        TableName = "DEPT_DICT",
        SchemaName = "HOSPITAL",
        Alias = "D"
    },
    Fields = new List<EngineFieldDef>
    {
        new() { ColumnName = "DEPT_CODE", TableAlias = "D" },
        new() { ColumnName = "DEPT_NAME", TableAlias = "D" }
    },
    Page = 1,
    PageSize = 20
};

// 3. Execute
var result = await engine.ExecuteAsync(request);
// result.Rows, result.Columns, result.Total, result.ElapsedMs
```

## US7ASCII Support

For US7ASCII databases, set `CharSetOverride = "gbk"` on the request.
The engine automatically wraps string columns with `RAWTOHEX(UTL_RAW.CAST_TO_RAW())`
and decodes hex values back to readable text in results.

## Excel Export

```csharp
var bytes = await engine.ExportExcelAsync(request);
File.WriteAllBytes("output.xlsx", bytes);
```

## License

This software is dual-licensed:

### AGPL v3 (Free)
Use it in open-source projects. Your entire application that uses this engine
must also be open-sourced under AGPL-compatible terms.
No license key needed — the engine runs without validation in this mode.

### Commercial License (Paid)
Use it in proprietary/closed-source products. Includes:
- No copyleft obligations
- Email support and bug fix priority

**Offline license validation (no server needed):**

```csharp
// At startup, validate using the built-in HMAC-SHA256 offline validator
EngineLicense.InitializeOffline(licenseKey);
// Or with a custom validator (e.g., call your own license server)
EngineLicense.Initialize(async (key) => { /* validate */ return true; }, licenseKey);
```

License keys are generated offline using the `GenerateLicense` tool and
delivered via email. The key contains signed data (licensee, expiry, tier)
and is cryptographically verified by the engine without network access.

Contact: is81@qq.com
