public class TaskTimers
{
    // Store scheduled tasks (with a unique identifier for each task)
    private readonly Dictionary<int, Timer> _timers = new Dictionary<int, Timer>();

    // Method to add a task to the scheduler
    public void AddTask(int taskId, Timer timer)
    {
        if (!_timers.ContainsKey(taskId))
        {
            _timers.Add(taskId, timer);
        }
    }

    // Method to remove a task from the scheduler
    public void RemoveTask(int taskId)
    {
        if (_timers.ContainsKey(taskId))
        {
            _timers[taskId].Dispose();  // Stop the timer
            _timers.Remove(taskId);
        }
    }

    // Retrieve all scheduled tasks
    public Dictionary<int, Timer> GetScheduledTasks() => _timers;
}