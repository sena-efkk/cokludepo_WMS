using System.Text.Json;

namespace Wms.Integration.Outbox;

public sealed class OutboxMessage
{
    private OutboxMessage()
    {
        EventType = string.Empty;
        Payload = string.Empty;
    }

    private OutboxMessage(
        Guid eventId,
        string eventType,
        int eventVersion,
        string payload,
        DateTime occurredAt,
        Guid? correlationId,
        DateTime createdAt)
    {
        Id = Guid.NewGuid();
        EventId = eventId;
        EventType = eventType;
        EventVersion = eventVersion;
        Payload = payload;
        OccurredAt = occurredAt;
        CorrelationId = correlationId;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid EventId { get; private set; }

    public string EventType { get; private set; }

    public int EventVersion { get; private set; }

    public string Payload { get; private set; }

    public DateTime OccurredAt { get; private set; }

    public Guid? CorrelationId { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime? PublishedAt { get; private set; }

    public int AttemptCount { get; private set; }

    public string? LastError { get; private set; }

    public DateTime? NextAttemptAt { get; private set; }

    public static OutboxMessage Create<TEvent>(
        Guid eventId,
        string eventType,
        int eventVersion,
        TEvent payload,
        DateTime occurredAt,
        Guid? correlationId = null,
        DateTime? createdAt = null)
        where TEvent : notnull
    {
        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Outbox message bir EventId taşımalıdır.", nameof(eventId));
        }

        if (string.IsNullOrWhiteSpace(eventType))
        {
            throw new ArgumentException("Outbox message bir EventType taşımalıdır.", nameof(eventType));
        }

        var json = JsonSerializer.Serialize(payload);
        return new OutboxMessage(
            eventId,
            eventType,
            eventVersion,
            json,
            occurredAt,
            correlationId,
            createdAt ?? DateTime.UtcNow);
    }

    public void MarkPublished(DateTime at)
    {
        PublishedAt = at;
        AttemptCount += 1;
        LastError = null;
        NextAttemptAt = null;
    }

    public void MarkFailed(string error, DateTime nextAttemptAt)
    {
        AttemptCount += 1;
        LastError = string.IsNullOrWhiteSpace(error) ? "publish failed" : error.Trim();
        NextAttemptAt = nextAttemptAt;
    }
}
