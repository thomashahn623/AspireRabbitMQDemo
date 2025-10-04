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
var publisher = builder.AddProject<Projects.MessagePublisher>("messagepublisher")
                       .WithReference(rabbitmq)
                       // hier sagst du: warte auf rabbitmq, bis es läuft
                       .WaitFor(rabbitmq);

// Consumer Projekt
var consumer = builder.AddProject("messageconsumer", "../MessageConsumer/MessageConsumer.csproj")
                      .WithReference(rabbitmq)
                      .WaitFor(rabbitmq);

builder.Build().Run();