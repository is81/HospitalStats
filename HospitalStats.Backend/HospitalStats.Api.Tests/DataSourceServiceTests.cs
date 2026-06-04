using System.Security.Cryptography;
using HospitalStats.Api.Services;

namespace HospitalStats.Api.Tests;

public class DataSourceServiceTests
{
    // ===== EncryptWithKey / DecryptWithKey =====

    [Fact]
    public void EncryptDecrypt_RoundTrip_ReturnsOriginal()
    {
        const string key = "test-key-123456!";
        const string plain = "Data Source=ORCL;User Id=test;Password=secret;";

        var encrypted = DataSourceService.EncryptWithKey(plain, key);
        var decrypted = DataSourceService.DecryptWithKey(encrypted, key);

        Assert.Equal(plain, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_WrongKey_ReturnsGarbageNotOriginal()
    {
        const string plain = "Data Source=ORCL;User Id=test;Password=secret;";

        var encrypted = DataSourceService.EncryptWithKey(plain, "key-A-123456!");

        // Decrypting with wrong key may throw or return garbage — either way it must not match
        string decrypted;
        try
        {
            decrypted = DataSourceService.DecryptWithKey(encrypted, "key-B-123456!");
        }
        catch (CryptographicException)
        {
            return; // Throwing is also valid behavior for wrong key
        }

        Assert.NotEqual(plain, decrypted);
    }

    [Fact]
    public void Encrypt_ProducesDifferentCiphertextEachTime_RandomIV()
    {
        const string key = "test-key-123456!";
        const string plain = "Data Source=ORCL;User Id=test;";

        var c1 = DataSourceService.EncryptWithKey(plain, key);
        var c2 = DataSourceService.EncryptWithKey(plain, key);

        Assert.NotEqual(c1, c2);
        Assert.Equal(plain, DataSourceService.DecryptWithKey(c1, key));
        Assert.Equal(plain, DataSourceService.DecryptWithKey(c2, key));
    }

    [Theory]
    [InlineData("Data Source=XE;User Id=scott;Password=tiger;Pooling=true;")]
    [InlineData("Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=db)(PORT=1521)));User Id=app;Password=p@ss;")]
    [InlineData("Data Source=localhost:1521/ORCL;User Id=test;Password=test;Connection Timeout=30;")]
    public void EncryptDecrypt_VariousConnectionStrings_Works(string plain)
    {
        const string key = "my-test-key-654321";

        var encrypted = DataSourceService.EncryptWithKey(plain, key);
        var decrypted = DataSourceService.DecryptWithKey(encrypted, key);

        Assert.Equal(plain, decrypted);
    }

    [Fact]
    public void EncryptDecrypt_LongConnectionString_Works()
    {
        const string key = "test-key-123456!";
        var plain = "Data Source=ORCL;User Id=" + new string('x', 500) + ";Password=pass;";

        var encrypted = DataSourceService.EncryptWithKey(plain, key);
        var decrypted = DataSourceService.DecryptWithKey(encrypted, key);

        Assert.Equal(plain, decrypted);
    }

    [Fact]
    public void EncryptWithKey_NonAsciiKey_Works()
    {
        const string key = "中文密钥测试-12345!";
        const string plain = "Data Source=ORCL;User Id=test;Password=test;Pooling=true;";

        var encrypted = DataSourceService.EncryptWithKey(plain, key);
        var decrypted = DataSourceService.DecryptWithKey(encrypted, key);

        Assert.Equal(plain, decrypted);
    }

    // ===== IsValidConnString =====

    [Theory]
    [InlineData("Data Source=ORCL;User Id=test;Password=pwd;", true)]
    [InlineData("Data Source=(DESCRIPTION=(ADDRESS=(PROTOCOL=TCP)(HOST=db)(PORT=1521)))", true)]
    [InlineData("Password=secret;User Id=admin;Pooling=true;Data Source=xe", true)]
    [InlineData("Min Pool Size=5;Max Pool Size=20;Data Source=ORCL;User Id=app;Password=x;", true)]
    [InlineData("", false)]
    [InlineData("random garbage text here", false)]
    [InlineData("hello world 你好", false)]
    public void IsValidConnString_ValidatesCorrectly(string input, bool expected)
    {
        Assert.Equal(expected, DataSourceService.IsValidConnString(input));
    }

    [Fact]
    public void IsValidConnString_Null_ReturnsFalse()
    {
        Assert.False(DataSourceService.IsValidConnString(null));
    }
}
