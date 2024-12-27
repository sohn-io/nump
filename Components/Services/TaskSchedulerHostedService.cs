using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System;
using System.Threading;
using System.Threading.Tasks;

public class TaskSchedulerHostedService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;

    public TaskSchedulerHostedService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Resolve the TaskSchedulerService from the DI container (it will be scoped)
        using (var scope = _serviceProvider.CreateScope())
        {
            var taskSchedulerService = scope.ServiceProvider.GetRequiredService<TaskSchedulerService>();
            // Start the task scheduler
            await taskSchedulerService.StartSchedulingAsync();
        }
     
    }
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}