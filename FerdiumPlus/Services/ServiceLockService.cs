using System.Security.Cryptography;
using System.Text;
using Windows.Security.Credentials.UI;

namespace FerdiumApp.Services;

/// <summary>
/// Provides salted PBKDF2 hashing for per-service passcodes/passwords and
/// native Windows Hello verification via WinRT UserConsentVerifier.
/// </summary>
public static class ServiceLockService
{
    private const int SaltSizeBytes = 16;
    private const int HashSizeBytes = 32;
    private const int Pbkdf2Iterations = 100_000;

    public static (string Hash, string Salt) HashSecret(string secret)
    {
        var saltBytes = RandomNumberGenerator.GetBytes(SaltSizeBytes);
        var hashBytes = Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(secret),
            saltBytes,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            HashSizeBytes);

        return (Convert.ToBase64String(hashBytes), Convert.ToBase64String(saltBytes));
    }

    public static bool VerifySecret(string? candidate, string? storedHash, string? storedSalt)
    {
        if (string.IsNullOrEmpty(candidate) ||
            string.IsNullOrWhiteSpace(storedHash) ||
            string.IsNullOrWhiteSpace(storedSalt))
        {
            return false;
        }

        try
        {
            var saltBytes = Convert.FromBase64String(storedSalt);
            var expectedHash = Convert.FromBase64String(storedHash);
            var actualHash = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(candidate),
                saltBytes,
                Pbkdf2Iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> IsWindowsHelloAvailableAsync()
    {
        try
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();
            return availability == UserConsentVerifierAvailability.Available;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<(bool Verified, string Message)> VerifyWindowsHelloAsync(string serviceName)
    {
        try
        {
            var availability = await UserConsentVerifier.CheckAvailabilityAsync();
            if (availability != UserConsentVerifierAvailability.Available)
            {
                return (false, $"Windows Hello is not available ({availability}).");
            }

            var result = await UserConsentVerifier.RequestVerificationAsync(
                $"Unlock {serviceName} in Ferdium");

            return result == UserConsentVerificationResult.Verified
                ? (true, string.Empty)
                : (false, "Windows Hello verification was canceled or failed.");
        }
        catch (Exception ex)
        {
            return (false, $"Windows Hello error: {ex.Message}");
        }
    }
}
