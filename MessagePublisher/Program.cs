using System;
using System.Threading;
using System.Threading.Tasks;
using MassTransit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Contracts;

namespace MessagePublisher;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);

        builder.Logging.AddConsole();

        // MassTransit konfigurieren und RabbitMQ aus Aspire-ConnectionString ziehen
        builder.Services.AddMassTransit(x =>
        {
            x.UsingRabbitMq((context, cfg) =>
            {
                // Hole IConfiguration über das Service-Providerfach
                var configuration = context.GetRequiredService<IConfiguration>();
                var conn = configuration.GetConnectionString("rabbitmq");
                if (string.IsNullOrWhiteSpace(conn))
                    throw new InvalidOperationException("ConnectionString 'rabbitmq' fehlt.");

                cfg.Host(new Uri(conn));

                // Optional: Endpoints automatisch konfigurieren
                cfg.ConfigureEndpoints(context);
            });
        });

        // Hintergrund-Worker registrieren
        builder.Services.AddHostedService<MessagePublishingWorker>();

        var app = builder.Build();
        await app.RunAsync();
    }
}


// Worker, der periodisch Events publisht
public class MessagePublishingWorker(ILogger<MessagePublishingWorker> logger, IPublishEndpoint publish)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("MessagePublishingWorker gestartet.");

        while (!stoppingToken.IsCancellationRequested)
        {
            var evt = new SomethingHappened(
                Id: Guid.NewGuid(),
                Source: "MessagePublisher",
                OccurredAtUtc: DateTime.UtcNow
            );

            await publish.Publish(evt, stoppingToken);
            logger.LogInformation("Event publiziert: {Id} @ {Time}", evt.Id, evt.OccurredAtUtc);

            await Task.Delay(TimeSpan.FromMilliseconds(5), stoppingToken);
        }
    }
}