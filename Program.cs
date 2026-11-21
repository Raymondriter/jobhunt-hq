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
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

builder.Services.AddDefaultIdentity<IdentityUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Blob Storage for resume PDFs (M3). Authentication is DefaultAzureCredential:
// in Azure the AZURE_CLIENT_ID app setting points it at this app's
// user-assigned managed identity, so there are no storage account keys
// anywhere; locally it falls back to developer credentials (Azure CLI /
// Visual Studio sign-in). The client is only constructed here — no token is
// requested until the first blob call, so the app starts fine without one.
builder.Services.Configure<BlobStorageOptions>(
    builder.Configuration.GetSection(BlobStorageOptions.SectionName));
builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<BlobStorageOptions>>().Value;
    return new BlobServiceClient(new Uri(options.ServiceUri), new DefaultAzureCredential());
});
builder.Services.AddScoped<IResumeStorageService, ResumeStorageService>();

// M4: deterministic fit check + bring-your-own-AI packet generation.
builder.Services.AddScoped<FitCheckService>();
builder.Services.AddScoped<AiPacketService>();

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
        db.Database.Migrate();
    }
    catch (Exception ex)
    {
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "Failed to apply database migrations at startup. The app will start, but database-backed pages will not work until the database is reachable.");
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
