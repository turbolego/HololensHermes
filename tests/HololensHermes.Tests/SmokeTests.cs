using Xunit;

namespace HololensHermes.Tests;

public class SmokeTests
{
    [Fact]
    public void string_not_null()
    {
        Assert.NotNull("hello");
    }

    [Fact]
    public void one_plus_one_is_two()
    {
        Assert.Equal(2, 1 + 1);
    }
}
