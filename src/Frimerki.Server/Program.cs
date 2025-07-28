using Frimerki.Data;
using Frimerki.Services;
using Frimerki.Server;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File("logs/frimerki-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

// Add services to the container
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure Entity Framework
builder.Services.AddDbContext<EmailDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Add Frimerki services
builder.Services.AddFrimerkiServices();

// Add SignalR
builder.Services.AddSignalR();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Serve static files from wwwroot
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<EmailHub>("/hubs/email");

// Fallback to serve the hello world page at root
app.MapFallbackToFile("index.html");

try
{
    Log.Information("Starting Frímerki Email Server");
    
    // Ensure database is created
    using (var scope = app.Services.CreateScope())
    {
        var context = scope.ServiceProvider.GetRequiredService<EmailDbContext>();
        context.Database.EnsureCreated();
        Log.Information("Database initialized successfully");
    }
    
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Frímerki Email Server terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
