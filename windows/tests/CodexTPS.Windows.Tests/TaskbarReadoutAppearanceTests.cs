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

    [Fact]
    public void UsesRegularWeightForNativeTaskbarText()
    {
        Assert.Equal(FontStyle.Regular, TaskbarReadoutAppearance.TextFontStyle);
    }

    [Theory]
    [InlineData(true, 32, 32, 32)]
    [InlineData(false, 255, 255, 255)]
    public void TextColorTracksTaskbarTheme(
        bool lightTheme,
        int red,
        int green,
        int blue)
    {
        var color = TaskbarReadoutAppearance.TextColor(lightTheme);

        Assert.Equal(red, color.R);
        Assert.Equal(green, color.G);
        Assert.Equal(blue, color.B);
    }

    [Fact]
    public void RenderKeepsBackgroundTransparentAndClickTargetPresent()
    {
        using var bitmap = TaskbarReadoutAppearance.Render(
            new Size(112, 28),
            "12.5K t/s",
            TaskbarEdge.Bottom,
            96,
            Color.Black);

        Assert.Equal(
            TaskbarReadoutAppearance.TransparentHitTestAlpha,
            bitmap.GetPixel(0, 0).A);

        var alphaValues = Enumerable.Range(0, bitmap.Height)
            .SelectMany(y => Enumerable.Range(0, bitmap.Width)
                .Select(x => bitmap.GetPixel(x, y).A))
            .ToArray();
        Assert.Contains(alphaValues, alpha => alpha > 128);
        Assert.True(
            alphaValues.Count(alpha => alpha > TaskbarReadoutAppearance.TransparentHitTestAlpha) <
            alphaValues.Length / 2);
    }
}
