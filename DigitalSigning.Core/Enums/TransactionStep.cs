using System;

namespace DigitalSigning.Core.Enums
{
    /// <summary>
    /// Bước xử lý trong quy trình ký số
    /// </summary>
    public enum TransactionStep
    {
        None = 0,
        FilePrepare = 1,
        Hash = 2,
        ProviderRequest = 3,
        WaitingUserConfirm = 4,
        ProviderSigning = 5,
        AppendSignature = 6,
        Upload = 7,
        WebhookNotify = 8,
        Completed = 9,
        Failed = 10
    }
}