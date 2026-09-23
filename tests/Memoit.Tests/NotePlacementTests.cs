using System.Windows;
using Memoit.Services;
using Xunit;

namespace Memoit.Tests;

public sealed class NotePlacementTests
{
    [Fact]
    public void CascadesFromMovedSourceRatherThanFixedDesktopOrigin()
        => Assert.Equal(new Point(528, 328), NotePlacement.Cascade(new Rect(500, 300, 320, 300), new Size(320, 300), new Rect(0, 0, 1920, 1080)));

    [Fact]
    public void RightAndBottomEdgesReverseOffsetBeforeClamping()
        => Assert.Equal(new Point(652, 472), NotePlacement.Cascade(new Rect(680, 500, 320, 300), new Size(320, 300), new Rect(0, 0, 1000, 800)));

    [Fact]
    public void OnlyOverflowingAxisReverses()
        => Assert.Equal(new Point(652, 228), NotePlacement.Cascade(new Rect(680, 200, 320, 300), new Size(320, 300), new Rect(0, 0, 1000, 800)));

    [Fact]
    public void SecondaryMonitorWithNegativeCoordinatesKeepsRelativeOffset()
        => Assert.Equal(new Point(-972, -72), NotePlacement.Cascade(new Rect(-1000, -100, 320, 300), new Size(320, 300), new Rect(-1920, -200, 1920, 1080)));

    [Fact]
    public void ChildLargerThanWorkAreaClampsWithoutThrowing()
        => Assert.Equal(new Point(-200, 0), NotePlacement.Cascade(new Rect(50, 20, 36, 36), new Size(1200, 900), new Rect(-200, 0, 800, 600)));
}
