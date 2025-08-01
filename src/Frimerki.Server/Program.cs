using System.Security.Cryptography;
using System.Text;

using Frimerki.Data;
using Frimerki.Protocols;
using Frimerki.Server;
using Frimerki.Server.Middleware;
using Frimerki.Services;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

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

// Configure JWT Authentication
var jwtSecret = builder.Configuration["Jwt:Secret"];
if (string.IsNullOrEmpty(jwtSecret)) {
    // Generate a random secret for development
    var bytes = new byte[32];
    using var rng = RandomNumberGenerator.Create();
    rng.GetBytes(bytes);
    jwtSecret = Convert.ToBase64String(bytes);
    Log.Warning("JWT secret not configured, using generated secret. This should be set in production.");
}

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options => {
        options.TokenValidationParameters = new TokenValidationParameters {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "Frimerki",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"] ?? "Frimerki",
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };
    });

builder.Services.AddAuthorization();

// Configure Entity Framework - Global Database
builder.Services.AddDbContext<GlobalDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("GlobalConnection") ?? "Data Source=frimerki_global.db"));

// Configure Entity Framework - Legacy single database (for migration period)
builder.Services.AddDbContext<EmailDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));


builder.Services
    .AddFrimerkiServices() // Add Frimerki services
    .AddEmailProtocols() // Add Email Protocols (IMAP, SMTP, POP3)
    .AddSignalR(); // Add SignalR

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment()) {
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Serve static files from wwwroot
app.UseStaticFiles();

// Add domain context middleware
app.UseDomainContext();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHub<EmailHub>("/hubs/email");

// Fallback to serve the hello world skip at root
app.MapFallbackToFile("index.html");

try {
    Log.Information("Starting Frímerki Email Server");

    // Ensure databases are created
    using (var scope = app.Services.CreateScope()) {
        // Initialize global database
        var globalContext = scope.ServiceProvider.GetRequiredService<GlobalDbContext>();
        await globalContext.Database.EnsureCreatedAsync();
        Log.Information("Global database initialized successfully");

        // Keep legacy database for migration period
        var emailContext = scope.ServiceProvider.GetRequiredService<EmailDbContext>();
        emailContext.Database.EnsureCreated();
        Log.Information("Legacy email database initialized successfully");
    }

    app.Run();
} catch (Exception ex) {
    Log.Fatal(ex, "Frímerki Email Server terminated unexpectedly");
} finally {
    Log.CloseAndFlush();
}

// Make Program accessible for testing
public partial class Program { }
