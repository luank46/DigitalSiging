using DigitalSigning.Core.Models.Legacy;

namespace DigitalSigning.Core.Services
{
    /// <summary>
    /// Account authentication service.
    /// Mapping từ IAccountService cũ.
    /// </summary>
    public interface IAccountService
    {
        /// <summary>Authenticate user by email and password.</summary>
        User? Authenticate(string email, string password);
    }
}
