# Level 09: Enterprise Broker Extensions

## Overview
Level 09 illustrates production-grade configuration for the four enterprise distributed messaging transports supported by the `EricksonLopez.Messaging` ecosystem: RabbitMQ, Apache Kafka, Azure Service Bus, and AWS SQS.

---

## 1. RabbitMQ Transport (`EricksonLopez.Messaging.RabbitMQ`)

Provides AMQP 0-9-1 transport integration with connection auto-recovery, topology creation, and dead-letter exchanges:

```csharp
builder.Services.AddRabbitMqMessagingTransport(options =>
{
    options.HostName = "rabbitmq.cluster.local";
    options.Port = 5672;
    options.VirtualHost = "/production";
    options.UserName = "app-service";
    options.Password = builder.Configuration["RabbitMQ:Password"];
    options.ExchangeName = "enterprise.events";
});
```

---

## 2. Apache Kafka Transport (`EricksonLopez.Messaging.Kafka`)

High-throughput distributed event streaming integration with partition-based routing and consumer group offset tracking:

```csharp
builder.Services.AddKafkaMessagingTransport(options =>
{
    options.BootstrapServers = "kafka-1:9092,kafka-2:9092";
    options.GroupId = "order-processing-group";
    options.ClientId = "order-consumer-01";
    options.EnableAutoCommit = false;
});
```

> [!NOTE]
> `EricksonLopez.Messaging.Kafka` relies on the native C++ `librdkafka` library. While fully functional on .NET 10, it does not support Native AOT trim-free compilation (`<IsAotCompatible>false</IsAotCompatible>`).

---

## 3. Azure Service Bus Transport (`EricksonLopez.Messaging.AzureServiceBus`)

Cloud-native enterprise transport supporting connection strings or passwordless Managed Identity:

```csharp
// Connection String mode
builder.Services.AddAzureServiceBusMessagingTransport(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("ServiceBus");
});

// Or Passwordless Managed Identity mode
builder.Services.AddAzureServiceBusMessagingTransport(options =>
{
    options.FullyQualifiedNamespace = "enterprise-sb.servicebus.windows.net";
    options.Credential = new DefaultAzureCredential();
});
```

- Implements native `IBatchMessageTransport` via `ServiceBusMessageBatch`.
- Implements `IDeferableMessageTransport` via `ServiceBusSender.ScheduleMessageAsync`.

---

## 4. AWS SQS Transport (`EricksonLopez.Messaging.AwsSqs`)

Amazon SQS integration supporting Standard queues, FIFO ordering, long polling, and LocalStack test endpoints:

```csharp
builder.Services.AddAwsSqsMessagingTransport(options =>
{
    options.Region = "us-east-1";
    options.WaitTimeSeconds = 20; // Long polling
    options.MaxNumberOfMessages = 10;
    // options.ServiceUrl = "http://localhost:4566"; // For LocalStack integration testing
});
```
