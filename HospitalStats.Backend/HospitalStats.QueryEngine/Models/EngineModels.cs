// HospitalStats.QueryEngine - Oracle query engine for HIS systems
// Copyright (C) 2026 is81
//
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, either version 3 of the License.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY.
//
// You may also use this software under a separate commercial license.
// Contact is81@qq.com for details.

namespace HospitalStats.QueryEngine;

// ===== Request models =====

/// <summary>
/// Complete query request — connection info, query definition, filters, options.
/// The engine performs no database lookups; all data is provided by the caller.
/// </summary>
public class QueryEngineRequest
{
    /// <summary>Plaintext Oracle connection string (caller decrypts).</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>
    /// Character set override (e.g. "gbk" for US7ASCII databases).
    /// When set, string columns are wrapped with RAWTOHEX(UTL_RAW.CAST_TO_RAW())
    /// to preserve raw bytes through Oracle's lossy charset conversion.
    /// </summary>
    public string? CharSetOverride { get; set; }

    /// <summary>Main (FROM) table definition.</summary>
    public EngineTableDef MainTable { get; set; } = new();

    /// <summary>SELECT output fields.</summary>
    public List<EngineFieldDef> Fields { get; set; } = new();

    /// <summary>WHERE filter conditions.</summary>
    public List<EngineFilterDef> Filters { get; set; } = new();

    /// <summary>JOIN definitions.</summary>
    public List<EngineJoinDef> Joins { get; set; } = new();

    /// <summary>Raw SQL override (optional). If provided, takes precedence over Fields/Joins/Filters for SQL generation.</summary>
    public string? RawSql { get; set; }

    /// <summary>GROUP BY column expression (e.g. "DEPT_NAME").</summary>
    public string? GroupByColumn { get; set; }

    /// <summary>ORDER BY column expression.</summary>
    public string? SortColumn { get; set; }

    /// <summary>Sort direction: ASC or DESC.</summary>
    public string? SortDirection { get; set; } = "ASC";

    /// <summary>Page number (1-based).</summary>
    public int Page { get; set; } = 1;

    /// <summary>Rows per page. Default 50.</summary>
    public int PageSize { get; set; } = 50;

    /// <summary>User-provided filter values, keyed by filter ID (string).</summary>
    public Dictionary<string, string> FilterValues { get; set; } = new();

    /// <summary>
    /// Context filter values (e.g. JWT claims like UserId, DeptName).
    /// Populated by the caller before passing to engine. Matched to EngineFilterDef.ContextKey.
    /// </summary>
    public Dictionary<string, string> ContextValues { get; set; } = new();

    /// <summary>Runtime options.</summary>
    public EngineOptions Options { get; set; } = new();
}

/// <summary>Runtime configuration for query execution.</summary>
public class EngineOptions
{
    /// <summary>Oracle command timeout in seconds. Default 120.</summary>
    public int QueryTimeoutSeconds { get; set; } = 120;

    /// <summary>Maximum row count before throwing. Default 50000.</summary>
    public int MaxRowCount { get; set; } = 50000;

    /// <summary>History retention limit (unused by engine; informational for callers).</summary>
    public int HistoryLimit { get; set; } = 50000;
}

// ===== Table/Column definitions =====

/// <summary>Database table definition.</summary>
public class EngineTableDef
{
    /// <summary>Table name (e.g. "OUTP_BILL_ITEMS").</summary>
    public string TableName { get; set; } = "";

    /// <summary>Oracle schema name (e.g. "HOSPITAL").</summary>
    public string? SchemaName { get; set; }

    /// <summary>Table alias used in SQL (e.g. "A").</summary>
    public string? Alias { get; set; }

    /// <summary>Columns belonging to this table.</summary>
    public List<EngineColumnDef> Columns { get; set; } = new();
}

/// <summary>Column metadata.</summary>
public class EngineColumnDef
{
    /// <summary>Column name.</summary>
    public string ColumnName { get; set; } = "";

    /// <summary>Oracle data type (VARCHAR2, NUMBER, DATE, etc.).</summary>
    public string? DataType { get; set; }
}

/// <summary>Query output field (SELECT column).</summary>
public class EngineFieldDef
{
    /// <summary>Column name on the database.</summary>
    public string ColumnName { get; set; } = "";

