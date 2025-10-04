var builder = DistributedApplication.CreateBuilder(args);

// Parameter deklarieren mit Default-Werten
var rmqUser = builder.AddParameter("RabbitMQUser", "guest", secret: false);
var rmqPassword = builder.AddParameter("RabbitMQPassword", "guest", secret: false);

// RabbitMQ mit diesen Parametern hinzufügen
var rabbitmq = builder.AddRabbitMQ("rabbitmq", rmqUser, rmqPassword)
                      .WithManagementPlugin();

// Parameter zur Ressource zuordnen (Parent Relationship)
rmqUser.WithParentRelationship(rabbitmq);
rmqPassword.WithParentRelationship(rabbitmq);

// Optional: zusätzliches Setup, Endpoints etc.
// rabbitmq = rabbitmq.WithEndpoint(...);

builder.Build().Run();