namespace TaskManagementSystem.Helpers;

/// <summary>
/// One definition of "project completion" shared by the dashboard and the project list,
/// so both screens always show the same percentage for the same project.
/// </summary>
public static class ProjectProgress
{
    /// <summary>
    /// Done ÷ (Total − Cancelled) × 100, rounded to 1 decimal. Cancelled tasks are "not work",
    /// so they don't count against completion. Returns 0 when there is nothing to do.
    /// </summary>
    public static double CompletionPercent(int totalTasks, int doneTasks, int cancelledTasks)
    {
        var relevant = totalTasks - cancelledTasks;
        return relevant <= 0 ? 0 : Math.Round(100.0 * doneTasks / relevant, 1);
    }
}
