namespace LlmUsageMonitor.Core.Abstractions;

/// <summary>Symmetric encryption of secrets at rest (AES-256-GCM under the hood).</summary>
public interface ISecretProtector
{
    /// <summary>Encrypts a cleartext string and returns a self-describing token.</summary>
    string Protect(string plaintext);

    /// <summary>Decrypts a token produced by <see cref="Protect"/>. Returns empty on failure.</summary>
    string Unprotect(string token);

    bool IsProtected(string value);
}
