using System;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contracts;

namespace MessagePublisher;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.AddConsole();

        // PostgreSQL ConnectionString aus Aspire (Ressourcenname: pg / Datenbank: outboxdb)
        var pgConn = builder.Configuration.GetConnectionString("pg:outboxdb")
                     ?? builder.Configuration.GetConnectionString("outboxdb")
                     ?? throw new InvalidOperationException("PostgreSQL ConnectionString 'pg:outboxdb' fehlt.");
        builder.Services.AddDbContext<OutboxDbContext>(opt =>
            opt.UseNpgsql(pgConn)
        );

        // MassTransit mit EF Core Outbox (Bus Outbox) konfigurieren
        builder.Services.AddMassTransit(x =>
        {
            // EF Outbox aktivieren: Speichert Publish/Send Vorgänge zuerst in DB und dispatcht asynchron
            x.AddEntityFrameworkOutbox<OutboxDbContext>(o =>
            {
                o.UseBusOutbox(); // nutzt Outbox beim Publish über den Bus
                o.QueryDelay = TimeSpan.FromSeconds(1); // Poll-Intervall
                o.DuplicateDetectionWindow = TimeSpan.FromMinutes(10);
                o.UsePostgres();
            });

            x.UsingRabbitMq((context, cfg) =>
            {
                var configuration = context.GetRequiredService<IConfiguration>();
                var conn = configuration.GetConnectionString("rabbitmq");
                if (string.IsNullOrWhiteSpace(conn))
                    throw new InvalidOperationException("ConnectionString 'rabbitmq' fehlt.");

                cfg.Host(new Uri(conn));
                cfg.ConfigureEndpoints(context);
            });
        });

        // Hintergrund-Worker registrieren
        builder.Services.AddHostedService<MessagePublishingWorker>();

        var app = builder.Build();

        // Stelle sicher, dass die Outbox-Datenbank + Tabellen existieren
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();
            // Für Demo weiterhin EnsureCreated; Empfehlung: Migrationen einsetzen.
            db.Database.EnsureCreated();
        }
        await app.RunAsync();
    }
}


// Worker, der periodisch Events publisht
public class MessagePublishingWorker(ILogger<MessagePublishingWorker> logger, IServiceProvider services)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MessagePublishingWorker gestartet (EF Outbox aktiv).");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = services.CreateScope();
                var publish = scope.ServiceProvider.GetRequiredService<IPublishEndpoint>();
                var db = scope.ServiceProvider.GetRequiredService<OutboxDbContext>();

                var evt = new SomethingHappened(
                    Id: Guid.NewGuid(),
                    Source: "MessagePublisher",
                    OccurredAtUtc: DateTime.UtcNow
                );

                // Publish wird durch EF Outbox abgefangen und erst nach SaveChanges dispatched.
                await publish.Publish(evt, stoppingToken);

                db.Heartbeats.Add(new OutboxHeartbeat { CreatedUtc = DateTime.UtcNow });

                // SaveChanges sorgt dafür, dass die Outbox-Nachrichten persistiert und vom Dispatcher später gesendet werden.
                await db.SaveChangesAsync(stoppingToken);

                logger.LogInformation("Event in Outbox gespeichert und zur Dispatch freigegeben: {Id}", evt.Id);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Fehler beim Erzeugen/Publizieren eines Events");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(200), stoppingToken); // kleinere Frequenz genügt für Demo
        }
    }
}