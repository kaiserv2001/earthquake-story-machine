using Microsoft.EntityFrameworkCore;
using Quake.Data.Entities;

namespace Quake.Data;

public class QuakeDbContext(DbContextOptions<QuakeDbContext> options) : DbContext(options)
{
    public DbSet<StoryCardRecord> StoryCards => Set<StoryCardRecord>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<StoryCardRecord>(e =>
        {
            e.HasIndex(x => x.QuakeId).IsUnique();
            e.Property(x => x.QuakeId).HasMaxLength(64);
            e.Property(x => x.Place).HasMaxLength(256);
            e.Property(x => x.BlobPath).HasMaxLength(512);
        });
    }
}
