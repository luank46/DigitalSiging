using System.Threading;
using System.Threading.Tasks;

namespace DigitalSigning.Core.Interfaces
{
    /// <summary>
    /// Calculates hash values for files during the hashing step.
    /// </summary>
    public interface IHashCalculator
    {
        /// <summary>Computes the hash of a file at the given path.</summary>
        Task<string> CalculateHashAsync(string filePath, CancellationToken ct = default);
    }
}
