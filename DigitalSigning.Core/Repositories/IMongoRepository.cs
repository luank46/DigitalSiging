using System.Collections.Generic;
using System.Threading.Tasks;
using DigitalSigning.Core.Models;

namespace DigitalSigning.Core.Repositories
{
    /// <summary>
    /// Generic MongoDB repository interface
    /// </summary>
    public interface IMongoRepository<T> where T : class
    {
        Task<T?> GetByIdAsync(string id);
        Task<IReadOnlyCollection<T>> GetAllAsync();
        Task InsertAsync(T entity);
        Task UpdateAsync(T entity);
        Task DeleteAsync(string id);
    }
}