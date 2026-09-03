using System.Windows;
using PetHelper;
using Xunit;

namespace PetHelper.Tests;

public sealed class DefaultLayoutTests
{
    [Theory]
    [InlineData(DefaultLayout.Center, 300d, 250d)]
    [InlineData(DefaultLayout.TopLeft, 4d, 4d)]
    [InlineData(DefaultLayout.TopCenter, 300d, 4d)]
    [InlineData(DefaultLayout.TopRight, 596d, 4d)]
    [InlineData(DefaultLayout.MiddleLeft, 4d, 250d)]
    [InlineData(DefaultLayout.MiddleRight, 596d, 250d)]
    [InlineData(DefaultLayout.BottomLeft, 4d, 496d)]
    [InlineData(DefaultLayout.BottomCenter, 300d, 496d)]
    [InlineData(DefaultLayout.BottomRight, 596d, 496d)]
    public void Place_uses_a_semantic_work_area_anchor(string placement, double expectedLeft, double expectedTop)
    {
        var target = DefaultLayout.Place(placement, new Rect(0d, 0d, 800d, 600d), new Size(200d, 100d));

        Assert.Equal(expectedLeft, target.Left);
        Assert.Equal(expectedTop, target.Top);
        Assert.Equal(200d, target.Width);
        Assert.Equal(100d, target.Height);
    }

    [Fact]
    public void Placement_validation_keeps_near_pet_dialogue_only()
    {
        Assert.True(DefaultLayout.IsPetPlacement(DefaultLayout.Center));
        Assert.False(DefaultLayout.IsPetPlacement(DefaultLayout.NearPet));
        Assert.True(DefaultLayout.IsDialoguePlacement(DefaultLayout.NearPet));
        Assert.False(DefaultLayout.IsDialoguePlacement("left"));
    }
}
