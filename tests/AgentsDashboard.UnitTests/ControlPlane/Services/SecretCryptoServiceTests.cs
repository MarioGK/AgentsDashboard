using AgentsDashboard.ControlPlane.Services;
using Microsoft.AspNetCore.DataProtection;

namespace AgentsDashboard.UnitTests.ControlPlane.Services;

public class SecretCryptoServiceTests
{
    private readonly SecretCryptoService _service;
    private readonly IDataProtector _protector;

    public SecretCryptoServiceTests()
    {
        var provider = new EphemeralDataProtectionProvider();
        _protector = provider.CreateProtector("AgentsDashboard.ProviderSecrets.v1");
        _service = new SecretCryptoService(provider);
    }

    [Test]
    public void Encrypt_WithPlaintext_ReturnsNonEmptyString()
    {
        var plaintext = "my-secret-value";

        var encrypted = _service.Encrypt(plaintext);

        encrypted.Should().NotBeEmpty();
        encrypted.Should().NotBe(plaintext);
    }

    [Test]
    public void Decrypt_WithEncryptedValue_ReturnsOriginalPlaintext()
    {
        var plaintext = "my-secret-value";

        var encrypted = _service.Encrypt(plaintext);
        var decrypted = _service.Decrypt(encrypted);

        decrypted.Should().Be(plaintext);
    }

    [Test]
    [Arguments("")]
    [Arguments("a")]
    [Arguments("api-key-12345")]
    [Arguments("This is a much longer secret with special chars: !@#$%^&*()")]
    public void EncryptDecrypt_Roundtrip_PreservesOriginal(string plaintext)
    {
        var encrypted = _service.Encrypt(plaintext);
        var decrypted = _service.Decrypt(encrypted);

        decrypted.Should().Be(plaintext);
    }

    [Test]
    public void Encrypt_DifferentValues_ReturnDifferentCiphertexts()
    {
        var value1 = "secret-one";
        var value2 = "secret-two";

        var encrypted1 = _service.Encrypt(value1);
        var encrypted2 = _service.Encrypt(value2);

        encrypted1.Should().NotBe(encrypted2);
    }

    [Test]
    public void Encrypt_SameValueTwice_ReturnsDifferentCiphertexts()
    {
        var plaintext = "my-secret-value";

        var encrypted1 = _service.Encrypt(plaintext);
        var encrypted2 = _service.Encrypt(plaintext);

        encrypted1.Should().NotBe(encrypted2);
    }

    [Test]
    public void FixedTimeEquals_SameStrings_ReturnsTrue()
    {
        var left = "token-12345";
        var right = "token-12345";

        var result = SecretCryptoService.FixedTimeEquals(left, right);

        result.Should().BeTrue();
    }

    [Test]
    public void FixedTimeEquals_DifferentStrings_ReturnsFalse()
    {
        var left = "token-12345";
        var right = "token-67890";

        var result = SecretCryptoService.FixedTimeEquals(left, right);

        result.Should().BeFalse();
    }

    [Test]
    [Arguments("", "")]
    [Arguments("a", "a")]
    [Arguments("same-value", "same-value")]
    public void FixedTimeEquals_IdenticalStrings_ReturnsTrue(string left, string right)
    {
        var result = SecretCryptoService.FixedTimeEquals(left, right);

        result.Should().BeTrue();
    }

    [Test]
    [Arguments("short", "much-longer-value")]
    [Arguments("prefix-value", "prefix-other")]
    [Arguments("abc", "abd")]
    public void FixedTimeEquals_DifferentValues_ReturnsFalse(string left, string right)
    {
        var result = SecretCryptoService.FixedTimeEquals(left, right);

        result.Should().BeFalse();
    }

    [Test]
    public void FixedTimeEquals_DifferentLengths_ReturnsFalse()
    {
        var left = "short";
        var right = "short-extra";

        var result = SecretCryptoService.FixedTimeEquals(left, right);

        result.Should().BeFalse();
    }

    [Test]
    public void FixedTimeEquals_EmptyStrings_ReturnsTrue()
    {
        var result = SecretCryptoService.FixedTimeEquals("", "");

        result.Should().BeTrue();
    }
}
