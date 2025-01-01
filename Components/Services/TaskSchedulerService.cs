using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using nump.Components.Database;
using nump.Components.Classes;
using System.Text.Json;
public class TaskSchedulerService
{
    private readonly IServiceScopeFactory _scopeFactory;
    public List<ScheduledTaskTimer> _timers = new List<ScheduledTaskTimer>();
    public TaskSchedulerService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }
    public class ScheduledTaskTimer
    {
        public Timer Timer { get; set; }
        public Guid TaskGuid { get; set; }

        public ScheduledTaskTimer(Timer timer, Guid task)
        {
            Timer = timer;
            TaskGuid = task;
        }
    }
    public async Task StartSchedulingAsync()
    {
        // Create a scope for resolving scoped services like DbContext
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<NumpContext>();

            // Fetch the list of tasks to be scheduled
            var scheduleInfo = await dbContext.Tasks
                .Where(x => x.Enabled == true)
                .ToListAsync();

            if (scheduleInfo != null && scheduleInfo.Any())
            {
                foreach (var task in scheduleInfo)
                {
                    await AddTimerForTask(task);
                }
            }
        }
    }

private void RemoveExistingTimerForTask(TaskProcess task)
{
    // Find the existing ScheduledTaskTimer that matches the given task
    var existingTimer = _timers.FirstOrDefault(st => st.TaskGuid == task.Guid);
    if (existingTimer != null)
    {
        existingTimer.Timer.Dispose(); // Dispose of the old timer
        _timers.Remove(existingTimer); // Remove it from the list
    }
}

    public async Task AddTimerForTask(TaskProcess task)
    {
            RemoveExistingTimerForTask(task);
        // Create a scope for resolving scoped services like DbContext
        using (var scope = _scopeFactory.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<NumpContext>();

            // You can directly use the task's NextRunTime to schedule it.
            var freq = JsonSerializer.Deserialize<Frequency>(task.Frequency);
            if (freq?.type == 4)
            {
                Console.WriteLine("parent task enabled. skipping");
                return; // Skip task if the type is 4
            }

            // Calculate the delay time for the task
            var delayTime = task.NextRunTime - DateTime.Now;

            if (delayTime > TimeSpan.Zero)
            {
                // Create a new Timer for this specific task
                var timer = new Timer(
                    async _ => await RunScheduledTaskAsync(task.Guid),
                    null,
                    (int)delayTime.Value.TotalMilliseconds,
                    Timeout.Infinite
                );

                var scheduledTaskTimer = new ScheduledTaskTimer(timer, task.Guid);
                _timers.Add(scheduledTaskTimer);

                Console.WriteLine($"{task.Name} scheduled to run at {task.NextRunTime}");
            }
        }
    }
    private async Task RunScheduledTaskAsync(Guid taskGuid)
    {
        try
        {
        using (var scope = _scopeFactory.CreateScope())
        {
            // Resolve the UserService from the created scope
            var userService = scope.ServiceProvider.GetRequiredService<nump.Components.Services.UserService>();
    
            // Now you can call ActuallyDoTask on the scoped service
            await userService.ActuallyDoTask(taskGuid);
        }
        }
        catch (Exception ex)
        {
            // Handle any exceptions that may occur during the task execution
            Console.WriteLine($"Error executing task {taskGuid}: {ex.Message}");
        }
    }

}