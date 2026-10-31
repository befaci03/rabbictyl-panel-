using BCrypt.Net;
using Rabbictyl.Core.Abstractions;

namespace Rabbictyl.Infrastructure.Security;

public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private readonly int _workFactor;

    public BcryptPasswordHasher(int workFactor)
    {
        if (workFactor is < 4 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(workFactor), workFactor, "Work factor must be between 4 and 31.");
        }

        _workFactor = workFactor;
    }

    public string Hash(string password)
    {
        ArgumentException.ThrowIfNullOrEmpty(password);

        return BCrypt.Net.BCrypt.HashPassword(password, _workFactor);
    }

    public bool Verify(string password, string passwordHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash))
        {
            return false;
        }

        // Laravel/PHP emits "$2y$" hashes; BCrypt.Net understands "$2a$"/"$2b$".
        var normalized = passwordHash.StartsWith("$2y$", StringComparison.Ordinal)
            ? "$2a$" + passwordHash[4..]
            : passwordHash;

        try
        {
            return BCrypt.Net.BCrypt.Verify(password, normalized);
        }
        catch (SaltParseException)
        {
            return false;
        }
    }
}
