using VpsReady.Core.Diagnostics;
using VpsReady.Core.Operations;

namespace VpsReady.Core.Local;

/// <summary>Stable public-safe outcomes for local OpenSSH config editing.</summary>
public static class OpenSshConfigEditErrorCatalog
{
    public const string InvalidInput = "OPENSSH_CONFIG_INPUT_INVALID";
    public const string AliasExists = "OPENSSH_CONFIG_ALIAS_EXISTS";
    public const string DuplicateAlias = "OPENSSH_CONFIG_ALIAS_DUPLICATE";
    public const string InvalidConfig = "OPENSSH_CONFIG_INVALID";
    public const string Permission = "OPENSSH_CONFIG_PERMISSION_FAILED";
    public const string LocalIo = "OPENSSH_CONFIG_IO_FAILED";
    public const string Cancelled = "OPENSSH_CONFIG_EDIT_CANCELLED";

    public static IReadOnlyCollection<string> All { get; } =
    [InvalidInput, AliasExists, DuplicateAlias, InvalidConfig, Permission, LocalIo, Cancelled];
}

/// <summary>
/// Desired local alias. Values are intentionally application-only and are
/// never safe to send to Activity, journals, bundles, or issue reports.
/// </summary>
public sealed class OpenSshConfigEditRequest
{
    public OpenSshConfigEditRequest(string alias, string hostName, string user, int port, string identityFile, bool identitiesOnly = true)
    {
        Alias = alias ?? throw new ArgumentNullException(nameof(alias));
        HostName = hostName ?? throw new ArgumentNullException(nameof(hostName));
        User = user ?? throw new ArgumentNullException(nameof(user));
        Port = port;
        IdentityFile = identityFile ?? throw new ArgumentNullException(nameof(identityFile));
        IdentitiesOnly = identitiesOnly;
    }

    public string Alias { get; }
    public string HostName { get; }
    public string User { get; }
    public int Port { get; }
    public string IdentityFile { get; }
    public bool IdentitiesOnly { get; }

    public override string ToString() => "OpenSshConfigEditRequest [values redacted]";
}

public enum OpenSshConfigEditDisposition
{
    Created,
    Unchanged,
}

public sealed class OpenSshConfigEditResult
{
    private OpenSshConfigEditResult(OperationResult operation, OpenSshConfigEditDisposition? disposition, string? errorCode)
    {
        Operation = operation;
        Disposition = disposition;
        ErrorCode = errorCode;
    }

    public OperationResult Operation { get; }
    public OpenSshConfigEditDisposition? Disposition { get; }
    public string? ErrorCode { get; }
    public bool Succeeded => Operation.Succeeded && Disposition is not null;

    public static OpenSshConfigEditResult Success(OperationResult operation, OpenSshConfigEditDisposition disposition)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!operation.Succeeded)
        {
            throw new ArgumentException("A successful config edit requires a successful verified operation.", nameof(operation));
        }

        return new OpenSshConfigEditResult(operation, disposition, null);
    }

    public static OpenSshConfigEditResult Failure(OperationResult operation, string errorCode)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!OpenSshConfigEditErrorCatalog.All.Contains(errorCode, StringComparer.Ordinal))
        {
            throw new ArgumentException("An approved OpenSSH config error code is required.", nameof(errorCode));
        }

        return new OpenSshConfigEditResult(operation, null, errorCode);
    }

    public override string ToString() => "OpenSshConfigEditResult [safe summary only]";
}

public interface IOpenSshConfigEditor
{
    Task<OpenSshConfigEditResult> AddAliasAsync(
        OpenSshConfigEditRequest request,
        CorrelationIds correlation,
        CancellationToken cancellationToken);
}
