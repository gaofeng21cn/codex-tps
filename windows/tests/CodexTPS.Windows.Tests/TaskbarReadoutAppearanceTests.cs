using CodexTPS.WindowsApp;

namespace CodexTPS.Windows.Tests;

public sealed class TaskbarReadoutAppearanceTests
{
    [Theory]
    [InlineData((int)TaskbarEdge.Bottom, 96, 12)]
    [InlineData((int)TaskbarEdge.Bottom, 144, 18)]
    [InlineData((int)TaskbarEdge.Top, 192, 24)]
    [InlineData((int)TaskbarEdge.Left, 96, 11)]
    [InlineData((int)TaskbarEdge.Right, 192, 22)]
    public void FontPixelSizeScalesWithTaskbarDpi(
        int edgeValue,
        int dpi,
        int expectedPixels)
    {
        Assert.Equal(
            expectedPixels,
            TaskbarReadoutAppearance.FontPixelSize((TaskbarEdge)edgeValue, dpi));
    }

    [Fact]
    public void InvalidDpiFallsBackToNinetySix()
    {
        Assert.Equal(
            12,
            TaskbarReadoutAppearance.FontPixelSize(TaskbarEdge.Bottom, 0));
    }
}
