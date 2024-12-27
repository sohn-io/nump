using nump.Components;
using nump.Components.Services;
using nump.Components.Database;
using nump.Components.Hubs;
using Microsoft.AspNetCore.ResponseCompression;

using Radzen;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddRadzenComponents();
builder.Services.AddRadzenCookieThemeService(options =>
{
    options.Name = "NUMPTheme"; // The name of the cookie
    options.Duration = TimeSpan.FromDays(365); // The duration of the cookie
});
builder.Services.AddSignalR();
builder.Services.AddControllersWithViews();
//Database
builder.Services.AddDbContext<NumpContext>(options =>
{
    options.UseSqlite("Data Source=nump.db;Default Timeout=30");
});
builder.Services.AddSingleton<TaskSchedulerService>();

// Register TaskSchedulerHostedService as Scoped (to manage startup)
builder.Services.AddHostedService<TaskSchedulerHostedService>();
builder.Services.AddScoped<UserService>();
builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<NotifService>();
builder.Services.AddScoped(typeof(SaveService<>), typeof(SaveService<>));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<NumpContext>();

    // Ensure the database is created
    dbContext.Database.EnsureCreated();
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();
app.MapControllers();
app.Run();
