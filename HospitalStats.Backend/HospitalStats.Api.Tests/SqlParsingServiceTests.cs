using HospitalStats.Api.DTOs;
using HospitalStats.Api.Models;
using HospitalStats.Api.Services;

namespace HospitalStats.Api.Tests;

public class SqlParsingServiceTests
{
    // ===== RemoveComments =====

    [Theory]
    [InlineData("SELECT * FROM T", "SELECT * FROM T")]
    [InlineData("SELECT * -- comment\nFROM T", "SELECT *  \nFROM T")]
    [InlineData("SELECT /* inline */ * FROM T", "SELECT   * FROM T")]
    [InlineData("/* block\ncomment */SELECT 1", " SELECT 1")]
    public void RemoveComments_StripsComments(string input, string expected)
    {
        var result = SqlParsingService.RemoveComments(input);
        Assert.Equal(expected, result);
    }

    // ===== NormalizeWhitespace =====

    [Fact]
    public void NormalizeWhitespace_CollapsesMultipleSpaces()
    {
        var result = SqlParsingService.NormalizeWhitespace("SELECT    *   FROM   T");
        Assert.Equal("SELECT * FROM T", result);
    }

    [Fact]
    public void NormalizeWhitespace_TrimsLeadingAndTrailing()
    {
        var result = SqlParsingService.NormalizeWhitespace("  SELECT * FROM T  ");
        Assert.Equal("SELECT * FROM T", result);
    }

    [Fact]
    public void NormalizeWhitespace_NewlinesToSpaces()
    {
        var result = SqlParsingService.NormalizeWhitespace("SELECT\n*\nFROM\nT");
        Assert.Equal("SELECT * FROM T", result);
    }

    // ===== ParseTableRefs =====

    [Fact]
    public void ParseTableRefs_QuotedSchemaTableAlias()
    {
        var result = SqlParsingService.ParseTableRefs("\"HOSPITAL\".\"PATIENTS\" \"P\"");

        Assert.Single(result);
        Assert.Equal("P", result[0].Alias);
        Assert.Equal("PATIENTS", result[0].TableName);
    }

    [Fact]
    public void ParseTableRefs_QuotedSchemaTableNoAlias()
    {
        var result = SqlParsingService.ParseTableRefs("\"HOSPITAL\".\"PATIENTS\"");

        Assert.Single(result);
        Assert.Equal("PATIENTS", result[0].Alias);
        Assert.Equal("PATIENTS", result[0].TableName);
    }

    [Fact]
    public void ParseTableRefs_UnquotedWithAlias()
    {
        var result = SqlParsingService.ParseTableRefs("HOSPITAL.PATIENTS P");

        Assert.Single(result);
        Assert.Equal("P", result[0].Alias);
        Assert.Equal("PATIENTS", result[0].TableName);
    }

    [Fact]
    public void ParseTableRefs_SingleWordTable()
    {
        var result = SqlParsingService.ParseTableRefs("PATIENTS");

        Assert.Single(result);
        Assert.Equal("PATIENTS", result[0].Alias);
        Assert.Equal("PATIENTS", result[0].TableName);
    }

