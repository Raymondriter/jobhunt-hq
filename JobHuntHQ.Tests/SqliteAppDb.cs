using JobHuntHQ.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace JobHuntHQ.Tests;

internal static class SqliteAppDb
{
    public static (ApplicationDbContext Db, SqliteConnection Connection) Create()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(connection)
            .Options;
        var db = new ApplicationDbContext(options);
        db.Database.EnsureCreated();
        return (db, connection);
    }
}
