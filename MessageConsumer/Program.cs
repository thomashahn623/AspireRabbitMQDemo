using MassTransit;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MessageConsumer;

using Contracts;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.AddConsole();

        builder.Services.AddMassTransit(x =>
        {
            x.AddConsumer<SomethingHappenedConsumer>();

            x.UsingRabbitMq((context, cfg) =>
            {
                var configuration = context.GetRequiredService<IConfiguration>();
                var conn = configuration.GetConnectionString("rabbitmq");
                if (string.IsNullOrWhiteSpace(conn))
                    throw new InvalidOperationException("ConnectionString 'rabbitmq' fehlt.");

                cfg.Host(new Uri(conn));

                cfg.ReceiveEndpoint("something-happened-queue", e =>
                {
                    // In-Memory Outbox aktivieren (stellt sicher, dass Publish/Send innerhalb der Consumer-
                    // Pipeline erst nach erfolgreichem Abschluss der Message verarbeitet werden und verhindert
                    // doppelte Publikationen bei Retries). Für persistente, echte Outbox (EF Core, Postgres etc.)
                    // müsste ein DbContext + AddEntityFrameworkOutbox konfiguriert werden.
                    e.UseInMemoryOutbox(context);
                    e.ConfigureConsumer<SomethingHappenedConsumer>(context);
                });
            });
        });

        var app = builder.Build();
        await app.RunAsync();
    }
}

public class SomethingHappenedConsumer(ILogger<SomethingHappenedConsumer> logger) : IConsumer<SomethingHappened>
{
    public Task Consume(ConsumeContext<SomethingHappened> context)
    {
        var msg = context.Message;
        logger.LogInformation("Event empfangen: {Id} von {Source} @ {Time}", msg.Id, msg.Source, msg.OccurredAtUtc);
        return Task.CompletedTask;
    }
}
