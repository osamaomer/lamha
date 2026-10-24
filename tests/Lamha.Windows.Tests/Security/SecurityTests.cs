using System.Text;
using Lamha.Windows.Security;

namespace Lamha.Windows.Tests.Security;

public sealed class SecurityTests : IDisposable
{
    /// <summary>The tests' own names in Credential Manager, removed afterwards.</summary>
    private readonly string _prefix = $"Lamha-test-{Guid.NewGuid():N}/";
    private readonly CredentialStore _keys;

    public SecurityTests() => _keys = new CredentialStore(_prefix);

    public void Dispose()
    {
        _keys.Set("claude", null);
        _keys.Set("gemini", null);
    }

    [Fact]
    public void AKeyIsKeptReplacedAndRemoved()
    {
        Assert.Null(_keys.Get("claude"));
        _keys.Set("claude", "sk-ant-test-مفتاح");
        Assert.Equal("sk-ant-test-مفتاح", _keys.Get("claude"));
        _keys.Set("claude", "sk-ant-second");
        Assert.Equal("sk-ant-second", _keys.Get("claude"));
        _keys.Set("claude", "");
        Assert.Null(_keys.Get("claude"));
        _keys.Set("claude", null); // removing what isn't there is fine
    }

    [Fact]
    public void EachProviderHasItsOwnKey()
    {
        _keys.Set("claude", "a");
        _keys.Set("gemini", "b");
        Assert.Equal("a", _keys.Get("claude"));
        Assert.Equal("b", _keys.Get("gemini"));
        Assert.Null(new CredentialStore(_prefix + "other/").Get("claude"));
    }

    [Fact]
    public void DpapiRoundTripsAndHidesThePlainText()
    {
        var dpapi = new Dpapi();
        var plain = Encoding.UTF8.GetBytes("secret الحافظة");
        var sealedBytes = dpapi.Protect(plain);
        Assert.DoesNotContain("secret", Encoding.UTF8.GetString(sealedBytes), StringComparison.Ordinal);
        Assert.Equal(plain, dpapi.Unprotect(sealedBytes));
    }

    [Fact]
    public void DpapiRefusesWhatItDidntEncrypt()
    {
        var dpapi = new Dpapi();
        Assert.Null(dpapi.Unprotect([1, 2, 3, 4]));
        var damaged = dpapi.Protect([5, 6, 7]);
        damaged[^1] ^= 0xFF;
        Assert.Null(dpapi.Unprotect(damaged));
    }
}
