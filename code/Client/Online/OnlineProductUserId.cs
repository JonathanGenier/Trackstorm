using System.Security.Cryptography;
using System.Text;

namespace Trackstorm.Client.Online;

/// <summary>An EOS identity, deliberately without numeric or session-player conversion operators.</summary>
internal sealed class OnlineProductUserId : IEquatable<OnlineProductUserId>
{
    private readonly string _value;


    /// <summary>Constructs a distinct online identity from a validated SDK result.</summary>
    /// <param name="value">Canonical Product User ID returned by EOS.</param>
    public OnlineProductUserId(string value)
    {
        if (value is null || value.Length != 32 || !value.All(char.IsAsciiHexDigit) || value.All(c => c == '0'))
        {
            throw new ArgumentException("EOS returned an invalid Product User ID.", nameof(value));
        }

        _value = value.ToLowerInvariant();
    }

    /// <summary>Canonical identity for Client transport routing only; never use in diagnostics.</summary>
    internal string Value => _value;

    /// <inheritdoc />
    public bool Equals(OnlineProductUserId? other) => other is not null && _value == other._value;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is OnlineProductUserId other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(_value);

    /// <inheritdoc />
    public override string ToString() => "puid#" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(_value)))[..12];
}
