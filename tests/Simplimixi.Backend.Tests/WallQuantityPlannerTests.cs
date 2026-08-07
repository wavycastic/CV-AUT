using System.Collections.Generic;
using CvAut;
using OpenCvSharp;
using Xunit;

namespace CvAut.Backend.Tests;

public sealed class WallQuantityPlannerTests
{
    [Fact]
    public void PlanNext_RemainingAtLeastTen_PrefersAddTen()
    {
        WallQuantityPlanStep step = WallQuantityPlanner.PlanNext(1, 31, Controls(addOne: true, addTen: true));
        Assert.True(step.CanExecute);
        Assert.Equal(WallQuantityControlRole.AddTen, step.Role);
        Assert.Equal(10, step.Delta);
        Assert.Equal(11, step.ExpectedCount);
    }

    [Fact]
    public void PlanNext_RemainderBelowTen_UsesAddOneWithoutOvershoot()
    {
        WallQuantityPlanStep step = WallQuantityPlanner.PlanNext(21, 25, Controls(addOne: true, addTen: true));
        Assert.True(step.CanExecute);
        Assert.Equal(WallQuantityControlRole.AddOne, step.Role);
        Assert.Equal(22, step.ExpectedCount);
    }

    [Fact]
    public void PlanNext_AddTenDisabled_FallsBackToAddOne()
    {
        WallQuantityPlanStep step = WallQuantityPlanner.PlanNext(1, 21, Controls(addOne: true, addTen: false));
        Assert.True(step.CanExecute);
        Assert.Equal(WallQuantityControlRole.AddOne, step.Role);
        Assert.Equal("add_ten_unavailable_fallback_add_one", step.Reason);
    }

    [Fact]
    public void PlanNext_AddOneUnavailableForRemainder_FailsClosed()
    {
        WallQuantityPlanStep step = WallQuantityPlanner.PlanNext(21, 25, Controls(addOne: false, addTen: true));
        Assert.False(step.CanExecute);
        Assert.Equal(0, step.Delta);
        Assert.Equal("add_one_unavailable_for_remainder", step.Reason);
    }

    [Theory]
    [InlineData(300, 300, 255)]
    [InlineData(31, 255, 31)]
    [InlineData(0, 0, 1)]
    public void ClampTarget_UsesConfiguredLimitAndHardSafetyMaximum(int requested, int limit, int expected)
        => Assert.Equal(expected, WallQuantityPlanner.ClampTarget(requested, limit));

    private static IReadOnlyList<WallQuantityControlInfo> Controls(bool addOne, bool addTen)
        => new[]
        {
            new WallQuantityControlInfo(true, WallQuantityControlRole.AddTen, 10, addTen, new Rect(0, 0, 10, 10), new Point(5, 5), 1, "test", addTen ? "ok" : "disabled"),
            new WallQuantityControlInfo(true, WallQuantityControlRole.AddOne, 1, addOne, new Rect(20, 0, 10, 10), new Point(25, 5), 1, "test", addOne ? "ok" : "disabled")
        };
}
