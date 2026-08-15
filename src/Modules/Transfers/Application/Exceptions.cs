namespace Wms.Modules.Transfers.Application;

public class TransferNotFoundException : Exception
{
    public TransferNotFoundException(Guid transferId)
        : base($"Transfer bulunamadı: {transferId}")
    {
    }
}

public sealed class InvalidTransferStateException : Exception
{
    public InvalidTransferStateException(string message)
        : base(message)
    {
    }
}

public sealed class DuplicateTransferNumberException : Exception
{
    public DuplicateTransferNumberException(string transferNumber)
        : base($"Transfer number zaten kullanımda: {transferNumber}")
    {
    }
}

public sealed class OverReceiptRejectedException : Exception
{
    public OverReceiptRejectedException(int shipped, int received, int attempted)
        : base($"Over receipt kabul edilmez: shipped {shipped}, received {received}, attempt {attempted} — explicit discrepancy/reconciliation gerekir.")
    {
    }
}