    /// <summary>Display alias for the output.</summary>
    public string? Alias { get; set; }

    /// <summary>Aggregate function: COUNT, SUM, AVG, MAX, MIN, or null.</summary>
    public string? AggregateFunc { get; set; }

    /// <summary>Sort order in SELECT list.</summary>
    public int SortOrder { get; set; }

    /// <summary>Table alias this column belongs to.</summary>
    public string TableAlias { get; set; } = "";

    /// <summary>Oracle data type of the column.</summary>
    public string? DataType { get; set; }
}

/// <summary>WHERE filter condition.</summary>
public class EngineFilterDef
{
    /// <summary>Filter ID (matches FilterValues key).</summary>
    public int Id { get; set; }

    /// <summary>Column name to filter on.</summary>
    public string ColumnName { get; set; } = "";

    /// <summary>Table alias for the column.</summary>
    public string TableAlias { get; set; } = "";

    /// <summary>Oracle data type (VARCHAR2, NUMBER, DATE, etc.).</summary>
    public string DataType { get; set; } = "";

    /// <summary>Operator: EQ, NE, GT, GTE, LT, LTE, LIKE, NOT LIKE, IN, NOT IN, BETWEEN, NOT BETWEEN.</summary>
    public string Operator { get; set; } = "EQ";

    /// <summary>Default value when user provides none.</summary>
    public string? DefaultValue { get; set; }

    /// <summary>If true, value comes from ContextValues instead of FilterValues.</summary>
    public bool IsContextFilter { get; set; }

    /// <summary>Key into ContextValues (e.g. "DeptName", "UserId").</summary>
    public string? ContextKey { get; set; }

    /// <summary>Sort order among filters.</summary>
    public int SortOrder { get; set; }
}

/// <summary>JOIN definition.</summary>
public class EngineJoinDef
{
    /// <summary>Join type: LEFT, RIGHT, INNER, FULL.</summary>
    public string JoinType { get; set; } = "LEFT";

    /// <summary>The table being joined.</summary>
    public EngineTableDef JoinTable { get; set; } = new();

    /// <summary>Left-side column name.</summary>
    public string LeftColumnName { get; set; } = "";

    /// <summary>Left-side table alias.</summary>
    public string LeftTableAlias { get; set; } = "";

    /// <summary>Right-side column name.</summary>
    public string RightColumnName { get; set; } = "";

    /// <summary>Right-side table alias.</summary>
    public string RightTableAlias { get; set; } = "";

    /// <summary>Whether to truncate date columns in join condition (TRUNC()).</summary>
    public bool LeftDateTrunc { get; set; }

    /// <summary>Sort order among joins.</summary>
    public int SortOrder { get; set; }
}

// ===== Result models =====

/// <summary>Result of a query execution.</summary>
public class QueryEngineResult
{
    /// <summary>Data rows as dictionaries (column name → value).</summary>
    public List<Dictionary<string, object?>> Rows { get; set; } = new();

    /// <summary>Column display names in order.</summary>
    public List<string> Columns { get; set; } = new();

    /// <summary>Total row count (before pagination).</summary>
    public int Total { get; set; }

    /// <summary>Current page (1-based).</summary>
    public int Page { get; set; }

    /// <summary>Rows per page.</summary>
    public int PageSize { get; set; }

    /// <summary>Query execution time in milliseconds.</summary>
    public long ElapsedMs { get; set; }
}

/// <summary>Request for distinct filter values.</summary>
public class DistinctValuesRequest
{
    /// <summary>Plaintext Oracle connection string.</summary>
    public string ConnectionString { get; set; } = "";

    /// <summary>Character set override for US7ASCII databases.</summary>
    public string? CharSetOverride { get; set; }

    /// <summary>Schema name.</summary>
    public string? SchemaName { get; set; }

    /// <summary>Table name.</summary>
    public string TableName { get; set; } = "";

    /// <summary>Table alias used in the query.</summary>
    public string? TableAlias { get; set; }

    /// <summary>Column name to get distinct values for.</summary>
    public string ColumnName { get; set; } = "";

    /// <summary>Column data type (to determine if hex encoding is needed).</summary>
    public string? DataType { get; set; }
}
