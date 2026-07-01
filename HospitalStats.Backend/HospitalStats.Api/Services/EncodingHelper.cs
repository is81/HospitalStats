using System.Text;
using System.Text.RegularExpressions;

namespace HospitalStats.Api.Services;

/// <summary>
/// Static utility methods for US7ASCII Oracle character encoding handling.
/// Extracted from QueryExecutionService for reuse across services and eventual
/// extraction into the standalone HospitalStats.QueryEngine package.
/// </summary>
public static class EncodingHelper
{
    public static bool IsStringType(string? dataType) => dataType?.ToUpperInvariant() switch
    {
        "VARCHAR2" or "NVARCHAR2" or "CHAR" or "NCHAR" or "CLOB" or "NCLOB"
            or "VARCHAR" or "NVARCHAR" or "LONG" => true,
        _ => false
    };

    public static bool ContainsNonAscii(string? value)
    {
        if (string.IsNullOrEmpty(value)) return false;
        return value.Any(c => c > 127);
    }

    /// <summary>
    /// Encode a filter value into a hex string suitable for comparison against
    /// RAWTOHEX(UTL_RAW.CAST_TO_RAW(...)) output. LIKE wildcards (% and _) are
    /// preserved as-is for LIKE/NOT LIKE operators; all other characters are
    /// converted to their byte representation in the source encoding and hex-encoded.
    /// The result is pure ASCII and survives US7ASCII transport without corruption.
    /// </summary>
    public static string EncodeNonAsciiValue(string value, string op, string? sourceEncoding)
    {
        var isLike = op.Equals("LIKE", StringComparison.OrdinalIgnoreCase) ||
                     op.Equals("NOT LIKE", StringComparison.OrdinalIgnoreCase);

        var enc = Encoding.GetEncoding(sourceEncoding ?? "gbk");
        var sb = new StringBuilder();

        foreach (var ch in value)
        {
            if (isLike && (ch == '%' || ch == '_'))
            {
                sb.Append(ch);
            }
            else
            {
                var bytes = enc.GetBytes(new[] { ch });
                foreach (var b in bytes)
                    sb.Append(b.ToString("X2"));
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Decode a hex string produced by RAWTOHEX(UTL_RAW.CAST_TO_RAW()) back to
    /// readable text using the specified target encoding.
    /// </summary>
    public static string DecodeHexString(string hex, string? targetEncoding)
    {
        if (string.IsNullOrEmpty(hex)) return hex;
        try
        {
            var bytes = Convert.FromHexString(hex);
            if (!string.IsNullOrEmpty(targetEncoding))
            {
                var enc = Encoding.GetEncoding(targetEncoding);
                return enc.GetString(bytes);
            }
            // Auto-detect: try common Chinese encodings
            foreach (var encName in new[] { "gbk", "gb2312", "gb18030" })
            {
                try
                {
                    var enc = Encoding.GetEncoding(encName);
                    var result = enc.GetString(bytes);
                    if (result.Any(c => c >= 0x4E00 && c <= 0x9FFF))
                        return result;
                }
                catch { }
            }
            // Fallback: return hex string as-is
            return hex;
        }
        catch
        {
            return hex;
        }
    }

    /// <summary>
    /// Fix garbled Chinese text from Oracle US7ASCII databases.
    /// NLS_LANG=WE8ISO8859P1 ensures raw bytes pass through 1:1 as Latin-1 chars.
    /// We recover raw bytes via ISO-8859-1 and re-decode with the real encoding.
    /// </summary>
    public static string ConvertEncoding(string input, string? sourceEncoding)
    {
        if (string.IsNullOrEmpty(input)) return input;
        try
        {
            var latin1 = Encoding.GetEncoding("iso-8859-1");
            var rawBytes = latin1.GetBytes(input);

            if (!string.IsNullOrEmpty(sourceEncoding))
            {
                var srcEnc = Encoding.GetEncoding(sourceEncoding);
                return srcEnc.GetString(rawBytes);
            }

            // Auto-detect: try common Chinese encodings, return first that produces Chinese
            foreach (var encName in new[] { "gbk", "gb2312", "gb18030" })
            {
                try
                {
                    var enc = Encoding.GetEncoding(encName);
                    var result = enc.GetString(rawBytes);
                    if (result.Any(c => c >= 0x4E00 && c <= 0x9FFF))
                        return result;
                }
                catch { }
            }
            return input;
        }
        catch
        {
            return input;
        }
    }

    /// <summary>
    /// Replace non-ASCII characters in SQL string literals with their hex-encoded
    /// equivalents, so they survive US7ASCII Oracle transport. Only modifies
    /// content inside single-quoted strings; SQL keywords and identifiers are untouched.
    /// </summary>
    public static string HexEncodeInlineLiterals(string sql, string sourceEncoding)
    {
        var sb = new StringBuilder();
        var depth = 0;
        var contentBuf = new StringBuilder();

        for (int i = 0; i < sql.Length; i++)
        {
            var ch = sql[i];
            if (ch == '\'')
            {
                if (depth == 0)
                {
                    depth = 1;
                    contentBuf.Clear();
                }
                else if (i + 1 < sql.Length && sql[i + 1] == '\'')
                {
                    // Oracle escaped quote: ''
                    contentBuf.Append("''");
                    i++; // skip second quote
                }
                else
                {
                    // End of string literal
                    var content = contentBuf.ToString();
                    if (ContainsNonAscii(content))
                    {
                        var enc = Encoding.GetEncoding(sourceEncoding);
                        var hex = Convert.ToHexString(enc.GetBytes(content));
                        sb.Append($"RAWTOHEX(HEXTORAW('{hex}'))");
                    }
                    else
                    {
                        sb.Append('\'');
                        sb.Append(content);
                        sb.Append('\'');
                    }
                    depth = 0;
                }
            }
            else if (depth > 0)
            {
                contentBuf.Append(ch);
            }
            else
            {
                sb.Append(ch);
            }
        }

        return sb.ToString();
    }

    public static string? DiagnoseEncoding(string input, ILogger logger)
    {
        if (string.IsNullOrEmpty(input))
            return null;

        var latin1 = Encoding.GetEncoding("iso-8859-1");
        var rawBytes = latin1.GetBytes(input);
        var hex = Convert.ToHexString(rawBytes.Take(20).ToArray());
        var hasHigh = rawBytes.Any(b => b > 127);
        logger.LogWarning("Encoding diagnosis: input={Input}, len={Len}, hasHighBytes={HasHigh}, first 20 hex={Hex}",
            input[..Math.Min(input.Length, 40)], input.Length, hasHigh, hex);

        // Try common Chinese encodings and log results
        foreach (var encName in new[] { "gb2312", "gbk", "gb18030", "big5", "utf-8" })
        {
            try
            {
                var enc = Encoding.GetEncoding(encName);
                var decoded = enc.GetString(rawBytes);
                var hasChinese = decoded.Any(c => c >= 0x4E00 && c <= 0x9FFF);
                logger.LogInformation("  Try {Enc}: hasChinese={HasCN}, sample={Sample}",
                    encName, hasChinese, decoded[..Math.Min(20, decoded.Length)]);
            }
            catch { }
        }
        return null;
    }
}
