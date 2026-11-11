using MediaStation.Unpack.Cli;
using MediaStation.Unpack.Model;
using Xunit;

namespace MediaStation.Unpack.Tests.Unit.Cli;

public class DuplicateScopeParserTests
{
    [Theory]
    [InlineData("global", DuplicateScope.Global)]
    [InlineData("GLOBAL", DuplicateScope.Global)]
    [InlineData("context", DuplicateScope.Context)]
    [InlineData("Context", DuplicateScope.Context)]
    [InlineData(null, DuplicateScope.None)]
    [InlineData("", DuplicateScope.None)]
    [InlineData("   ", DuplicateScope.None)]
    public void ParseDuplicateScope_ValidValues_ReturnsExpectedScope(string? input, DuplicateScope expected)
    {
        var result = Program.ParseDuplicateScope(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void ParseDuplicateScope_InvalidValue_ThrowsArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => Program.ParseDuplicateScope("invalid_scope"));
        Assert.Contains("invalid_scope", ex.Message);
    }
}
