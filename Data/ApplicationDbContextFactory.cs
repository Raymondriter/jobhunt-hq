using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace JobHuntHQ.Data;

/// <summary>
/// Lets the EF Core tools (<c>dotnet ef migrations add</c>, etc.) create the
/// context without running the app or needing a live database. The connection
/// string here is a placeholder for tooling only — it is never used to connect.
/// The real connection string comes from configuration at runtime
/// ("ConnectionStrings:DefaultConnection", set in Azure App Service config in
/// production — never committed to this repo).
/// </summary>
public class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=JobHuntHQ;Trusted_Connection=True;MultipleActiveResultSets=true");
        return new ApplicationDbContext(optionsBuilder.Options);
    }
}
