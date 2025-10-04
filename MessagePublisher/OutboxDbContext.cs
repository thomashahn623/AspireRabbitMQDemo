using System.Reflection;
using MassTransit;
using MassTransit.EntityFrameworkCoreIntegration;
using Microsoft.EntityFrameworkCore;

namespace MessagePublisher;

// DbContext, der sowohl Domain (falls später nötig) als auch die MassTransit Outbox Tabellen hält
public class OutboxDbContext : DbContext
{
    public OutboxDbContext(DbContextOptions<OutboxDbContext> options) : base(options)
    {
    }

    public DbSet<OutboxHeartbeat> Heartbeats => Set<OutboxHeartbeat>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // MassTransit Outbox Entities konfigurieren
        modelBuilder.AddInboxStateEntity();
        modelBuilder.AddOutboxMessageEntity();
        modelBuilder.AddOutboxStateEntity();

        base.OnModelCreating(modelBuilder);
    }
}

public class OutboxHeartbeat
{
    public int Id { get; set; }
    public DateTime CreatedUtc { get; set; }
}
