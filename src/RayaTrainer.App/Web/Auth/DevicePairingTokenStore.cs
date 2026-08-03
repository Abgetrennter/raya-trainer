using System.Security.Cryptography;

namespace RayaTrainer.App.Web.Auth;

public sealed class DevicePairingTokenStore
{
    private readonly HashSet<string> _tokens = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public string IssueToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        lock (_gate)
        {
            _tokens.Add(token);
        }

        return token;
    }

    public bool ValidateBearer(string? authorizationHeader)
    {
        const string prefix = "Bearer ";
        return authorizationHeader is not null &&
            authorizationHeader.StartsWith(prefix, StringComparison.Ordinal) &&
            ValidateToken(authorizationHeader[prefix.Length..].Trim());
    }

    public bool ValidateToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        lock (_gate)
        {
            return _tokens.Contains(token);
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _tokens.Clear();
        }
    }
}
