using System.Security.Claims;
using JobHuntHQ.Controllers;
using JobHuntHQ.Data;
using JobHuntHQ.Models;
using JobHuntHQ.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace JobHuntHQ.Tests;

internal static class TestApp
{
    public const string UserA = "user-a";
    public const string UserB = "user-b";

    private static readonly HttpClient Http = new();

    public static async Task UsingDb(Func<ApplicationDbContext, Task> action)
    {
        var (db, connection) = SqliteAppDb.Create();
        try
        {
            await action(db);
        }
        finally
        {
            await db.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    public static ApplicationsController Applications(ApplicationDbContext db, string userId)
    {
        var resumes = new ResumeTextService(new UnusedResumeStorage());
        var controller = new ApplicationsController(
            db,
            new FitCheckService(),
            new AiPacketService(resumes),
            new InterviewPacketService(resumes),
            new PostingFetchService(Http));
        Bind(controller, userId, tempData: true);
        return controller;
    }

    public static ImportController Import(ApplicationDbContext db, string userId)
    {
        var controller = new ImportController(
            db,
            new StubWebHostEnvironment(),
            new PostingFetchService(Http),
            new FitCheckService());
        Bind(controller, userId, tempData: true);
        return controller;
    }

    public static HomeController Home(ApplicationDbContext db, string userId)
    {
        var controller = new HomeController(NullLogger<HomeController>.Instance, db);
        Bind(controller, userId, tempData: false);
        return controller;
    }

    public static CalendarController Calendar(ApplicationDbContext db)
    {
        var controller = new CalendarController(db);
        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("calendar.example");
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return controller;
    }

    public static JobApplication App(
        string userId,
        string company,
        ApplicationStatus status = ApplicationStatus.Saved,
        DateOnly? dateApplied = null)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return new JobApplication
        {
            UserId = userId,
            Company = company,
            RoleTitle = "Engineer",
            Status = status,
            DateSaved = dateApplied ?? today,
            DateApplied = dateApplied,
            CreatedUtc = DateTime.UtcNow,
            UpdatedUtc = DateTime.UtcNow
        };
    }

    private static void Bind(Controller controller, string userId, bool tempData)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, userId) },
                authenticationType: "Test"))
        };
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        if (tempData)
        {
            // The shared framework no longer ships NullTempDataProvider; this no-op
            // provider keeps TempData readable for the rest of the test call.
            controller.TempData = new TempDataDictionary(http, new NullTempDataProvider());
        }
    }

    private sealed class NullTempDataProvider : ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(HttpContext context) =>
            new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public void SaveTempData(HttpContext context, IDictionary<string, object> values)
        {
        }
    }

    private sealed class UnusedResumeStorage : IResumeStorageService
    {
        public Task UploadAsync(string blobName, Stream content, string contentType, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<Stream?> DownloadAsync(string blobName, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream?>(null);

        public Task DeleteAsync(string blobName, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class StubWebHostEnvironment : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "JobHuntHQ.Tests";
        public string WebRootPath { get; set; } = string.Empty;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
