using System.Security.Cryptography;

namespace Trackstorm.Client.Online;

/// <summary>Session-scoped salted verifier. Never retains or formats the raw credential.</summary>
internal sealed class LobbyCredential
{
    private readonly byte[] _salt;
    private readonly byte[] _hash;

    private LobbyCredential(byte[] salt, byte[] hash)
    {
        _salt = salt;
        _hash = hash;
    }

    /// <inheritdoc />
    public override string ToString() => "Locked access verifier [redacted]";

    /// <summary>Derives a session-scoped verifier using a fresh random salt.</summary>
    /// <param name="credential">Transient access code; never retained or logged.</param>
    /// <returns>The validated result, or an explicit failure/absence.</returns>
    internal static LobbyCredential Create(string credential)
    {
        if (credential.Length is < 4 or > 64)
        {
            throw new ArgumentException("Access code must contain 4–64 characters.");
        }

        byte[] salt = RandomNumberGenerator.GetBytes(16);
        return new LobbyCredential(salt, Derive(credential, salt));
    }

    /// <summary>Reads a bounded salted verifier; malformed data is rejected.</summary>
    /// <param name="value">Untrusted text or serialized verifier.</param>
    /// <returns>The validated result, or an explicit failure/absence.</returns>
    internal static LobbyCredential? Parse(string value)
    {
        string[] parts = value.Split(':');
        if (parts.Length != 2 || parts[0].Length != 32 || parts[1].Length != 64)
        {
            return null;
        }

        try
        {
            return new LobbyCredential(Convert.FromHexString(parts[0]), Convert.FromHexString(parts[1]));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>Exports salted verification metadata for lightweight lobby admission.</summary>
    /// <returns>The validated result, or an explicit failure/absence.</returns>
    internal string ExportVerifier() => $"{Convert.ToHexString(_salt)}:{Convert.ToHexString(_hash)}";

    /// <summary>Compares a supplied code without retaining it or formatting it in diagnostics.</summary>
    /// <param name="credential">Transient access code; never retained or logged.</param>
    /// <returns>The validated result, or an explicit failure/absence.</returns>
    internal bool Verify(string? credential) => credential is { Length: >= 4 and <= 64 } && CryptographicOperations.FixedTimeEquals(_hash, Derive(credential, _salt));


    private static byte[] Derive(string credential, byte[] salt) => Rfc2898DeriveBytes.Pbkdf2(credential, salt, 100_000, HashAlgorithmName.SHA256, 32);
}
