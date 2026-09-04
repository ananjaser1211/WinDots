using System.Runtime.InteropServices;
using Windows.Security.Credentials;
using WinDots.Core.Contracts;

namespace WinDots.Windows.Security;

/// <summary>
/// An <see cref="ISecretStore"/> backed by the Windows Credential Manager via <see cref="PasswordVault"/>. Secrets are
/// stored per resource (<c>WinDots</c>) and per user name (the caller's key), never in settings files or on disk in the
/// package. All access is best-effort: a missing credential or vault error yields null rather than throwing. See
/// AGENTS.md ("Store secrets with Windows Credential Manager") and _docs/privacy.md.
/// </summary>
public sealed class CredentialManagerSecretStore : ISecretStore
{
    /// <summary>The Credential Manager resource all WinDots secrets share.</summary>
    public const string Resource = "WinDots";

    public Task<string?> GetAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        try
        {
            var vault = new PasswordVault();
            PasswordCredential credential = vault.Retrieve(Resource, key);
            credential.RetrievePassword();
            return Task.FromResult<string?>(credential.Password);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            // No such credential, or the vault is unavailable.
            return Task.FromResult<string?>(null);
        }
    }

    public Task SetAsync(string key, string value, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        ArgumentNullException.ThrowIfNull(value);
        var vault = new PasswordVault();
        RemoveExisting(vault, key);
        vault.Add(new PasswordCredential(Resource, key, value));
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrEmpty(key);
        try
        {
            var vault = new PasswordVault();
            RemoveExisting(vault, key);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            // Already absent.
        }

        return Task.CompletedTask;
    }

    private static void RemoveExisting(PasswordVault vault, string key)
    {
        try
        {
            PasswordCredential existing = vault.Retrieve(Resource, key);
            vault.Remove(existing);
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            // Nothing to remove.
        }
    }
}
