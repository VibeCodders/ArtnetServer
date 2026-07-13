using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ArtnetNode.Core
{
    public static class ApiKeyAuthHandler
    {
        public static bool ValidateToken(string? providedToken, string expectedToken)
        {
            if (string.IsNullOrEmpty(expectedToken))
                return true;

            if (string.IsNullOrEmpty(providedToken))
                return false;

            if (providedToken.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                providedToken = providedToken.Substring(7);

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(providedToken),
                Encoding.UTF8.GetBytes(expectedToken));
        }
    }
}
