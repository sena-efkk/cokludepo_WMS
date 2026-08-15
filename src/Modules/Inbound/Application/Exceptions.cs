namespace Wms.Modules.Inbound.Application;

public class InboundNotFoundException : Exception
{
    public InboundNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class ReceiptNotFoundException : InboundNotFoundException
{
    public ReceiptNotFoundException(Guid receiptId)
        : base($"Receipt bulunamadı: {receiptId}")
    {
    }
}

public sealed class ReceiptLineNotFoundException : InboundNotFoundException
{
    public ReceiptLineNotFoundException(Guid receiptLineId)
        : base($"Receipt line bulunamadı: {receiptLineId}")
    {
    }
}

public sealed class PutawayTaskNotFoundException : InboundNotFoundException
{
    public PutawayTaskNotFoundException(Guid taskId)
        : base($"Putaway task bulunamadı: {taskId}")
    {
    }
}

public sealed class InvalidReceiptStateException : Exception
{
    public InvalidReceiptStateException(string message)
        : base(message)
    {
    }
}

public sealed class InvalidPutawayTaskStateException : Exception
{
    public InvalidPutawayTaskStateException(string message)
        : base(message)
    {
    }
}

public sealed class OverReceiptNotAllowedException : Exception
{
    public OverReceiptNotAllowedException(int expected, int receivedSoFar, int attempted)
        : base($"Over-receipt policy kapalı: expected {expected}, received {receivedSoFar}, attempt {attempted}.")
    {
    }
}

public sealed class InvalidReceivingLocationException : Exception
{
    public InvalidReceivingLocationException(string message)
        : base(message)
    {
    }
}

public sealed class DuplicateReceiptNumberException : Exception
{
    public DuplicateReceiptNumberException(string receiptNumber)
        : base($"Receipt number zaten kullanımda: {receiptNumber}")
    {
    }
}

public sealed class PutawaySourceMismatchException : Exception
{
    public PutawaySourceMismatchException(string taskSource, string? scanned)
        : base($"Source scan putaway task'ın source location'ıyla eşleşmiyor. Task source: {taskSource}, scanned: {scanned ?? "(çözülemedi)"}")
    {
    }
}

public sealed class PutawaySkuMismatchException : Exception
{
    public PutawaySkuMismatchException(Guid taskSkuId, string? scanned)
        : base($"SKU scan putaway task'ın SKU'suna çözülemedi. Task sku: {taskSkuId}, scanned: {scanned ?? "(çözülemedi)"}")
    {
    }
}

public sealed class PutawayQuantityMismatchException : Exception
{
    public PutawayQuantityMismatchException(int taskQuantity, int requested)
        : base($"Putaway quantity task ile birebir eşleşmelidir. Task: {taskQuantity}, requested: {requested}")
    {
    }
}

public sealed class PutawayRejectedException : Exception
{
    public PutawayRejectedException(string code, string reason)
        : base(reason)
    {
        Code = code;
    }

    public string Code { get; }
}
