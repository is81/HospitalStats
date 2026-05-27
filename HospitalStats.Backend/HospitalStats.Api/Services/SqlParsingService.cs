using System.Text.RegularExpressions;
using HospitalStats.Api.Data;
using HospitalStats.Api.DTOs;
using HospitalStats.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HospitalStats.Api.Services;

public class SqlParsingService
{
    private readonly AppDbContext _db;

    public SqlParsingService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<SqlParseResponse> ParseAsync(string sql)
    {
        sql = RemoveComments(sql);
        sql = NormalizeWhitespace(sql);
        sql = sql.TrimEnd(';').TrimEnd();

        if (Regex.IsMatch(sql, @"\bSELECT\s+\*", RegexOptions.IgnoreCase))
            throw new ArgumentException("不支持 SELECT *，请展开为具体列名");

        if (!Regex.IsMatch(sql, @"\bSELECT\b", RegexOptions.IgnoreCase))
            throw new ArgumentException("请输入有效的 SELECT 查询语句");

        var selectPart = ExtractBetween(sql, "SELECT", "FROM");
        var fromPart = ExtractBetween(sql, "FROM", @"WHERE|GROUP\s+BY|ORDER\s+BY|HAVING|$");
        var wherePart = ExtractBetween(sql, "WHERE", @"GROUP\s+BY|ORDER\s+BY|HAVING|$");
        var groupByPart = ExtractBetween(sql, @"GROUP\s+BY", @"ORDER\s+BY|HAVING|$");
        var orderByPart = ExtractBetween(sql, @"ORDER\s+BY", "$");

        var response = new SqlParseResponse
        {
            RawSql = sql  // cleaned original SQL for direct execution
        };

        // parse table references from FROM clause
        var tableRefs = ParseTableRefs(fromPart);

        // load MetaTable entries for all referenced tables (case-insensitive)
        var tableMap = new Dictionary<string, MetaTable>(StringComparer.OrdinalIgnoreCase);
        foreach (var (alias, tableName) in tableRefs)
        {
            if (tableMap.ContainsKey(tableName)) continue;
            var metaTable = await _db.MetaTables
                .FirstOrDefaultAsync(t => t.TableName.ToUpper() == tableName.ToUpper());
            if (metaTable != null)
                tableMap[tableName] = metaTable;
        }

        // set main table
        if (tableRefs.Count > 0)
        {
            var first = tableRefs.First();
            if (tableMap.TryGetValue(first.TableName, out var mainMeta))
            {
                response.MainTableId = mainMeta.Id;
                response.MainTableName = mainMeta.Alias ?? mainMeta.TableName;
            }
            else
            {
                response.MainTableName = first.TableName;
            }
        }

        // build alias→tableName map (alias → actual table stored name)
        var aliasToTableName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (alias, tableName) in tableRefs)
        {
            if (tableMap.TryGetValue(tableName, out var mt))
                aliasToTableName[alias] = mt.TableName!;
            else
                aliasToTableName[alias] = tableName;
        }

        // load columns for all matched tables
        var allColumns = new List<MetaColumn>();
        foreach (var mt in tableMap.Values)
        {
            var cols = await _db.MetaColumns
                .Include(c => c.MetaTable)
                .Where(c => c.MetaTableId == mt.Id)
                .ToListAsync();
            allColumns.AddRange(cols);
        }

        // parse SELECT columns
        response.Columns = ParseSelectColumns(selectPart, allColumns, aliasToTableName);

        // parse WHERE filters (excluding join conditions)
        response.Filters = ParseWhereFilters(wherePart, allColumns, aliasToTableName);

        // detect JOIN relationships from WHERE clause (multi-table)
        response.Joins = ParseJoins(wherePart, allColumns, aliasToTableName, tableMap);

        // GROUP BY
        if (!string.IsNullOrEmpty(groupByPart))
            response.GroupByColumn = groupByPart.Trim();

        // ORDER BY
        if (!string.IsNullOrEmpty(orderByPart))
        {
            var m = Regex.Match(orderByPart.Trim(), @"^(.+?)\s*(ASC|DESC)?\s*$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                response.SortColumn = m.Groups[1].Value.Trim();
                response.SortDirection = m.Groups[2].Success ? m.Groups[2].Value.ToUpper() : "ASC";
            }
        }

