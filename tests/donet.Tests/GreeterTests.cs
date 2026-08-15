namespace donet.Tests;

public class GreeterTests
{
    [Fact]
    public void Greet_ReturnsHelloWithName()
    {
        Assert.Equal("Hello, donet!", Greeter.Greet("donet"));
    }

    [Fact]
    public void Greet_ThrowsOnEmptyName()
    {
        Assert.Throws<ArgumentException>(() => Greeter.Greet("  "));
    }
}
