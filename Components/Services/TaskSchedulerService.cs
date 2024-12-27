using nump.Components.Classes;
using nump.Components.Database;

public class TaskSchedulerService
{

    protected readonly NumpContext _context;

    public TaskSchedulerService(NumpContext context)
    {
        _context = context;
        Initialize();
    }


    private void Initialize()
    {

        List<NumpInstructionSet> Tasks = _context.Tasks.ToList();
        int id = 0;
        foreach (NumpInstructionSet task in Tasks)
        {
            ScheduleTask(task);
            id++;
        }
    }
    public void ScheduleTask(NumpInstructionSet instruction)
    {
        DateTime scheduledTime = new DateTime();
        if (instruction._frequency.date != null && instruction._frequency.time != null)
        {
            scheduledTime = instruction._frequency.date.Value.ToDateTime((TimeOnly)instruction._frequency.time);
        }
        else if (instruction._frequency.time != null)
        {
            scheduledTime = GetNextDateTime((TimeOnly)instruction._frequency.time);
        }
        instruction.CancelToken = new CancellationTokenSource();
        //Action task = new Action(() => Yeet(instruction, instruction.cancelToken.Token));
        // Calculate the time remaining until the scheduled time
        TimeSpan timeUntilTask = scheduledTime - DateTime.Now;

        // If the scheduled time is in the past, throw an exception
        if (timeUntilTask <= TimeSpan.Zero)
        {
            Console.WriteLine("The scheduled time must be in the future.");
            return;
        }

        // Use a timer to trigger the task after the calculated time
        Timer timer = new Timer(_ =>
        {
            Console.WriteLine($"Task started at {DateTime.Now}");
        }, null, timeUntilTask, Timeout.InfiniteTimeSpan); // Only execute once
    }


    private async Task<bool> CheckHeader(List<string> headers, List<string> requiredColumns)
    {
        return requiredColumns.All(item => headers.Contains(item, StringComparer.OrdinalIgnoreCase));
    }

    static DateTime GetNextDateTime(TimeOnly targetTime)
    {
        DateTime currentDateTime = DateTime.Now;  // Current date and time
        DateTime todayAtTargetTime = currentDateTime.Date.Add(targetTime.ToTimeSpan());

        // If the target time is later today, use today's date, else use tomorrow's date
        if (currentDateTime < todayAtTargetTime)
        {
            return todayAtTargetTime;  // Use today's date at the target time
        }
        else
        {
            return todayAtTargetTime.AddDays(1);  // Use tomorrow's date at the target time
        }
    }

}