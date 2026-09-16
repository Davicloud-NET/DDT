using DDT.Server.Authentication;
using Xunit;

namespace DDT.Server.Tests;

public sealed class IdentityBootstrapTests
{
    [Fact]
    public void AlwaysGeneratesAPasswordIdentityAccepts()
    {
        // Before the fix about 2.6 percent of draws had no digit, so ten thousand draws cannot all pass
        // by luck.
        for (int draw = 0; draw < 10_000; draw++)
        {
            string password = IdentityBootstrap.GeneratePassword();

            Assert.Contains(password, char.IsAsciiDigit);
            Assert.Contains(password, char.IsAsciiLetterUpper);
            Assert.Contains(password, char.IsAsciiLetterLower);
        }
    }
}
