using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace AgentsDashboard.ControlPlane.Services;

public class SecretCryptoService(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("AgentsDashboard.ProviderSecrets.v1");

    public string Encrypt(string plaintext)
    {
        return _protector.Protect(plaintext);
    }

    public string Decrypt(string ciphertext)
    {
        return _protector.Unprotect(ciphertext);
    }

    public static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = System.Text.Encoding.UTF8.GetBytes(left);
        var rightBytes = System.Text.Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
