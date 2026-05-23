using System.Security.Cryptography;
using System.Text;

namespace WebDispositivosMqtt.Services.Provisioning;

public class DeviceProvisioningService : IDeviceProvisioningService
{
    private readonly byte[] _key;

    public DeviceProvisioningService(IConfiguration configuration)
    {
        var keyBase64 = configuration["Provisioning:EncryptionKey"]
            ?? throw new InvalidOperationException("Falta configuración: Provisioning:EncryptionKey");

        _key = Convert.FromBase64String(keyBase64);

        if (_key.Length != 32)
            throw new InvalidOperationException("Provisioning:EncryptionKey debe ser exactamente 32 bytes en base64.");
    }

    public string GenerateCredential(out string plainPassword)
    {
        plainPassword = GeneratePassword();
        return Encrypt(plainPassword);
    }

    public string GetPlainPassword(string protectedCredential)
    {
        return Decrypt(protectedCredential);
    }

    private string Encrypt(string plainText)
    {
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var cipher = encryptor.TransformFinalBlock(
            Encoding.UTF8.GetBytes(plainText), 0,
            Encoding.UTF8.GetByteCount(plainText));

        // IV (16 bytes) + ciphertext concatenados, luego base64
        var result = new byte[aes.IV.Length + cipher.Length];
        aes.IV.CopyTo(result, 0);
        cipher.CopyTo(result, aes.IV.Length);
        return Convert.ToBase64String(result);
    }

    private string Decrypt(string cipherBase64)
    {
        var data = Convert.FromBase64String(cipherBase64);
        using var aes = Aes.Create();
        aes.Key = _key;
        aes.IV = data[..16];

        using var decryptor = aes.CreateDecryptor();
        var plain = decryptor.TransformFinalBlock(data, 16, data.Length - 16);
        return Encoding.UTF8.GetString(plain);
    }

    private static string GeneratePassword()
    {
        const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
    }
}
