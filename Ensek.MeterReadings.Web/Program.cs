using Microsoft.EntityFrameworkCore;
using Ensek.MeterReadings.Domain.Interfaces; // Domain interfaces
using Ensek.MeterReadings.Data; // DbContext
using Ensek.MeterReadings.Data.Repositories; // Repository implementation
using Ensek.MeterReadings.Services; // Service implementations
using Ensek.MeterReadings.Services.Validation; // Validation rules
using Microsoft.Extensions.Logging; // Added for explicit logging access
using System; // Added for Exception, TimeSpan
using Microsoft.AspNetCore.Builder; // Added for WebApplication
using Microsoft.Extensions.DependencyInjection; // Added for IServiceCollection
using Microsoft.Extensions.Hosting; // Added for IHostEnvironment

var builder = WebApplication.CreateBuilder(args);

// --- Configuration ---
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
if (string.IsNullOrEmpty(connectionString))
{
    // Use logger if available, otherwise Console.Error
    Console.Error.WriteLine("FATAL ERROR: Connection string 'DefaultConnection' not found in configuration.");
    throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
}
else
{
    // Mask sensitive parts before logging connection string
    string maskedConnectionString = connectionString;
    try
    {
        var csBuilder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrEmpty(csBuilder.Password)) csBuilder.Password = "*****";
        if (!string.IsNullOrEmpty(csBuilder.UserID)) csBuilder.UserID = "*****"; // Mask User ID too if desired
        maskedConnectionString = csBuilder.ConnectionString;
    }
    catch { /* Ignore potential parsing errors, log original if masking fails */ }
    Console.WriteLine($"Using database connection string: {maskedConnectionString}");
}


// --- Dependency Injection (Services) ---

// 1. Add MVC & API Controllers
builder.Services.AddControllersWithViews(); // For MVC

// 2. Add DbContext for SQL Server
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        // Configure SQL Server specific options like retry logic for transient faults
        sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 3, // Number of retry attempts
                maxRetryDelay: TimeSpan.FromSeconds(10), // Max delay between retries
                errorNumbersToAdd: null); // Use default list of transient error numbers
    }));

// 3. Register Domain/Service/Data dependencies
// Services Project
builder.Services.AddScoped<ICsvParsingService, CsvParsingService>();
builder.Services.AddScoped<IMeterReadingValidationService, MeterReadingValidationService>();
builder.Services.AddScoped<IMeterReadingUploadOrchestrator, MeterReadingUploadOrchestrator>();

// Validation Rules (using manual registration for clarity)
builder.Services.AddScoped<IValidationRule, AccountExistsRule>();
builder.Services.AddScoped<IValidationRule, MeterValueFormatRule>();
builder.Services.AddScoped<IValidationRule, DuplicateInBatchRule>();
builder.Services.AddScoped<IValidationRule, DuplicateInDbRule>();
builder.Services.AddScoped<IValidationRule, OlderReadingRule>();
// If using Scrutor: builder.Services.Scan(...)

// Data Project
builder.Services.AddScoped<IMeterReadingRepository, MeterReadingRepository>();

// 4. Add Logging
builder.Services.AddLogging(config =>
{
    config.AddConfiguration(builder.Configuration.GetSection("Logging"));
    config.AddConsole();
    config.AddDebug();
});

// 5. Add API Explorer & Swagger (Optional - for API testing)
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "ENSEK Meter Reading API & MVC",
        Version = "v1",
        Description = "API and MVC endpoints for uploading meter readings."
    });
});


// --- Middleware Pipeline ---
var app = builder.Build();

// Apply EF Core Migrations and Seed on startup (can be disabled for production)
// Consider moving this logic to a separate utility or handling migrations via deployment pipeline.
bool applyMigrationsOnStartup = app.Configuration.GetValue<bool>("ApplyMigrationsOnStartup", defaultValue: app.Environment.IsDevelopment()); // Default true in Dev

if (applyMigrationsOnStartup)
{
    Console.WriteLine("Attempting to apply database migrations on startup...");
    // Create a scope to resolve scoped services like DbContext
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<Program>>(); // Get logger instance
        try
        {
            var dbContext = services.GetRequiredService<ApplicationDbContext>();
            // Check for pending migrations before applying
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(); // Use async version
            if (pendingMigrations.Any())
            {
                logger.LogInformation("Applying {Count} pending database migrations...", pendingMigrations.Count());
                await dbContext.Database.MigrateAsync(); // Applies pending migrations & runs OnModelCreating (seeding)
                logger.LogInformation("Database migrations applied successfully.");
            }
            else
            {
                logger.LogInformation("No pending database migrations found.");
            }
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "An error occurred while migrating or seeding the database. Application might not function correctly.");
        }
    }
}
else
{
    Console.WriteLine("Skipping database migrations on startup based on configuration.");
}


// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage(); // Show detailed errors in Development
                                     // Enable Swagger UI only in Development
    app.UseSwagger(); // Generates swagger.json
    app.UseSwaggerUI(c => // Serves the Swagger UI HTML/JS
    {
        c.SwaggerEndpoint("/swagger/v1/swagger.json", "ENSEK API V1");
        // Serve Swagger UI at /swagger route for easier access
        c.RoutePrefix = "swagger";
    });
}
else
{
    // Use standard MVC error handler for production
    app.UseExceptionHandler("/Shared/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see [https://aka.ms/aspnetcore-hsts](https://aka.ms/aspnetcore-hsts).
    app.UseHsts();
}

app.UseHttpsRedirection(); // Redirect HTTP requests to HTTPS.
app.UseStaticFiles(); // Enable serving static files (CSS, JS, images) from wwwroot.

app.UseRouting(); // Add routing middleware.

// No Authentication/Authorization middleware needed for this version

// Map endpoints
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"); // Default MVC route

app.MapControllers(); // Map API controllers based on their attributes ([ApiController], [Route], etc.).

app.Run(); // Start the application and listen for requests.