using DDT.Server.Ldap;
using Xunit;

namespace DDT.Server.Tests;

public sealed class LdapFilterTests
{
    [Theory]
    [InlineData("*", @"\2a")]
    [InlineData("(", @"\28")]
    [InlineData(")", @"\29")]
    [InlineData(@"\", @"\5c")]
    [InlineData("/", @"\2f")]
    public void FilterMetacharactersAreEscaped(string input, string expected)
    {
        Assert.Equal(expected, LdapFilter.EscapeValue(input));
    }

    [Fact]
    public void AWildcardInjectionCannotWidenTheFilter()
    {
        // Unescaped, this turns (sAMAccountName={0}) into a filter matching every account.
        Assert.Equal(@"\2a\29\28objectClass=\2a", LdapFilter.EscapeValue("*)(objectClass=*"));
    }

    [Fact]
    public void OrdinaryUserNamesArePassedThrough()
    {
        Assert.Equal("j.doe", LdapFilter.EscapeValue("j.doe"));
    }

    [Theory]
    [InlineData(",", @"\,")]
    [InlineData("+", @"\+")]
    [InlineData("=", @"\=")]
    [InlineData("\"", "\\\"")]
    public void DistinguishedNameMetacharactersAreEscaped(string input, string expected)
    {
        Assert.Equal(expected, LdapFilter.EscapeDistinguishedNameValue(input));
    }
}
