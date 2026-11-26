using TaskManagementSystem.Helpers;

namespace TaskManagementSystem.UnitTests.BusinessRules;

/// <summary>The shared completion formula used by the dashboard and the project list.</summary>
public sealed class ProjectProgressTests
{
    [Theory]
    [InlineData(0, 0, 0, 0.0)]      // empty project → 0, not a division by zero
    [InlineData(4, 4, 0, 100.0)]    // everything done
    [InlineData(4, 1, 1, 33.3)]     // cancelled tasks don't count: 1 / (4 − 1) = 33.33… → 33.3
    [InlineData(3, 2, 0, 66.7)]     // rounded to 1 decimal
    [InlineData(2, 0, 2, 0.0)]      // only cancelled tasks → nothing to do → 0
    [InlineData(5, 0, 0, 0.0)]      // nothing done yet
    public void CompletionPercent_ReturnsDoneShareOfNonCancelledTasks(int total, int done, int cancelled, double expected)
    {
        // Act
        var percent = ProjectProgress.CompletionPercent(total, done, cancelled);

        // Assert
        Assert.Equal(expected, percent);
    }
}
