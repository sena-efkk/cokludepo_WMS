using Wms.Integration.Contracts;

namespace Wms.Integration.Messaging;

public sealed record IntegrationEventEnvelope(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTime OccurredAt,
    Guid? CorrelationId,
    string Payload)
{
    public static IntegrationEventEnvelope From(Outbox.OutboxMessage message) =>
        new(
            message.EventId,
            message.EventType,
            message.EventVersion,
            message.OccurredAt,
            message.CorrelationId,
            message.Payload);
}