        response.UnmatchedColumns = response.Columns
            .Where(c => !c.Matched)
            .Select(c => c.Expression ?? c.Alias ?? "")
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();

        return response;
    }

    // ===== Clause extraction =====

    internal static string ExtractBetween(string sql, string startKeyword, string stopKeywords)
    {
        var pattern = $@"\b{startKeyword}\b\s+(.*?)(?=\b({stopKeywords})\b|$)"
            .Replace("$", @"\z");
        var m = Regex.Match(sql, pattern, RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return m.Success ? m.Groups[1].Value.Trim() : "";
    }

    // ===== Comment & whitespace =====

    internal static string RemoveComments(string sql)
    {
        sql = Regex.Replace(sql, @"--[^\n\r]*", " ");
        sql = Regex.Replace(sql, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        return sql;
    }

    internal static string NormalizeWhitespace(string sql)
    {
        return Regex.Replace(sql, @"\s+", " ").Trim();
    }

    // ===== Table parsing =====

    /// <summary>
    /// Parse FROM clause into list of (alias, tableName) pairs.
    /// Handles: "SCHEMA"."TABLE" "ALIAS", SCHEMA.TABLE ALIAS, TABLE ALIAS, TABLE
    /// </summary>
    internal static List<(string Alias, string TableName)> ParseTableRefs(string fromClause)
    {
        var result = new List<(string Alias, string TableName)>();

        // Remove commas and split on JOIN keywords to get individual table references
        var parts = Regex.Split(fromClause, @"\s*,\s*|\s+(?:LEFT|RIGHT|INNER|OUTER|FULL|CROSS)?\s*JOIN\s+", RegexOptions.IgnoreCase);

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Pattern 1: "SCHEMA"."TABLE" "ALIAS"
            var m = Regex.Match(trimmed, @"^""(\w+)""\s*\.\s*""(\w+)""(?:\s+""?(\w+)""?)?", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var alias = m.Groups[3].Success ? m.Groups[3].Value : m.Groups[2].Value;
                result.Add((alias, m.Groups[2].Value));
                continue;
            }

            // Pattern 2: SCHEMA.TABLE ALIAS
            m = Regex.Match(trimmed, @"^(\w+)\s*\.\s*(\w+)(?:\s+(\w+))?", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var alias = m.Groups[3].Success ? m.Groups[3].Value : m.Groups[2].Value;
                result.Add((alias, m.Groups[2].Value));
                continue;
            }

            // Pattern 3: TABLE ALIAS (single identifier, no dot)
            m = Regex.Match(trimmed, @"^(\w+)(?:\s+(\w+))?", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var tableName = m.Groups[1].Value;
                var alias = m.Groups[2].Success ? m.Groups[2].Value : tableName;
                result.Add((alias, tableName));
            }
        }

        return result;
    }

    // ===== SELECT column parsing =====

    internal static List<SqlColumnMatch> ParseSelectColumns(
        string selectClause, List<MetaColumn> allColumns, Dictionary<string, string> aliasToTableName)
    {
        var parts = SplitRespectingNesting(selectClause, ',');
        var result = new List<SqlColumnMatch>();

        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            var (aggregateFunc, innerExpr) = ExtractAggregateFunc(trimmed);

            string? alias = null;
            var exprWithoutAlias = innerExpr;

            var asMatch = Regex.Match(innerExpr, @"^(.*?)\s+AS\s+""?(\w+)""?\s*$", RegexOptions.IgnoreCase);
            if (asMatch.Success)
            {
                exprWithoutAlias = asMatch.Groups[1].Value.Trim();
                alias = asMatch.Groups[2].Value;
            }
            else
            {
                var implicitMatch = Regex.Match(innerExpr, @"^(.+)\s+(""?[a-zA-Z_]\w*""?)\s*$");
                if (implicitMatch.Success)
                {
                    var possibleAlias = implicitMatch.Groups[2].Value.Trim('"');
                    var expr = implicitMatch.Groups[1].Value.Trim();
                    if (Regex.IsMatch(possibleAlias, @"^[a-zA-Z_]\w*$")
                        && !Regex.IsMatch(expr, @"[""']|\)$|^\("))
                    {
                        exprWithoutAlias = expr;
                        alias = possibleAlias;
                    }
                }
            }

            var colMatch = MatchColumn(exprWithoutAlias.Trim('"'), allColumns, aliasToTableName);

            result.Add(new SqlColumnMatch
            {
                MetaColumnId = colMatch?.Id,
                Alias = alias,
                AggregateFunc = aggregateFunc,
                Expression = trimmed,
                Matched = colMatch != null
            });
        }

        return result.OrderByDescending(c => c.Matched).ThenBy(c => c.Expression).ToList();
    }

    internal static (string? AggregateFunc, string InnerExpr) ExtractAggregateFunc(string expr)
    {
        var m = Regex.Match(expr.Trim(), @"^(COUNT|SUM|AVG|MAX|MIN)\s*\(\s*(.+?)\s*\)\s*$", RegexOptions.IgnoreCase);
        if (m.Success)
            return (m.Groups[1].Value.ToUpper(), m.Groups[2].Value.Trim());
        return (null, expr.Trim());
    }

    // ===== WHERE filter parsing =====

    internal static List<SqlFilterMatch> ParseWhereFilters(
        string whereClause, List<MetaColumn> allColumns, Dictionary<string, string> aliasToTableName)
    {
        var result = new List<SqlFilterMatch>();
        if (string.IsNullOrEmpty(whereClause)) return result;

        var conditions = SplitConditions(whereClause);

        foreach (var cond in conditions)
        {
            var trimmed = cond.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Handle NOT operator prefix
            var notPrefix = false;
            if (trimmed.ToUpperInvariant().StartsWith("NOT "))
            {
                notPrefix = true;
                trimmed = trimmed[4..].Trim();
            }

            // Match qualified column: alias.column OP value
            // Supports: =, !=, <>, >, >=, <, <=, LIKE, NOT LIKE, IN, NOT IN, BETWEEN, NOT BETWEEN
            var m = Regex.Match(trimmed,
                @"^""?(\w+)""?\s*\.\s*""?(\w+)""?\s*(>=|<=|!=|<>|>|<|=|(?:NOT\s+)?LIKE|(?:NOT\s+)?IN|(?:NOT\s+)?BETWEEN)\s+(.+)$",
                RegexOptions.IgnoreCase);

            if (!m.Success)
            {
                // Try unqualified column
                m = Regex.Match(trimmed,
                    @"^""?(\w+)""?\s*(>=|<=|!=|<>|>|<|=|(?:NOT\s+)?LIKE|(?:NOT\s+)?IN|(?:NOT\s+)?BETWEEN)\s+(.+)$",
                    RegexOptions.IgnoreCase);
            }

            if (!m.Success) continue;

            string columnName;
            string tableAlias;
            string op;
            string rawValue;

            if (m.Groups.Count == 5) // qualified: alias.column OP value
            {
                tableAlias = m.Groups[1].Value;
                columnName = m.Groups[2].Value;
                op = m.Groups[3].Value;
                rawValue = m.Groups[4].Value.Trim();
            }
            else // unqualified: column OP value
            {
                tableAlias = "";
                columnName = m.Groups[1].Value;
                op = m.Groups[2].Value;
                rawValue = m.Groups[3].Value.Trim();
            }

            // Skip join conditions: RHS is a column reference like alias.column
            if (IsColumnReference(rawValue))
                continue;

            if (notPrefix)
                op = "NOT " + op.ToUpperInvariant();
            else
                op = NormalizeOperator(op);

            var col = MatchColumnByName(columnName, tableAlias, allColumns, aliasToTableName);

            result.Add(new SqlFilterMatch
            {
                MetaColumnId = col?.Id,
                Operator = op,
                DefaultValue = ExtractDefaultValue(rawValue),
                Label = col?.Alias ?? col?.ColumnName ?? columnName,
                Matched = col != null
            });
        }

        return result;
    }

    /// <summary>Check if a value looks like a column reference (alias.column)</summary>
    internal static bool IsColumnReference(string value)
    {
        // Match patterns like "alias.column" or function calls on columns
        if (Regex.IsMatch(value, @"^""?\w+""?\s*\.\s*""?\w+""?$"))
            return true;
        // Function calls containing column references: to_char(a.col), etc
        if (Regex.IsMatch(value, @"\w+\s*\.\s*\w+", RegexOptions.IgnoreCase))
            return true;
        return false;
    }

    internal static string NormalizeOperator(string op)
    {
        return op.ToUpperInvariant() switch
        {
            "=" => "EQ",
            "!=" => "NE",
            "<>" => "NE",
            ">" => "GT",
            ">=" => "GTE",
            "<" => "LT",
            "<=" => "LTE",
            "LIKE" => "LIKE",
            "NOT LIKE" => "NOT LIKE",
            "IN" => "IN",
            "NOT IN" => "NOT IN",
            "BETWEEN" => "BETWEEN",
            "NOT BETWEEN" => "NOT BETWEEN",
            _ => "EQ"
        };
    }

    internal static string? ExtractDefaultValue(string rawValue)
    {
        if (string.IsNullOrEmpty(rawValue)) return null;

        // TO_DATE('value', 'format')
        var dateMatch = Regex.Match(rawValue, @"TO_DATE\s*\(\s*'([^']*)'", RegexOptions.IgnoreCase);
        if (dateMatch.Success)
            return dateMatch.Groups[1].Value;

        // Quoted string
        if (rawValue.StartsWith("'"))
        {
            var m = Regex.Match(rawValue, @"'([^']*)'");
            if (m.Success) return m.Groups[1].Value;
        }

        // Plain literal (not a column reference)
        if (!Regex.IsMatch(rawValue, @"^\w+\.\w+"))
            return rawValue.Trim('\'').Trim('"');

        return null;
    }

    // ===== JOIN detection =====

    internal static List<SqlJoinMatch> ParseJoins(
        string whereClause, List<MetaColumn> allColumns,
        Dictionary<string, string> aliasToTableName,
        Dictionary<string, MetaTable> tableMap)
    {
        var result = new List<SqlJoinMatch>();
        if (string.IsNullOrEmpty(whereClause)) return result;

        // Only detect joins when there are at least 2 tables
        if (aliasToTableName.Count < 2) return result;

        var conditions = SplitConditions(whereClause);
        foreach (var cond in conditions)
        {
            var trimmed = cond.Trim();
            if (string.IsNullOrEmpty(trimmed)) continue;

            // Skip NOT-prefixed conditions
            if (trimmed.ToUpperInvariant().StartsWith("NOT ")) continue;

            // Match: alias1.col1 = alias2.col2
            var m = Regex.Match(trimmed,
                @"^""?(\w+)""?\s*\.\s*""?(\w+)""?\s*=\s*""?(\w+)""?\s*\.\s*""?(\w+)""?\s*$",
                RegexOptions.IgnoreCase);

            if (!m.Success)
            {
                // Try function-wrapped: func(alias1.col1, ...) = func(alias2.col2, ...)
                // e.g., to_char(a.visit_date,'...')=to_char(b.visit_date,'...')
                m = Regex.Match(trimmed,
                    @"^.*?\b(\w+)\s*\.\s*(\w+)\b.*?\s*=\s*.*?\b(\w+)\s*\.\s*(\w+)\b.*$",
                    RegexOptions.IgnoreCase);
                if (!m.Success)
                    continue;
            }

            var leftAlias = m.Groups[1].Value;
            var leftCol = m.Groups[2].Value;
            var rightAlias = m.Groups[3].Value;
            var rightCol = m.Groups[4].Value;

            // Must be different aliases
            if (string.Equals(leftAlias, rightAlias, StringComparison.OrdinalIgnoreCase))
                continue;

            // Match columns
            var leftMeta = MatchColumnByName(leftCol, leftAlias, allColumns, aliasToTableName);
            var rightMeta = MatchColumnByName(rightCol, rightAlias, allColumns, aliasToTableName);

            // Determine which is the main table (first in aliasToTableName)
            // The main table is the first one parsed from FROM clause
            var mainTableName = aliasToTableName.Values.FirstOrDefault() ?? "";
            aliasToTableName.TryGetValue(leftAlias, out var leftTable);
            aliasToTableName.TryGetValue(rightAlias, out var rightTable);

            // We want: LEFT = main table column, RIGHT = join table column
            int? joinTableId = null;
            string? joinTableName = null;
            int? leftMetaColumnId;
            string? leftColumnName;
            int? rightMetaColumnId;
            string? rightColumnName;

            if (string.Equals(leftTable, mainTableName, StringComparison.OrdinalIgnoreCase))
            {
                // left side is main table, right side is join table
                leftMetaColumnId = leftMeta?.Id;
                leftColumnName = leftMeta?.ColumnName ?? leftCol;
                rightMetaColumnId = rightMeta?.Id;
                rightColumnName = rightMeta?.ColumnName ?? rightCol;
                joinTableName = rightTable;
            }
            else if (string.Equals(rightTable, mainTableName, StringComparison.OrdinalIgnoreCase))
            {
                // right side is main table, left side is join table
                leftMetaColumnId = rightMeta?.Id;
                leftColumnName = rightMeta?.ColumnName ?? rightCol;
                rightMetaColumnId = leftMeta?.Id;
                rightColumnName = leftMeta?.ColumnName ?? leftCol;
                joinTableName = leftTable;
            }
            else
            {
                // Neither is main table — skip
                continue;
            }

            if (joinTableName != null && tableMap.TryGetValue(joinTableName, out var joinMetaTable))
                joinTableId = joinMetaTable.Id;

            result.Add(new SqlJoinMatch
            {
                JoinTableId = joinTableId,
                JoinTableName = joinTableName,
                JoinType = "LEFT",
                LeftMetaColumnId = leftMetaColumnId,
                LeftColumnName = leftColumnName,
                RightMetaColumnId = rightMetaColumnId,
                RightColumnName = rightColumnName,
                Matched = leftMetaColumnId != null && rightMetaColumnId != null && joinTableId != null
            });
        }

        return result;
    }

    // ===== Column matching (case-insensitive) =====

    internal static MetaColumn? MatchColumn(string colExpr, List<MetaColumn> allColumns,
        Dictionary<string, string> aliasToTableName)
    {
        var qualified = Regex.Match(colExpr, @"^""?(\w+)""?\s*\.\s*""?(\w+)""?$");
        if (qualified.Success)
        {
            var alias = qualified.Groups[1].Value;
            var colName = qualified.Groups[2].Value;
            return MatchColumnByName(colName, alias, allColumns, aliasToTableName);
        }

        // Unqualified
        var simpleCol = colExpr.Trim('"');
        var matches = allColumns
            .Where(c => string.Equals(c.ColumnName, simpleCol, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(c.Alias, simpleCol, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return matches.FirstOrDefault();
    }

    internal static MetaColumn? MatchColumnByName(string columnName, string tableAlias,
        List<MetaColumn> allColumns, Dictionary<string, string> aliasToTableName)
    {
        // Resolve alias to actual table name
        aliasToTableName.TryGetValue(tableAlias, out var tableName);

        var candidates = allColumns
            .Where(c => string.Equals(c.ColumnName, columnName, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(c.Alias, columnName, StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 1) return candidates[0];

        if (tableName != null)
        {
            var byTable = candidates
                .FirstOrDefault(c => string.Equals(c.MetaTable?.TableName, tableName, StringComparison.OrdinalIgnoreCase));
            if (byTable != null) return byTable;
        }

        return candidates.FirstOrDefault();
    }

    // ===== String splitting helpers =====

    internal static List<string> SplitRespectingNesting(string text, char delimiter)
    {
        var parts = new List<string>();
        int depth = 0;
        bool inSingleQuote = false;
        bool inDoubleQuote = false;
        int start = 0;

        for (int i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (ch == '\'' && !inDoubleQuote) inSingleQuote = !inSingleQuote;
            else if (ch == '"' && !inSingleQuote) inDoubleQuote = !inDoubleQuote;
            else if (!inSingleQuote && !inDoubleQuote)
            {
                if (ch == '(') depth++;
                else if (ch == ')') depth--;
                else if (ch == delimiter && depth == 0)
                {
                    parts.Add(text[start..i]);
                    start = i + 1;
                }
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    internal static List<string> SplitConditions(string whereClause)
    {
        var parts = new List<string>();
        int depth = 0;
        bool inQuote = false;
        int start = 0;

        for (int i = 0; i < whereClause.Length; i++)
        {
            var ch = whereClause[i];
            if (ch == '\'') inQuote = !inQuote;
            if (inQuote) continue;
            if (ch == '(') depth++;
            else if (ch == ')') depth--;

            if (depth == 0 && i + 4 <= whereClause.Length)
            {
                var sub = whereClause.Substring(i, 4).ToUpperInvariant();
                if (sub == " AND" && (i + 4 == whereClause.Length || !char.IsLetterOrDigit(whereClause[i + 4])))
                {
                    parts.Add(whereClause[start..i]);
                    start = i + 4;
                    i += 3;
                }
            }
        }

        parts.Add(whereClause[start..]);
        return parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
    }
}
