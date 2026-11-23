using TaskManagementSystem.Enums;
using TaskManagementSystem.Services;

namespace TaskManagementSystem.UnitTests.BusinessRules;

/// <summary>
/// Tests the state machine against an INDEPENDENT copy of the specification (docs/10).
/// Every one of the 5 × 5 = 25 (from, to) pairs is checked, so adding or removing a transition
/// in the code without updating the spec makes a test fail.
/// </summary>
public sealed class TaskStatusWorkflowTests
{
    // The specification, written separately from the implementation: (from, to) → requires owner?
    private static readonly Dictionary<(TaskItemStatus, TaskItemStatus), bool> Specification = new()
    {
        [(TaskItemStatus.Todo, TaskItemStatus.InProgress)] = false,
        [(TaskItemStatus.Todo, TaskItemStatus.Cancelled)] = false,
        [(TaskItemStatus.InProgress, TaskItemStatus.Todo)] = false,
        [(TaskItemStatus.InProgress, TaskItemStatus.InReview)] = false,
        [(TaskItemStatus.InProgress, TaskItemStatus.Cancelled)] = false,
        [(TaskItemStatus.InReview, TaskItemStatus.InProgress)] = false,
        [(TaskItemStatus.InReview, TaskItemStatus.Done)] = false,
        [(TaskItemStatus.InReview, TaskItemStatus.Cancelled)] = false,
        [(TaskItemStatus.Done, TaskItemStatus.InProgress)] = true,
        [(TaskItemStatus.Cancelled, TaskItemStatus.Todo)] = true
    };

    public static TheoryData<TaskItemStatus, TaskItemStatus> AllPairs()
    {
        var data = new TheoryData<TaskItemStatus, TaskItemStatus>();
        foreach (var from in Enum.GetValues<TaskItemStatus>())
        {
            foreach (var to in Enum.GetValues<TaskItemStatus>())
            {
                data.Add(from, to);
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void CanTransition_MatchesSpecification(TaskItemStatus from, TaskItemStatus to)
    {
        // Arrange
        var expectedAllowed = Specification.TryGetValue((from, to), out var expectedRequiresOwner);

        // Act
        var allowed = TaskStatusWorkflow.CanTransition(from, to, out var requiresOwner);

        // Assert
        Assert.Equal(expectedAllowed, allowed);
        Assert.Equal(expectedRequiresOwner, requiresOwner);
    }

    [Fact]
    public void GetNextStatuses_FromTodo_ReturnsInProgressAndCancelled()
    {
        var next = TaskStatusWorkflow.GetNextStatuses(TaskItemStatus.Todo);

        Assert.Equal([TaskItemStatus.InProgress, TaskItemStatus.Cancelled], next);
    }

    [Theory]
    [InlineData(TaskItemStatus.Done, true)]
    [InlineData(TaskItemStatus.Cancelled, true)]
    [InlineData(TaskItemStatus.Todo, false)]
    [InlineData(TaskItemStatus.InProgress, false)]
    [InlineData(TaskItemStatus.InReview, false)]
    public void IsClosed_OnlyForDoneAndCancelled(TaskItemStatus status, bool expected)
    {
        Assert.Equal(expected, TaskStatusWorkflow.IsClosed(status));
    }
}
