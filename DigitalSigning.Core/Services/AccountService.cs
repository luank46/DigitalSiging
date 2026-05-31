using DigitalSigning.Core.Models.Legacy;

namespace DigitalSigning.Core.Services
{
    /// <summary>
    /// Account authentication — giữ nguyên logic từ AccountService cũ.
    /// </summary>
    public class AccountService : IAccountService
    {
        public User? Authenticate(string email, string password)
        {
            if (email == "quangich@quangich.com" && password == "123456")
            {
                return new User { Name = "Quảng ích", Email = "quangich@quangich.com" };
            }
            return null;
        }
    }
}
