var builder = DistributedApplication.CreateBuilder(args);

// Parameter, RabbitMQ etc.
var rmqUser = builder.AddParameter("RabbitMQUser", "guest", secret: false);
var rmqPassword = builder.AddParameter("RabbitMQPassword", "guest", secret: false);

var rabbitmq = builder.AddRabbitMQ("rabbitmq", rmqUser, rmqPassword)
                      .WithManagementPlugin();
                      // optional: fixe Port-Mappings
                      //.WithEndpoint(5672, 5672)
                      //.WithEndpoint(15672, 15672);

// Dein Publisher-Projekt
// PostgreSQL Datenbank für Outbox
var pg = builder.AddPostgres("pg")
                .WithDataVolume()
                .AddDatabase("outboxdb");

var publisher = builder.AddProject<Projects.MessagePublisher>("messagepublisher")
                       .WithReference(rabbitmq)
                       .WithReference(pg)
                       .WaitFor(rabbitmq)
                       .WaitFor(pg);

// Consumer Projekt
var consumer = builder.AddProject("messageconsumer", "../MessageConsumer/MessageConsumer.csproj")
                      .WithReference(rabbitmq)
                      .WithReference(pg)
                      .WaitFor(rabbitmq)
                      .WaitFor(pg);

builder.Build().Run();