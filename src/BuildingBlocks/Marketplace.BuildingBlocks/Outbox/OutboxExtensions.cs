using System.Text.Json;
using Marketplace.BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Marketplace.BuildingBlocks.Outbox;

public static class OutboxExtensions
{
    /// <summary>OutboxMessages tablosunu modele ekler. Her servisin DbContext'inde OnModelCreating içinde çağrılır.</summary>
    public static void AddOutboxMessage(this ModelBuilder modelBuilder, string? schema = null)
    {
        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("OutboxMessages", schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).IsRequired();
            e.Property(x => x.Payload).IsRequired();
            // İşlenmemişleri hızlı taramak için filtreli index.
            e.HasIndex(x => new { x.ProcessedOn, x.OccurredOn });
        });
    }

    /// <summary>
    /// Bir integration event'i outbox'a ekler. Çağıran, iş verisiyle birlikte tek SaveChanges yapmalıdır
    /// (atomik yazım). Event bu noktada YAYINLANMAZ; dispatcher sonradan yayınlar.
    /// </summary>
    public static void EnqueueIntegrationEvent(this DbContext db, IntegrationEvent @event)
    {
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Type = @event.GetType().AssemblyQualifiedName!,
            Payload = JsonSerializer.Serialize(@event, @event.GetType()),
            OccurredOn = @event.OccurredOn
        });
    }
}
