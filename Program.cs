using Azure.Identity;
using Azure.Storage.Blobs;
using JobHuntHQ.Data;
using JobHuntHQ.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// Application Insights (M3). The connection string comes from the
// APPLICATIONINSIGHTS_CONNECTION_STRING app setting in Azure — nothing is
// hard-coded. Registration is conditional on a connection string being
// configured: the 3.x (OpenTelemetry-based) exporter throws at startup when
// none is set, unlike the classic 2.x SDK which quietly disabled itself.
// With this guard the app runs everywhere; in Azure, where the setting
// exists, telemetry flows.
var appInsightsConfigured =
    !string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"])
    || !string.IsNullOrWhiteSpace(builder.Configuration["ApplicationInsights:ConnectionString"]);
if (appInsightsConfigured)
{
    builder.Services.AddApplicationInsightsTelemetry();
}
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
var useSqlite = builder.Environment.IsDevelopment()
    && connectionString.Contains(".db", StringComparison.OrdinalIgnoreCase);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    if (useSqlite)
    {
        options.UseSqlite(connectionString);
    }
    else
    {
        options.UseSqlServer(connectionString, sqlOptions =>
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 6,
                maxRetryDelay: TimeSpan.FromSeconds(15),
                errorNumbersToAdd: null));
    }
});

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Blob Storage for resume PDFs (M3). Authentication is DefaultAzureCredential:
// in Azure the AZURE_CLIENT_ID app setting points it at this app's
// user-assigned managed identity, so there are no storage account keys
// anywhere; locally it falls back to developer credentials (Azure CLI /
// Visual Studio sign-in) or an optional Development-only LocalFolder.
builder.Services.Configure<BlobStorageOptions>(
    builder.Configuration.GetSection(BlobStorageOptions.SectionName));
var localBlobFolder = builder.Environment.IsDevelopment()
    ? builder.Configuration[$"{BlobStorageOptions.SectionName}:{nameof(BlobStorageOptions.LocalFolder)}"]
    : null;
if (!string.IsNullOrWhiteSpace(localBlobFolder))
{
    var resolvedFolder = Path.IsPathRooted(localBlobFolder)
        ? localBlobFolder
        : Path.Combine(builder.Environment.ContentRootPath, localBlobFolder);
    builder.Services.AddScoped<IResumeStorageService>(_ => new LocalResumeStorageService(resolvedFolder));
}
else
{
    builder.Services.AddSingleton(sp =>
    {
        var options = sp.GetRequiredService<IOptions<BlobStorageOptions>>().Value;
        return new BlobServiceClient(new Uri(options.ServiceUri), new DefaultAzureCredential());
    });
    builder.Services.AddScoped<IResumeStorageService, ResumeStorageService>();
}

// M4: deterministic fit check + bring-your-own-AI packet generation.
// Phase 2: resume PDF text extraction for the packet, and the SSRF-guarded
// posting-page fetcher. Auto-redirect is disabled on the fetcher's handler
// because PostingFetchService follows redirects manually, re-validating
// every hop against its SSRF guards.
builder.Services.AddScoped<FitCheckService>();
builder.Services.AddScoped<AiPacketService>();
builder.Services.AddScoped<InterviewPacketService>();
builder.Services.AddScoped<ResumeTextService>();
builder.Services.AddHttpClient<PostingFetchService>()
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });

builder.Services.AddControllersWithViews();

var app = builder.Build();

// Apply pending EF Core migrations at startup. Wrapped in try/catch: if the
// database is unreachable the app should still start (the landing page works;
// Identity and data pages will fail until the database is back), and the
// failure must be visible in the logs rather than a silent crash loop.
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (useSqlite)
        {
            Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
            db.Database.EnsureCreated();
        }
        else
        {
            db.Database.Migrate();
        }
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogWarning(ex, "Initial database migration attempt did not complete (Azure SQL AutoPause may still be waking up). Scheduling background retry.");
        if (!useSqlite)
        {
            _ = Task.Run(async () =>
            {
                foreach (var delaySeconds in new[] { 20, 30, 45 })
                {
                    await Task.Delay(TimeSpan.FromSeconds(delaySeconds));
                    try
                    {
                        using var retryScope = app.Services.CreateScope();
                        var retryDb = retryScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                        await retryDb.Database.MigrateAsync();
                        logger.LogInformation("Background database migration succeeded after Azure SQL wake-up.");
                        return;
                    }
                    catch (Exception retryEx)
                    {
                        logger.LogWarning(retryEx, "Background database migration retry failed; will retry if attempts remain.");
                    }
                }
            });
        }
    }
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

app.Run();
