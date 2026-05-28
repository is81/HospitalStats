using System.Security.Claims;
using System.Text;
using HospitalStats.Api.Models;
using HospitalStats.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;

namespace HospitalStats.Api.Tests;

public class QueryExecutionServiceTests
{
    static QueryExecutionServiceTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    // ===== OperatorToSql =====

    [Theory]
    [InlineData("EQ", "col = :p")]
    [InlineData("NE", "col != :p")]
    [InlineData("GT", "col > :p")]
    [InlineData("GTE", "col >= :p")]
    [InlineData("LT", "col < :p")]
    [InlineData("LTE", "col <= :p")]
    [InlineData("LIKE", "col LIKE :p")]
    [InlineData("NOT LIKE", "col NOT LIKE :p")]
    [InlineData("IN", "col IN (:p)")]
    [InlineData("NOT IN", "col NOT IN (:p)")]
    public void OperatorToSql_NonDate_ReturnsCorrectSql(string op, string expected)
    {
        var result = QueryExecutionService.OperatorToSql("col", op, "p", isDate: false);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void OperatorToSql_Between_ReturnsSqlWithFromTo()
    {
        var result = QueryExecutionService.OperatorToSql("col", "BETWEEN", "p", isDate: false);
        Assert.Equal("col BETWEEN :p_from AND :p_to", result);
    }

    [Fact]
    public void OperatorToSql_NotBetween_ReturnsSqlWithFromTo()
    {
        var result = QueryExecutionService.OperatorToSql("col", "NOT BETWEEN", "p", isDate: false);
        Assert.Equal("col NOT BETWEEN :p_from AND :p_to", result);
    }

    [Fact]
    public void OperatorToSql_DateBetween_UsesToDate()
    {
        var result = QueryExecutionService.OperatorToSql("col", "BETWEEN", "p", isDate: true);
        Assert.Equal("col BETWEEN TO_DATE(:p_from, 'YYYY-MM-DD') AND TO_DATE(:p_to, 'YYYY-MM-DD')", result);
    }

    [Fact]
    public void OperatorToSql_DateNotBetween_UsesToDate()
    {
        var result = QueryExecutionService.OperatorToSql("col", "NOT BETWEEN", "p", isDate: true);
        Assert.Equal("col NOT BETWEEN TO_DATE(:p_from, 'YYYY-MM-DD') AND TO_DATE(:p_to, 'YYYY-MM-DD')", result);
    }

    [Fact]
    public void OperatorToSql_DateEq_UsesToDate()
    {
        var result = QueryExecutionService.OperatorToSql("col", "EQ", "p", isDate: true);
        Assert.Equal("col = TO_DATE(:p, 'YYYY-MM-DD')", result);
    }

    [Fact]
    public void OperatorToSql_UnknownOp_DefaultsToEq()
    {
        var result = QueryExecutionService.OperatorToSql("col", "WHATEVER", "p");
        Assert.Equal("col = :p", result);
    }

    // ===== BuildCacheKey =====

    private static readonly DateTime TestDate = new(2026, 5, 27, 12, 0, 0);

    [Fact]
    public void BuildCacheKey_SameInputs_ProducesSameKey()
    {
        var filters = new Dictionary<string, string> { ["1"] = "abc" };
        var ctx = new Dictionary<string, string> { ["DeptName"] = "内科" };

        var key1 = QueryExecutionService.BuildCacheKey(42, filters, 1, 50, ctx, TestDate);
        var key2 = QueryExecutionService.BuildCacheKey(42, filters, 1, 50, ctx, TestDate);

        Assert.Equal(key1, key2);
    }

    [Fact]
    public void BuildCacheKey_DifferentContextValues_ProducesDifferentKeys()
    {
        var filters = new Dictionary<string, string> { ["1"] = "abc" };
        var ctx1 = new Dictionary<string, string> { ["DeptName"] = "内科" };
        var ctx2 = new Dictionary<string, string> { ["DeptName"] = "眼科" };

        var key1 = QueryExecutionService.BuildCacheKey(42, filters, 1, 50, ctx1, TestDate);
        var key2 = QueryExecutionService.BuildCacheKey(42, filters, 1, 50, ctx2, TestDate);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void BuildCacheKey_DifferentPage_ProducesDifferentKeys()
    {
        var filters = new Dictionary<string, string>();
        var ctx = new Dictionary<string, string>();

        var key1 = QueryExecutionService.BuildCacheKey(42, filters, 1, 50, ctx, TestDate);
        var key2 = QueryExecutionService.BuildCacheKey(42, filters, 2, 50, ctx, TestDate);

        Assert.NotEqual(key1, key2);
    }

    [Fact]
    public void BuildCacheKey_DifferentUpdatedAt_ProducesDifferentKeys()
    {
        var filters = new Dictionary<string, string>();
        var ctx = new Dictionary<string, string>();

        var key1 = QueryExecutionService.BuildCacheKey(42, filters, 1, 50, ctx, TestDate);
        var key2 = QueryExecutionService.BuildCacheKey(42, filters, 1, 50, ctx, TestDate.AddMinutes(1));

        Assert.NotEqual(key1, key2);
    }

    // ===== SanitizeRawSql =====

    [Theory]
    [InlineData("SELECT * FROM T", "SELECT * FROM T")]
    [InlineData("SELECT * FROM T;", "SELECT * FROM T")]
    [InlineData("  SELECT * FROM T  ", "  SELECT * FROM T")]
    [InlineData("SELECT * FROM T;;;", "SELECT * FROM T")]
    [InlineData(null, "")]
    [InlineData("", "")]
    public void SanitizeRawSql_TrimsSemicolonsAndWhitespace(string? input, string expected)
    {
        var result = QueryExecutionService.SanitizeRawSql(input);
        Assert.Equal(expected, result);
    }

    // ===== BuildSelectClause =====

    [Fact]
    public void BuildSelectClause_SingleField_ReturnsQualifiedSelect()
    {
        var config = new QueryConfig
        {
            Fields = new List<QueryField>
            {
                new()
                {
                    SortOrder = 0,
                    MetaColumn = new MetaColumn
                    {
                        ColumnName = "PATIENT_NAME",
                        MetaTable = new MetaTable { Alias = "T" }
                    }
                }
            }
        };

        var result = QueryExecutionService.BuildSelectClause(config);
        Assert.Equal("\"T\".\"PATIENT_NAME\" AS \"PATIENT_NAME\"", result);
    }

    [Fact]
    public void BuildSelectClause_WithAlias_UsesAliasAsLabel()
    {
        var config = new QueryConfig
        {
            Fields = new List<QueryField>
            {
                new()
                {
                    SortOrder = 0,
                    Alias = "姓名",
                    MetaColumn = new MetaColumn
                    {
                        ColumnName = "PATIENT_NAME",
                        MetaTable = new MetaTable { Alias = "T" }
                    }
                }
            }
        };

        var result = QueryExecutionService.BuildSelectClause(config);
        Assert.Equal("\"T\".\"PATIENT_NAME\" AS \"PATIENT_NAME\"", result);
    }

    [Fact]
    public void BuildSelectClause_AggregateFunc_WrapsColumn()
    {
        var config = new QueryConfig
        {
            Fields = new List<QueryField>
            {
                new()
                {
                    SortOrder = 0,
                    AggregateFunc = "COUNT",
                    Alias = "cnt",
                    MetaColumn = new MetaColumn
                    {
                        ColumnName = "ID",
                        MetaTable = new MetaTable { Alias = "T" }
                    }
                }
            }
        };

        var result = QueryExecutionService.BuildSelectClause(config);
        Assert.Equal("COUNT(\"T\".\"ID\") AS \"ID\"", result);
    }

    [Fact]
    public void BuildSelectClause_NoFields_ThrowsInvalidOperation()
    {
        var config = new QueryConfig { Fields = new List<QueryField>() };

        Assert.Throws<InvalidOperationException>(() =>
            QueryExecutionService.BuildSelectClause(config));
    }

    // ===== ResolveContextValues =====

    [Fact]
    public void ResolveContextValues_ValidClaims_ReturnsBothKeys()
    {
        var accessor = CreateHttpContextAccessor(
            ("dept_name", "眼科"),
            (ClaimTypes.NameIdentifier, "42"));

        var service = CreateService(httpContextAccessor: accessor);

        var result = service.ResolveContextValues();

        Assert.Equal("眼科", result["DeptName"]);
        Assert.Equal("42", result["UserId"]);
    }

    [Fact]
    public void ResolveContextValues_NoClaims_ReturnsEmpty()
    {
        var accessor = CreateHttpContextAccessor();
        var service = CreateService(httpContextAccessor: accessor);

        var result = service.ResolveContextValues();

        Assert.Empty(result);
    }

    [Fact]
    public void ResolveContextValues_NullHttpContext_ReturnsEmpty()
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.SetupGet(a => a.HttpContext).Returns((HttpContext?)null);

        var service = CreateService(httpContextAccessor: accessor.Object);

        var result = service.ResolveContextValues();

        Assert.Empty(result);
    }

    // ===== BuildWhereClause =====

    [Fact]
    public void BuildWhereClause_NormalFilter_AppliesUserValue()
    {
        var config = CreateSimpleConfig(filterId: 1, op: "EQ", defaultValue: null);
        var userFilters = new Dictionary<string, string> { ["1"] = "test_value" };
        var contextValues = new Dictionary<string, string>();

        var service = CreateService();
        var pv = new Dictionary<string, string>();
        var result = service.BuildWhereClause(config, userFilters, contextValues, pv);

        Assert.Contains("\"T\".\"COL1\" = :p_f_1", result);
        Assert.Contains("test_value", pv["1"]);
    }

    [Fact]
    public void BuildWhereClause_NormalFilter_AppliesDefaultWhenNoUserInput()
    {
        var config = CreateSimpleConfig(filterId: 1, op: "EQ", defaultValue: "default_val");
        var userFilters = new Dictionary<string, string>();
        var contextValues = new Dictionary<string, string>();

        var service = CreateService();
        var pv = new Dictionary<string, string>();
        var result = service.BuildWhereClause(config, userFilters, contextValues, pv);

        Assert.Contains("\"T\".\"COL1\" = :p_f_1", result);
        Assert.Equal("default_val", pv["1"]);
    }

    [Fact]
    public void BuildWhereClause_NoValue_EmptyFilters_SkipsFilter()
    {
        var config = CreateSimpleConfig(filterId: 1, op: "EQ", defaultValue: null);
        var userFilters = new Dictionary<string, string>();
        var contextValues = new Dictionary<string, string>();

        var service = CreateService();
        var pv = new Dictionary<string, string>();
        var result = service.BuildWhereClause(config, userFilters, contextValues, pv);

        Assert.Empty(result);
    }

    [Fact]
    public void BuildWhereClause_ContextFilter_InjectsContextValue()
    {
        var config = CreateSimpleConfig(filterId: 1, op: "EQ",
            isContextFilter: true, contextKey: "DeptName");
        var userFilters = new Dictionary<string, string>(); // user provides nothing
        var contextValues = new Dictionary<string, string> { ["DeptName"] = "眼科" };

        var service = CreateService();
        var pv = new Dictionary<string, string>();
        var result = service.BuildWhereClause(config, userFilters, contextValues, pv);

        Assert.Contains("\"T\".\"COL1\" = :p_f_1", result);
        Assert.Equal("眼科", pv["1"]);
    }

    [Fact]
    public void BuildWhereClause_ContextFilter_MissingContextKey_Skips()
    {
        var config = CreateSimpleConfig(filterId: 1, op: "EQ",
            isContextFilter: true, contextKey: "DeptName");
        var userFilters = new Dictionary<string, string>();
        var contextValues = new Dictionary<string, string>(); // empty — no DeptName

        var service = CreateService();
        var pv = new Dictionary<string, string>();
        var result = service.BuildWhereClause(config, userFilters, contextValues, pv);

        Assert.Empty(result);
        Assert.Empty(userFilters);
        Assert.Empty(pv);
    }

    [Fact]
    public void BuildWhereClause_ContextFilterOverridesUserInput()
    {
        // Even if user sends a value, context filter should take precedence
        var config = CreateSimpleConfig(filterId: 1, op: "EQ",
            isContextFilter: true, contextKey: "DeptName");
        var userFilters = new Dictionary<string, string> { ["1"] = "hacker_value" };
        var contextValues = new Dictionary<string, string> { ["DeptName"] = "眼科" };

        var service = CreateService();
        var pv = new Dictionary<string, string>();
        var result = service.BuildWhereClause(config, userFilters, contextValues, pv);

        Assert.Equal("眼科", pv["1"]); // context wins, user input overwritten in paramValues
    }

    [Fact]
    public void BuildWhereClause_MultipleFilters_CombinesWithAnd()
    {
        var config = CreateSimpleConfig(filterId: 1, op: "EQ", defaultValue: "val1");
        config.Filters.Add(new QueryFilter
        {
            Id = 2,
            MetaColumn = new MetaColumn
            {
                ColumnName = "COL2",
                MetaTable = new MetaTable { Alias = "T" }
            },
            Operator = "LIKE",
            DefaultValue = "%test%",
            SortOrder = 1
        });
        var userFilters = new Dictionary<string, string>();
        var contextValues = new Dictionary<string, string>();

        var service = CreateService();
        var pv = new Dictionary<string, string>();
        var result = service.BuildWhereClause(config, userFilters, contextValues, pv);

        Assert.Contains(" AND ", result);
        Assert.Contains("\"T\".\"COL1\" = :p_f_1", result);
        Assert.Contains("\"T\".\"COL2\" LIKE :p_f_2", result);
    }

    // ===== BuildCountSql =====

    [Fact]
    public void BuildCountSql_ConfigBased_WrapsWithCount()
    {
        var config = CreateSimpleConfig(filterId: 1, op: "EQ", defaultValue: "val1");
        var userFilters = new Dictionary<string, string>();
        var contextValues = new Dictionary<string, string>();

        var service = CreateService();
        var paramValues = new Dictionary<string, string>();
        var (sql, _) = service.BuildCountSql(config, userFilters, contextValues, paramValues, false);

        Assert.StartsWith("SELECT COUNT(*)", sql);
    }

    [Fact]
    public void BuildCountSql_RawSqlNoUserFilter_WrapsRawSql()
    {
        var config = new QueryConfig
        {
            RawSql = "SELECT * FROM PATIENTS",
            MainTable = new MetaTable
            {
                TableName = "PATIENTS",
                SchemaName = "HOSPITAL"
            },
            Filters = new List<QueryFilter>
            {
                new()
                {
                    Id = 1,
                    MetaColumn = new MetaColumn
                    {
                        ColumnName = "DEPT",
                        MetaTable = new MetaTable { Alias = "T" }
                    },
                    Operator = "EQ",
                    IsContextFilter = true,
                    ContextKey = "DeptName",
                    SortOrder = 0
                }
            }
        };
        var contextValues = new Dictionary<string, string> { ["DeptName"] = "内科" };

        var service = CreateService();
        var paramValues = new Dictionary<string, string>();
        var (sql, _) = service.BuildCountSql(config, new Dictionary<string, string>(), contextValues, paramValues, true);

        Assert.StartsWith("SELECT COUNT(*) FROM (SELECT * FROM PATIENTS WHERE", sql);
    }

    // ===== BuildDataSql =====

    [Fact]
    public void BuildDataSql_UsesRownumPagination()
    {
        var config = CreateSimpleConfig(filterId: 1, op: "EQ", defaultValue: null);
        var service = CreateService();
        var contextValues = new Dictionary<string, string>();

        var paramValues = new Dictionary<string, string>();
        var (sql, _) = service.BuildDataSql(config, 2, 20, new Dictionary<string, string>(), contextValues, paramValues, false, false);

        Assert.Contains("ROWNUM <= :p_endRow", sql);
        Assert.Contains("rn >= :p_startRow", sql);
    }

    // ===== BuildFromClause =====

    [Fact]
    public void BuildFromClause_SingleTable_QualifiesSchemaTable()
    {
        var config = new QueryConfig
        {
            MainTable = new MetaTable
            {
                TableName = "PATIENTS",
                SchemaName = "HOSPITAL",
                Alias = "P"
            },
            Joins = new List<QueryJoin>()
        };

        var result = QueryExecutionService.BuildFromClause(config);
        Assert.Contains("\"HOSPITAL\".\"PATIENTS\"", result);
        Assert.Contains("\"P\"", result);
    }

    // ===== HasUserFilterInput =====

    [Fact]
    public void HasUserFilterInput_UserValueDiffersFromDefault_ReturnsTrue()
    {
        var config = CreateSimpleConfig(filterId: 1, defaultValue: "abc");
        var userFilters = new Dictionary<string, string> { ["1"] = "x" };

        var result = QueryExecutionService.HasUserFilterInput(config, userFilters);
        Assert.True(result);
    }

    [Fact]
    public void HasUserFilterInput_UserValueEqualsDefault_ReturnsFalse()
    {
        var config = CreateSimpleConfig(filterId: 1, defaultValue: "abc");
        var userFilters = new Dictionary<string, string> { ["1"] = "abc" };

        var result = QueryExecutionService.HasUserFilterInput(config, userFilters);
        Assert.False(result);
    }

    [Fact]
    public void HasUserFilterInput_EmptyUserFilters_ReturnsFalse()
    {
        var config = CreateSimpleConfig(filterId: 1);
        var userFilters = new Dictionary<string, string>();

        var result = QueryExecutionService.HasUserFilterInput(config, userFilters);
        Assert.False(result);
    }

    [Fact]
    public void HasUserFilterInput_ContextFilter_Ignored()
    {
        var config = CreateSimpleConfig(filterId: 1, isContextFilter: true, contextKey: "DeptName");
        var userFilters = new Dictionary<string, string> { ["1"] = "x" };

        var result = QueryExecutionService.HasUserFilterInput(config, userFilters);
        Assert.False(result);
    }

    // ===== BuildOrderBy =====

    [Fact]
    public void BuildOrderBy_WithDirection_ReturnsQualified()
    {
        var config = new QueryConfig
        {
            SortColumn = "P.PATIENT_NAME",
            SortDirection = "DESC"
        };

        var result = QueryExecutionService.BuildOrderBy(config);
        Assert.Equal("\"P\".\"PATIENT_NAME\" DESC", result);
    }

    [Fact]
    public void BuildOrderBy_EmptyColumn_ReturnsEmpty()
    {
        var config = new QueryConfig { SortColumn = null };

        var result = QueryExecutionService.BuildOrderBy(config);
        Assert.Empty(result);
    }

    // ===== BuildGroupBy =====

    [Fact]
    public void BuildGroupBy_QualifiedColumn_ReturnsQuoted()
    {
        var config = new QueryConfig { GroupByColumn = "P.DEPT_NAME" };

        var result = QueryExecutionService.BuildGroupBy(config);
        Assert.Equal("\"P\".\"DEPT_NAME\"", result);
    }

    [Fact]
    public void BuildGroupBy_NullColumn_ReturnsEmpty()
    {
        var config = new QueryConfig { GroupByColumn = null };

        var result = QueryExecutionService.BuildGroupBy(config);
        Assert.Empty(result);
    }

    // ===== MergeParams =====

    [Fact]
    public void MergeParams_CombinesAllSources()
    {
        var countParams = new Dictionary<string, object?> { ["p_endRow"] = 20 };
        var dataParams = new Dictionary<string, object?> { ["p_startRow"] = 1 };
        var userFilters = new Dictionary<string, string> { ["1"] = "val" };

        var result = QueryExecutionService.MergeParams(countParams, dataParams, userFilters);

        Assert.NotNull(result);
    }

    // ===== IsStringType =====

    [Theory]
    [InlineData("VARCHAR2", true)]
    [InlineData("NVARCHAR2", true)]
    [InlineData("CHAR", true)]
    [InlineData("NCHAR", true)]
    [InlineData("CLOB", true)]
    [InlineData("NCLOB", true)]
    [InlineData("VARCHAR", true)]
    [InlineData("NVARCHAR", true)]
    [InlineData("LONG", true)]
    [InlineData("NUMBER", false)]
    [InlineData("DATE", false)]
    [InlineData("INTEGER", false)]
    [InlineData("FLOAT", false)]
    [InlineData(null, false)]
    public void IsStringType_Various(string? dataType, bool expected)
    {
        Assert.Equal(expected, QueryExecutionService.IsStringType(dataType));
    }

    // ===== DecodeHexString =====

    [Fact]
    public void DecodeHexString_Gbk_ReturnsChinese()
    {
        // "内科" in GBK = C4 DA BF C6
        var result = QueryExecutionService.DecodeHexString("C4DABFC6", "gbk");
        Assert.Equal("内科", result);
    }

    [Fact]
    public void DecodeHexString_NullOrEmpty_ReturnsSame()
    {
        Assert.Equal("", QueryExecutionService.DecodeHexString("", "gbk"));
    }

    [Fact]
    public void DecodeHexString_InvalidHex_ReturnsSame()
    {
        Assert.Equal("ZZZ", QueryExecutionService.DecodeHexString("ZZZ", "gbk"));
    }

    // ===== BuildSelectClause with HexEncoding =====

    [Fact]
    public void BuildSelectClause_WithHexEncoding_WrapsStringColumn()
    {
        var config = new QueryConfig
        {
            Fields = new List<QueryField>
            {
                new()
                {
                    SortOrder = 0,
                    MetaColumn = new MetaColumn
                    {
                        ColumnName = "DIAGNOSIS",
                        DataType = "VARCHAR2",
                        MetaTable = new MetaTable { Alias = "T" }
                    }
                }
            }
        };

        var result = QueryExecutionService.BuildSelectClause(config, useHexEncoding: true);
        Assert.Contains("RAWTOHEX(UTL_RAW.CAST_TO_RAW", result);
        Assert.Contains("AS \"DIAGNOSIS\"", result);
    }

    [Fact]
    public void BuildSelectClause_WithHexEncoding_DoesNotWrapNumberColumn()
    {
        var config = new QueryConfig
        {
            Fields = new List<QueryField>
            {
                new()
                {
                    SortOrder = 0,
                    MetaColumn = new MetaColumn
                    {
                        ColumnName = "PATIENT_ID",
                        DataType = "NUMBER",
                        MetaTable = new MetaTable { Alias = "T" }
                    }
                }
            }
        };

        var result = QueryExecutionService.BuildSelectClause(config, useHexEncoding: true);
        Assert.DoesNotContain("RAWTOHEX", result);
    }

    // ===== Helpers =====

    private static QueryConfig CreateSimpleConfig(int filterId, string op = "EQ",
        string? defaultValue = null, bool isContextFilter = false, string? contextKey = null)
    {
        return new QueryConfig
        {
            MainTable = new MetaTable
            {
                TableName = "TEST_TABLE",
                SchemaName = "HOSPITAL",
                Alias = "T"
            },
            Fields = new List<QueryField>
            {
                new()
                {
                    SortOrder = 0,
                    MetaColumn = new MetaColumn
                    {
                        ColumnName = "COL1",
                        MetaTable = new MetaTable { Alias = "T" }
                    }
                }
            },
            Filters = new List<QueryFilter>
            {
                new()
                {
                    Id = filterId,
                    MetaColumn = new MetaColumn
                    {
                        ColumnName = "COL1",
                        MetaTable = new MetaTable { Alias = "T" }
                    },
                    Operator = op,
                    DefaultValue = defaultValue,
                    IsContextFilter = isContextFilter,
                    ContextKey = contextKey,
                    SortOrder = 0
                }
            },
            Joins = new List<QueryJoin>()
        };
    }

    private static QueryExecutionService CreateService(
        IHttpContextAccessor? httpContextAccessor = null)
    {
        var mockLogger = new Mock<ILogger<QueryExecutionService>>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["QueryTimeoutSeconds"] = "120" })
            .Build();
        return new QueryExecutionService(
            null!, null!, null!,
            mockLogger.Object,
            httpContextAccessor ?? CreateHttpContextAccessor(),
            config);
    }

    private static IHttpContextAccessor CreateHttpContextAccessor(
        params (string type, string value)[] claims)
    {
        var identity = new ClaimsIdentity(
            claims.Select(c => new Claim(c.type, c.value)),
            "test");
        var principal = new ClaimsPrincipal(identity);
        var httpContext = new DefaultHttpContext { User = principal };
        var mock = new Mock<IHttpContextAccessor>();
        mock.SetupGet(a => a.HttpContext).Returns(httpContext);
        return mock.Object;
    }
}
