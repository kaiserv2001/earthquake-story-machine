using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Quake.Data;

/// <summary>
/// Lets `dotnet ef migrations add` build a context at design time without the
/// Functions host or a live connection string. The placeholder connection is
/// only used to discover the SQL Server provider's model shape, never opened.
/// </summary>
public sealed class DesignTimeQuakeDbContextFactory : IDesignTimeDbContextFactory<QuakeDbContext>
{
    public QuakeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<QuakeDbContext>()
            .UseSqlServer("Server=localhost;Database=QuakeDb;Trusted_Connection=False;")
            .Options;
        return new QuakeDbContext(options);
    }
}
