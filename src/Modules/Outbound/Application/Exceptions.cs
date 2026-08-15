namespace Wms.Modules.Outbound.Application;

public class OutboundNotFoundException : Exception
{
    public OutboundNotFoundException(string message)
        : base(message)
    {
    }
}

public sealed class OrderNotFoundException : OutboundNotFoundException
{
    public OrderNotFoundException(Guid orderId)
        : base($"Order bulunamadı: {orderId}")
    {
    }
}

public sealed class PickTaskNotFoundException : OutboundNotFoundException
{
    public PickTaskNotFoundException(Guid taskId)
        : base($"Pick task bulunamadı: {taskId}")
    {
    }
}

public sealed class InvalidOrderStateException : Exception
{
    public InvalidOrderStateException(string message)
        : base(message)
    {
    }
}

public sealed class InvalidPickTaskStateException : Exception
{
    public InvalidPickTaskStateException(string message)
        : base(message)
    {
    }
}

public sealed class PickLocationMismatchException : Exception
{
    public PickLocationMismatchException(Guid taskLocationId, string? scanned)
        : base($"Location scan pick task'ın location'ıyla eşleşmiyor. Task location: {taskLocationId}, scanned: {scanned ?? "(çözülemedi)"}")
    {
    }
}

public sealed class PickSkuMismatchException : Exception
{
    public PickSkuMismatchException(Guid taskSkuId, string? scanned)
        : base($"SKU scan pick task'ın SKU'suna çözülemedi. Task sku: {taskSkuId}, scanned: {scanned ?? "(çözülemedi)"}")
    {
    }
}

public sealed class PickQuantityExceededException : Exception
{
    public PickQuantityExceededException(int required, int picked, int attempted)
        : base($"Pick quantity reservation'ı aşıyor: required {required}, picked {picked}, attempt {attempted}.")
    {
    }
}

public sealed class DuplicateOrderNumberException : Exception
{
    public DuplicateOrderNumberException(string orderNumber)
        : base($"Order number zaten kullanımda: {orderNumber}")
    {
    }
}