    [Fact]
    public void ParseTableRefs_JoinClause_ExtractsBothTables()
    {
        var fromClause = "\"HOSPITAL\".\"PATIENTS\" \"P\" LEFT JOIN \"HOSPITAL\".\"VISITS\" \"V\"";

        var result = SqlParsingService.ParseTableRefs(fromClause);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.Alias == "P" && r.TableName == "PATIENTS");
        Assert.Contains(result, r => r.Alias == "V" && r.TableName == "VISITS");
    }

    [Fact]
    public void ParseTableRefs_CommaSeparatedTables()
    {
        var fromClause = "PATIENTS P, VISITS V";

        var result = SqlParsingService.ParseTableRefs(fromClause);

        Assert.Equal(2, result.Count);
    }

    // ===== ParseSelectColumns =====

    [Fact]
    public void ParseSelectColumns_QualifiedColumn_Matched()
    {
        var selectClause = "\"P\".\"PATIENT_NAME\" AS \"姓名\"";
        var columns = new List<MetaColumn>
        {
            new()
            {
                ColumnName = "PATIENT_NAME",
                MetaTable = new MetaTable { Alias = "P" }
            }
        };
        var aliasMap = new Dictionary<string, string> { ["P"] = "PATIENTS" };

        var result = SqlParsingService.ParseSelectColumns(selectClause, columns, aliasMap);

        Assert.Single(result);
        Assert.True(result[0].Matched);
        Assert.Equal("姓名", result[0].Alias);
    }

    [Fact]
    public void ParseSelectColumns_UnmatchedColumn_NotMatched()
    {
        var selectClause = "\"P\".\"UNKNOWN_COL\"";
        var columns = new List<MetaColumn>();
        var aliasMap = new Dictionary<string, string> { ["P"] = "PATIENTS" };

        var result = SqlParsingService.ParseSelectColumns(selectClause, columns, aliasMap);

        Assert.Single(result);
        Assert.False(result[0].Matched);
    }

    [Fact]
    public void ParseSelectColumns_AggregateFunction_ExtractsFuncAndInner()
    {
        var selectClause = "COUNT(\"P\".\"ID\")"; // the alias is handled by ParseSelectColumns, not ExtractAggregateFunc
        var columns = new List<MetaColumn>
        {
            new()
            {
                ColumnName = "ID",
                MetaTable = new MetaTable { Alias = "P" }
            }
        };
        var aliasMap = new Dictionary<string, string> { ["P"] = "PATIENTS" };

        var result = SqlParsingService.ParseSelectColumns(selectClause, columns, aliasMap);

        Assert.Single(result);
        Assert.Equal("COUNT", result[0].AggregateFunc);
    }

    [Fact]
    public void ParseSelectColumns_MultipleColumns_CorrectCount()
    {
        var selectClause = "\"P\".\"COL1\", \"P\".\"COL2\" AS \"c2\"";
        var columns = new List<MetaColumn>
        {
            new() { ColumnName = "COL1", MetaTable = new MetaTable { Alias = "P" } },
            new() { ColumnName = "COL2", MetaTable = new MetaTable { Alias = "P" } }
        };
        var aliasMap = new Dictionary<string, string> { ["P"] = "PATIENTS" };

        var result = SqlParsingService.ParseSelectColumns(selectClause, columns, aliasMap);

        Assert.Equal(2, result.Count);
    }

    // ===== ParseWhereFilters =====

    [Fact]
    public void ParseWhereFilters_SkipJoinConditions_EqualityOnly()
    {
        // This is a join condition (alias.col = alias.col), not a filter
        var whereClause = "\"P\".\"ID\" = \"V\".\"PATIENT_ID\"";
        var columns = new List<MetaColumn>();
        var aliasMap = new Dictionary<string, string> { ["P"] = "PATIENTS", ["V"] = "VISITS" };

        var result = SqlParsingService.ParseWhereFilters(whereClause, columns, aliasMap);

        Assert.Empty(result); // join conditions are skipped
    }

    [Fact]
    public void ParseWhereFilters_SimpleEq_WithValue()
    {
        var whereClause = "\"P\".\"DEPT\" = '内科'";
        var columns = new List<MetaColumn>
        {
            new()
            {
                ColumnName = "DEPT",
                MetaTable = new MetaTable { Alias = "P" }
            }
        };
        var aliasMap = new Dictionary<string, string> { ["P"] = "PATIENTS" };

        var result = SqlParsingService.ParseWhereFilters(whereClause, columns, aliasMap);

        Assert.Single(result);
        Assert.Equal("EQ", result[0].Operator);
        Assert.Equal("内科", result[0].DefaultValue);
        Assert.True(result[0].Matched);
    }

    [Fact]
    public void ParseWhereFilters_Like_ExtractsPattern()
    {
        var whereClause = "\"P\".\"NAME\" LIKE '%张%'";
        var columns = new List<MetaColumn>
        {
            new()
            {
                ColumnName = "NAME",
                MetaTable = new MetaTable { Alias = "P" }
            }
        };
        var aliasMap = new Dictionary<string, string> { ["P"] = "PATIENTS" };

        var result = SqlParsingService.ParseWhereFilters(whereClause, columns, aliasMap);

        Assert.Single(result);
        Assert.Equal("LIKE", result[0].Operator);
        Assert.Equal("%张%", result[0].DefaultValue);
    }

    [Fact]
    public void ParseWhereFilters_NotLike_ExtractsPattern()
    {
        var whereClause = "\"P\".\"NAME\" NOT LIKE '%test%'";
        var columns = new List<MetaColumn>
        {
            new()
            {
                ColumnName = "NAME",
                MetaTable = new MetaTable { Alias = "P" }
            }
        };
        var aliasMap = new Dictionary<string, string> { ["P"] = "PATIENTS" };

        var result = SqlParsingService.ParseWhereFilters(whereClause, columns, aliasMap);

        Assert.Single(result);
        Assert.Equal("NOT LIKE", result[0].Operator);
    }

    [Fact]
    public void ParseWhereFilters_MultipleConditions_SplitByAnd()
    {
        var whereClause = "\"P\".\"DEPT\" = '内科' AND \"P\".\"AGE\" > '18'";
        var columns = new List<MetaColumn>
        {
            new() { ColumnName = "DEPT", MetaTable = new MetaTable { Alias = "P" } },
            new() { ColumnName = "AGE", MetaTable = new MetaTable { Alias = "P" } }
        };
        var aliasMap = new Dictionary<string, string> { ["P"] = "PATIENTS" };

        var result = SqlParsingService.ParseWhereFilters(whereClause, columns, aliasMap);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void ParseWhereFilters_DateToDate_ExtractsDateFormat()
    {
        var whereClause = "\"P\".\"VISIT_DATE\" = TO_DATE('2024-01-15', 'YYYY-MM-DD')";
        var columns = new List<MetaColumn>
        {
            new()
            {
                ColumnName = "VISIT_DATE",
                DataType = "DATE",
                MetaTable = new MetaTable { Alias = "P" }
            }
        };
        var aliasMap = new Dictionary<string, string> { ["P"] = "PATIENTS" };

        var result = SqlParsingService.ParseWhereFilters(whereClause, columns, aliasMap);

        Assert.Single(result);
        Assert.Equal("2024-01-15", result[0].DefaultValue);
    }

    // ===== ParseJoins =====

    [Fact]
    public void ParseJoins_SimpleEqualityJoin_DetectsJoin()
    {
        var whereClause = "\"P\".\"ID\" = \"V\".\"PATIENT_ID\"";
        var columns = new List<MetaColumn>
        {
            new()
            {
                ColumnName = "ID",
                MetaTable = new MetaTable { Alias = "P", TableName = "PATIENTS" }
            },
            new()
            {
                ColumnName = "PATIENT_ID",
                MetaTable = new MetaTable { Alias = "V", TableName = "VISITS" }
            }
        };
        var aliasMap = new Dictionary<string, string> { ["P"] = "PATIENTS", ["V"] = "VISITS" };
        var tableMap = new Dictionary<string, MetaTable>
        {
            ["PATIENTS"] = new() { Id = 1, TableName = "PATIENTS" },
            ["VISITS"] = new() { Id = 2, TableName = "VISITS" }
        };

        var result = SqlParsingService.ParseJoins(whereClause, columns, aliasMap, tableMap);

        Assert.Single(result);
        Assert.Equal("VISITS", result[0].JoinTableName);
        Assert.Equal("INNER", result[0].JoinType);
    }

    [Fact]
    public void ParseJoins_SingleTable_NoJoin()
    {
        var whereClause = "\"P\".\"DEPT\" = '内科'";
        var columns = new List<MetaColumn>();
        var aliasMap = new Dictionary<string, string> { ["P"] = "PATIENTS" };
        var tableMap = new Dictionary<string, MetaTable>();

        var result = SqlParsingService.ParseJoins(whereClause, columns, aliasMap, tableMap);

        Assert.Empty(result);
    }

    // ===== SplitConditions =====

    [Fact]
    public void SplitConditions_SplitsOnAndOutsideQuotes()
    {
        var whereClause = "P.DEPT = '内 科' AND P.AGE > '18'";

        var result = SqlParsingService.SplitConditions(whereClause);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void SplitConditions_AndInsideQuotes_NotSplit()
    {
        var whereClause = "P.DEPT = 'AND' AND P.AGE > '18'";

        var result = SqlParsingService.SplitConditions(whereClause);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void SplitConditions_Parentheses_RespectDepth()
    {
        var whereClause = "(P.A = '1' AND P.B = '2') AND P.C = '3'";

        var result = SqlParsingService.SplitConditions(whereClause);

        Assert.Equal(2, result.Count);
    }

    // ===== SplitRespectingNesting =====

    [Fact]
    public void SplitRespectingNesting_SimpleComma_Split()
    {
        var parts = SqlParsingService.SplitRespectingNesting("a, b, c", ',');
        Assert.Equal(3, parts.Count);
    }

    [Fact]
    public void SplitRespectingNesting_QuotedComma_NotSplit()
    {
        var parts = SqlParsingService.SplitRespectingNesting("'a,b', c", ',');
        Assert.Equal(2, parts.Count);
    }

    [Fact]
    public void SplitRespectingNesting_ParenthesizedComma_NotSplit()
    {
        var parts = SqlParsingService.SplitRespectingNesting("FN(a, b), c", ',');
        Assert.Equal(2, parts.Count);
    }

    // ===== ExtractBetween =====

    [Fact]
    public void ExtractBetween_SelectToFrom()
    {
        var sql = "SELECT a, b FROM T WHERE x = 1";
        var result = SqlParsingService.ExtractBetween(sql, "SELECT", "FROM");

        Assert.Equal("a, b", result);
    }

    [Fact]
    public void ExtractBetween_WhereToGroupBy()
    {
        var sql = "SELECT a FROM T WHERE x = 1 GROUP BY a ORDER BY a";
        var result = SqlParsingService.ExtractBetween(sql, "WHERE", @"GROUP\s+BY|ORDER\s+BY|HAVING|$");

        Assert.Equal("x = 1", result);
    }

    // ===== ExtractAggregateFunc =====

    [Theory]
    [InlineData("COUNT(\"P\".\"ID\")", "COUNT", "\"P\".\"ID\"")]
    [InlineData("SUM(\"P\".\"AMOUNT\")", "SUM", "\"P\".\"AMOUNT\"")]
    [InlineData("AVG(P.VALUE)", "AVG", "P.VALUE")]
    [InlineData("MAX(P.DATE)", "MAX", "P.DATE")]
    [InlineData("MIN(P.DATE)", "MIN", "P.DATE")]
    [InlineData("P.COL1", null, "P.COL1")]
    public void ExtractAggregateFunc_ReturnsCorrectResult(
        string input, string? expectedFunc, string expectedInner)
    {
        var (func, inner) = SqlParsingService.ExtractAggregateFunc(input);

        Assert.Equal(expectedFunc, func);
        Assert.Equal(expectedInner, inner);
    }

    // ===== NormalizeOperator =====

    [Theory]
    [InlineData("=", "EQ")]
    [InlineData("!=", "NE")]
    [InlineData("<>", "NE")]
    [InlineData(">", "GT")]
    [InlineData(">=", "GTE")]
    [InlineData("<", "LT")]
    [InlineData("<=", "LTE")]
    [InlineData("LIKE", "LIKE")]
    [InlineData("NOT LIKE", "NOT LIKE")]
    [InlineData("IN", "IN")]
    [InlineData("NOT IN", "NOT IN")]
    [InlineData("BETWEEN", "BETWEEN")]
    [InlineData("NOT BETWEEN", "NOT BETWEEN")]
    public void NormalizeOperator_MapsCorrectly(string input, string expected)
    {
        var result = SqlParsingService.NormalizeOperator(input);
        Assert.Equal(expected, result);
    }

    // ===== ExtractDefaultValue =====

    [Theory]
    [InlineData("'hello'", "hello")]
    [InlineData("'内 科'", "内 科")]
    [InlineData("TO_DATE('2024-01-01', 'YYYY-MM-DD')", "2024-01-01")]
    [InlineData("123", "123")]
    [InlineData("", null)]
    public void ExtractDefaultValue_ReturnsCorrectValue(string input, string? expected)
    {
        var result = SqlParsingService.ExtractDefaultValue(input);
        Assert.Equal(expected, result);
    }

    // ===== Helper methods =====

    [Theory]
    [InlineData("hello", true)]
    [InlineData("_valid", true)]
    [InlineData("A1", true)]
    [InlineData("1invalid", false)]
    [InlineData("", false)]
    public void IsValidIdentifier_ChecksCorrectly(string input, bool expected)
    {
        Assert.Equal(expected, SqlParsingService.IsValidIdentifier(input));
    }

    [Theory]
    [InlineData("func(a,b)", true)]
    [InlineData("func(a,b", false)]
    [InlineData(")a(", false)]
    [InlineData("no parens", true)]
    public void HasBalancedParentheses_ChecksCorrectly(string input, bool expected)
    {
        Assert.Equal(expected, SqlParsingService.HasBalancedParentheses(input));
    }

    [Theory]
    [InlineData("func('hello world')", true)]
    [InlineData("unclosed 'quote", false)]
    [InlineData("no quotes", true)]
    [InlineData("'nested''quote'", true)]
    public void HasBalancedQuotes_ChecksCorrectly(string input, bool expected)
    {
        Assert.Equal(expected, SqlParsingService.HasBalancedQuotes(input));
    }

    // ===== Complex SELECT column parsing =====

    [Fact]
    public void ParseSelectColumns_FunctionWithImplicitAlias_ExtractsAlias()
    {
        var selectClause = "to_char(P.BILLING_DATE, 'yyyy') sj";
        var columns = new List<MetaColumn>
        {
            new() { ColumnName = "BILLING_DATE", MetaTable = new MetaTable { Alias = "P" } }
        };
        var aliasMap = new Dictionary<string, string> { ["P"] = "PATIENTS" };

        var result = SqlParsingService.ParseSelectColumns(selectClause, columns, aliasMap);

        Assert.Single(result);
        Assert.Equal("sj", result[0].Alias);
        Assert.True(result[0].Matched);
    }

    [Fact]
    public void ParseSelectColumns_AggregateCaseWithImplicitAlias_ExtractsBoth()
    {
        var selectClause = "sum(case when a.item_class='A' then a.costs end) yp";
        var columns = new List<MetaColumn>
        {
            new() { ColumnName = "ITEM_CLASS", MetaTable = new MetaTable { Alias = "A" } },
            new() { ColumnName = "COSTS", MetaTable = new MetaTable { Alias = "A" } }
        };
        var aliasMap = new Dictionary<string, string> { ["A"] = "INP_BILL_DETAIL" };

        var result = SqlParsingService.ParseSelectColumns(selectClause, columns, aliasMap);

        Assert.Single(result);
        Assert.Equal("yp", result[0].Alias);
        Assert.Equal("SUM", result[0].AggregateFunc);
        Assert.True(result[0].Matched);
    }

    [Fact]
    public void ParseSelectColumns_QuotedChineseAlias_ExtractsAlias()
    {
        var selectClause = "sum(a.costs) \"总费用\"";
        var columns = new List<MetaColumn>
        {
            new() { ColumnName = "COSTS", MetaTable = new MetaTable { Alias = "A" } }
        };
        var aliasMap = new Dictionary<string, string> { ["A"] = "INP_BILL_DETAIL" };

        var result = SqlParsingService.ParseSelectColumns(selectClause, columns, aliasMap);

        Assert.Single(result);
        Assert.Equal("总费用", result[0].Alias);
        Assert.Equal("SUM", result[0].AggregateFunc);
        Assert.True(result[0].Matched);
    }

    [Fact]
    public void ParseSelectColumns_NestedFunctionQuotedAlias_MatchesColumn()
    {
        var selectClause = "trunc(months_between(cm.ADMISSION_DATE, a.DATE_OF_BIRTH)/12) \"年龄\"";
        var columns = new List<MetaColumn>
        {
            new() { ColumnName = "ADMISSION_DATE", MetaTable = new MetaTable { Alias = "CM" } },
            new() { ColumnName = "DATE_OF_BIRTH", MetaTable = new MetaTable { Alias = "A" } }
        };
        var aliasMap = new Dictionary<string, string> { ["CM"] = "PAT_VISIT", ["A"] = "PAT_MASTER_INDEX" };

        var result = SqlParsingService.ParseSelectColumns(selectClause, columns, aliasMap);

        Assert.Single(result);
        Assert.Equal("年龄", result[0].Alias);
        Assert.True(result[0].Matched);
    }

    [Fact]
    public void ParseSelectColumns_FunctionNoAlias_MatchesColumnInExpr()
    {
        var selectClause = "to_char(P.BILLING_DATE, 'yyyy')";
        var columns = new List<MetaColumn>
        {
            new() { ColumnName = "BILLING_DATE", MetaTable = new MetaTable { Alias = "P" } }
        };
        var aliasMap = new Dictionary<string, string> { ["P"] = "PATIENTS" };

        var result = SqlParsingService.ParseSelectColumns(selectClause, columns, aliasMap);

        Assert.Single(result);
        Assert.True(result[0].Matched);
    }

    [Fact]
    public void ParseSelectColumns_SimpleQualifiedAlias_StillWorks()
    {
        // Verify existing behavior still works after reorder
        var selectClause = "\"P\".\"PATIENT_NAME\" AS \"姓名\"";
        var columns = new List<MetaColumn>
        {
            new() { ColumnName = "PATIENT_NAME", MetaTable = new MetaTable { Alias = "P" } }
        };
        var aliasMap = new Dictionary<string, string> { ["P"] = "PATIENTS" };

        var result = SqlParsingService.ParseSelectColumns(selectClause, columns, aliasMap);

        Assert.Single(result);
        Assert.Equal("姓名", result[0].Alias);
        Assert.True(result[0].Matched);
    }

    [Fact]
    public void ParseSelectColumns_AggregateAsAlias_StillWorks()
    {
        // Verify COUNT(P.ID) still works after reorder
        var selectClause = "COUNT(\"P\".\"ID\")";
        var columns = new List<MetaColumn>
        {
            new() { ColumnName = "ID", MetaTable = new MetaTable { Alias = "P" } }
        };
        var aliasMap = new Dictionary<string, string> { ["P"] = "PATIENTS" };

        var result = SqlParsingService.ParseSelectColumns(selectClause, columns, aliasMap);

        Assert.Single(result);
        Assert.Equal("COUNT", result[0].AggregateFunc);
        Assert.True(result[0].Matched);
    }

    // ===== MatchColumnInExpr =====

    [Fact]
    public void MatchColumnInExpr_FindsColumnInFunctionArgs()
    {
        var columns = new List<MetaColumn>
        {
            new() { ColumnName = "BILLING_DATE", MetaTable = new MetaTable { Alias = "A", TableName = "INP_BILL_DETAIL" } }
        };
        var aliasMap = new Dictionary<string, string> { ["a"] = "INP_BILL_DETAIL" };

        var result = SqlParsingService.MatchColumnInExpr("to_char(a.billing_date, 'yyyy')", columns, aliasMap);

        Assert.NotNull(result);
        Assert.Equal("BILLING_DATE", result!.ColumnName);
    }

    [Fact]
    public void MatchColumnInExpr_NoMatch_ReturnsNull()
    {
        var columns = new List<MetaColumn>();
        var aliasMap = new Dictionary<string, string>();

        var result = SqlParsingService.MatchColumnInExpr("some_func(x, y)", columns, aliasMap);

        Assert.Null(result);
    }

    // ===== UNION and subquery handling =====

    [Fact]
    public void ParseWhereFilters_SkipsSubqueryConditions()
    {
        var whereClause = "\"P\".\"DEPT\" = '内科' AND \"P\".\"ID\" IN (SELECT patient_id FROM pat_master_index WHERE id_no = '123')";
        var columns = new List<MetaColumn>
        {
            new() { ColumnName = "DEPT", MetaTable = new MetaTable { Alias = "P" } }
        };
        var aliasMap = new Dictionary<string, string> { ["P"] = "PATIENTS" };

        var result = SqlParsingService.ParseWhereFilters(whereClause, columns, aliasMap);

        // Only the simple condition should be parsed; subquery condition is skipped
        Assert.Single(result);
        Assert.Equal("EQ", result[0].Operator);
        Assert.Equal("内科", result[0].DefaultValue);
    }

    // ===== ParseWhereFilters: no space after operator =====

    [Fact]
    public void ParseWhereFilters_NoSpaceAfterOperator_Gte()
    {
        var whereClause = "\"A\".\"BILLING_DATE_TIME\">=TO_DATE('2026-05-01','YYYY-MM-DD')";
        var columns = new List<MetaColumn>
        {
            new()
            {
                ColumnName = "BILLING_DATE_TIME",
                MetaTable = new MetaTable { Alias = "A" }
            }
        };
        var aliasMap = new Dictionary<string, string> { ["A"] = "INP_BILL_DETAIL" };

        var result = SqlParsingService.ParseWhereFilters(whereClause, columns, aliasMap);

        Assert.Single(result);
        Assert.Equal("GTE", result[0].Operator);
        Assert.Equal("2026-05-01", result[0].DefaultValue);
        Assert.True(result[0].Matched);
    }

    [Fact]
    public void ParseWhereFilters_NoSpaceAfterOperator_Lt()
    {
        var whereClause = "\"A\".\"BILLING_DATE_TIME\" <TO_DATE('2026-06-01','YYYY-MM-DD')";
        var columns = new List<MetaColumn>
        {
            new()
            {
                ColumnName = "BILLING_DATE_TIME",
                MetaTable = new MetaTable { Alias = "A" }
            }
        };
        var aliasMap = new Dictionary<string, string> { ["A"] = "INP_BILL_DETAIL" };

        var result = SqlParsingService.ParseWhereFilters(whereClause, columns, aliasMap);

        Assert.Single(result);
        Assert.Equal("LT", result[0].Operator);
        Assert.Equal("2026-06-01", result[0].DefaultValue);
        Assert.True(result[0].Matched);
    }
}
