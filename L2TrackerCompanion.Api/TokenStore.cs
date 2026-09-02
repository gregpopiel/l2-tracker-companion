using System.Security.Cryptography;
using System.Text;

namespace L2TrackerCompanion.Api;

/// <summary>
/// DPAPI-encrypted JWT on disk (plan step 17). Current-user scope — never
/// stored as plain text. A failed validation must <see cref="Clear"/>.
/// </summary>
public sealed class TokenStore
{
    public const string AppDataFolderName = "L2TrackerCompanion";
    public const string FileName = "auth.bin";
    public const string BaseUrlFileName = "api-base-url.txt";
    public const string DefaultBaseUrl = "https://l2tracker.cc";

    private static readonly byte[] Entropy = "L2TrackerCompanion.Auth.v1"u8.ToArray();

    private readonly string _directory;

    public TokenStore(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
        Directory.CreateDirectory(_directory);
    }

    public static TokenStore GetDefault()
    {
        var directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            AppDataFolderName);
        return new TokenStore(directory);
    }

    public string TokenPath => Path.Combine(_directory, FileName);

    public string BaseUrlPath => Path.Combine(_directory, BaseUrlFileName);

    public bool HasToken => File.Exists(TokenPath);

    public void SaveToken(string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        var plain = Encoding.UTF8.GetBytes(token.Trim());
        var protectedBytes = ProtectedData.Protect(plain, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(TokenPath, protectedBytes);
    }

    public string? TryLoadToken()
    {
        if (!File.Exists(TokenPath))
        {
            return null;
        }

        try
        {
            var protectedBytes = File.ReadAllBytes(TokenPath);
            var plain = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            var token = Encoding.UTF8.GetString(plain).Trim();
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch (CryptographicException)
        {
            ClearToken();
            return null;
        }
    }

    public void ClearToken()
    {
        if (File.Exists(TokenPath))
        {
            File.Delete(TokenPath);
        }
    }

    public string LoadBaseUrl()
    {
        if (!File.Exists(BaseUrlPath))
        {
            return DefaultBaseUrl;
        }

        var value = File.ReadAllText(BaseUrlPath).Trim();
        return string.IsNullOrWhiteSpace(value) ? DefaultBaseUrl : NormalizeBaseUrl(value);
    }

    public void SaveBaseUrl(string baseUrl)
    {
        File.WriteAllText(BaseUrlPath, NormalizeBaseUrl(baseUrl));
    }

    public static string NormalizeBaseUrl(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        return baseUrl.Trim().TrimEnd('/');
    }

    public static bool IsDefaultBaseUrl(string? baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return true;
        }

        return string.Equals(NormalizeBaseUrl(baseUrl), DefaultBaseUrl, StringComparison.OrdinalIgnoreCase);
    }
}
