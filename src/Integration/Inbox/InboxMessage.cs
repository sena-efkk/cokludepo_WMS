namespace Wms.Integration.Inbox;

public sealed class InboxMessage
{
    private InboxMessage()
    {
        Consumer = string.Empty;
    }

    private InboxMessage(string consumer, Guid eventId, DateTime processedAt)
    {
        Id = Guid.NewGuid();
        Consumer = consumer;
        EventId = eventId;
        ProcessedAt = processedAt;
    }

    public Guid Id { get; private set; }

    public string Consumer { get; private set; }

    public Guid EventId { get; private set; }

    public DateTime ProcessedAt { get; private set; }

    public static InboxMessage Create(string consumer, Guid eventId, DateTime? processedAt = null)
    {
        if (string.IsNullOrWhiteSpace(consumer))
        {
            throw new ArgumentException("Inbox message bir consumer taşımalıdır.", nameof(consumer));
        }

        if (eventId == Guid.Empty)
        {
            throw new ArgumentException("Inbox message bir EventId taşımalıdır.", nameof(eventId));
        }

        return new InboxMessage(consumer.Trim(), eventId, processedAt ?? DateTime.UtcNow);
    }
}
