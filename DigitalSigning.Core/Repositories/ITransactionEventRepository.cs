using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DigitalSigning.Core.Enums;
using DigitalSigning.Core.Models;

namespace DigitalSigning.Core.Repositories
{
    /// <summary>
    /// Repository cho các sự kiện giao dịch
    /// </summary>
    public interface ITransactionEventRepository : IMongoRepository<TransactionEvent>
    {
        Task<IReadOnlyCollection<TransactionEvent>> GetByMaGiaoDichAsync(string maGiaoDich);
    }
}