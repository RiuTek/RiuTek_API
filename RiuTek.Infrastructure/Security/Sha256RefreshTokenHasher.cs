using System.Security.Cryptography;
using System.Text;
using RiuTek.Application.Common.Interfaces;

namespace RiuTek.Infrastructure.Security;

public class Sha256RefreshTokenHasher : IRefreshTokenHasher
{
    public string HashToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        var bytes = Encoding.UTF8.GetBytes(token);
        var hashBytes = SHA256.HashData(bytes);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
