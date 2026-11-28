using JobHuntHQ.Models;
using Microsoft.EntityFrameworkCore;

namespace JobHuntHQ.Tests;

public class TrackerImportTests
{
    [Fact]
    public async Task ImportCsv_MapsTrackerAliasesAndDedups()
    {
        const string csv =
            "Company Name,Position,Stage,Date Applied,Job Link,Salary,Next Step,Follow Up Date,Comments\n" +
            "\"Fabrikam\",\"QA Engineer\",\"Phone Screen\",\"2026-03-02\",\"https://jobs.example/fabrikam\",\"$120,000\",\"Send thanks\",\"2026-03-09\",\"referred\"\n";

        await TestApp.UsingDb(async db =>
        {
            var importer = TestApp.Import(db, TestApp.UserA);
            var (first, firstError) = await importer.ImportCsvTextAsync(csv);
            Assert.Null(firstError);
            Assert.Equal(1, first.ImportedCount);

            var app = await db.JobApplications.SingleAsync();
            Assert.Equal(TestApp.UserA, app.UserId);
            Assert.Equal("Fabrikam", app.Company);
            Assert.Equal("QA Engineer", app.RoleTitle);
            Assert.Equal(ApplicationStatus.Screening, app.Status);
            Assert.Equal(new DateOnly(2026, 3, 2), app.DateApplied);
            Assert.Equal("https://jobs.example/fabrikam", app.Url);
            Assert.Equal(120000, app.SalaryMin);
            Assert.Null(app.SalaryMax);
            Assert.Equal("Send thanks", app.NextAction);
            Assert.Equal(new DateOnly(2026, 3, 9), app.NextActionDue);
            Assert.Equal("referred", app.Notes);

            var (second, secondError) = await importer.ImportCsvTextAsync(csv);
            Assert.Null(secondError);
            Assert.Equal(0, second.ImportedCount);
            Assert.Equal(1, second.DuplicateCount);
            Assert.Equal(1, await db.JobApplications.CountAsync());
        });
    }

    [Fact]
    public async Task ImportCsv_MapsStatusAliasesAndRejectsHeaderWithoutTitle()
    {
        var cases = new (string Status, ApplicationStatus Expected, bool Unrecognized)[]
        {
            ("bookmarked", ApplicationStatus.Saved, false),
            ("submitted", ApplicationStatus.Applied, false),
            ("Technical Interview", ApplicationStatus.Interview, false),
            ("Final Round", ApplicationStatus.Interview, false),
            ("Offer", ApplicationStatus.Offer, false),
            ("no offer", ApplicationStatus.Rejected, false),
            ("declined", ApplicationStatus.Rejected, false),
            ("archived", ApplicationStatus.Ghosted, false),
            ("withdrawn by candidate", ApplicationStatus.Withdrawn, false),
            ("maybe later", ApplicationStatus.Saved, true)
        };

        await TestApp.UsingDb(async db =>
        {
            var importer = TestApp.Import(db, TestApp.UserA);
            foreach (var (status, expected, unrecognized) in cases)
            {
                var company = "Co " + status;
                var csv = "Company,Title,Status\n\"" + company + "\",\"Role\",\"" + status + "\"\n";
                var (result, error) = await importer.ImportCsvTextAsync(csv);
                Assert.Null(error);
                Assert.Equal(1, result.ImportedCount);
                var app = await db.JobApplications.SingleAsync(row => row.Company == company);
                Assert.Equal(expected, app.Status);
                if (status == "no offer")
                {
                    Assert.NotEqual(ApplicationStatus.Offer, app.Status);
                }

                if (unrecognized)
                {
                    Assert.Contains(result.Warnings, warning =>
                        warning.Contains("not recognized", StringComparison.OrdinalIgnoreCase));
                }
            }
        });

        await TestApp.UsingDb(async db =>
        {
            var importer = TestApp.Import(db, TestApp.UserA);
            var (result, error) = await importer.ImportCsvTextAsync("Company,Notes\nAcme,hello\n");
            Assert.False(string.IsNullOrWhiteSpace(error));
            Assert.Equal(0, result.ImportedCount);
            Assert.Equal(0, await db.JobApplications.CountAsync());
        });
    }

    [Fact]
    public async Task ImportCsv_WarnsAndImportsWhenDateIsUnparsed()
    {
        await TestApp.UsingDb(async db =>
        {
            var importer = TestApp.Import(db, TestApp.UserA);
            var (result, error) = await importer.ImportCsvTextAsync(
                "Company,Title,Date Applied\nDateCo,QA,\"March 2\"\n");

            Assert.Null(error);
            Assert.Equal(1, result.ImportedCount);
            var app = await db.JobApplications.SingleAsync();
            Assert.Equal("DateCo", app.Company);
            Assert.Null(app.DateApplied);
            Assert.Contains(result.Warnings, warning => warning.Contains("March 2", StringComparison.Ordinal));
        });
    }
}
