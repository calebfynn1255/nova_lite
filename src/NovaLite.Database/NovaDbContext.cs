using Microsoft.EntityFrameworkCore;
using NovaLite.Database.Entities;
using System;
using System.IO;

namespace NovaLite.Database;

public class NovaDbContext : DbContext
{
    public DbSet<ChatSessionEntity> Sessions { get; set; } = null!;
    public DbSet<ChatMessageEntity> Messages { get; set; } = null!;
    public DbSet<UserFactEntity> UserFacts { get; set; } = null!;
    
    public NovaDbContext()
    {
    }

    public NovaDbContext(DbContextOptions<NovaDbContext> options) : base(options)
    {
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NovaLite");
            Directory.CreateDirectory(appData);
            var dbPath = Path.Combine(appData, "database.db");
            optionsBuilder.UseSqlite($"Data Source={dbPath}");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatSessionEntity>()
            .HasMany(s => s.Messages)
            .WithOne(m => m.Session)
            .HasForeignKey(m => m.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
            
        base.OnModelCreating(modelBuilder);
    }
}
