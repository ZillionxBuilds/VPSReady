using VpsReady.Application;
using VpsReady.Core.Local;

namespace VpsReady.Desktop;

/// <summary>
/// Keeps desktop storage-picker failures out of async-void event handlers.
/// Only the chosen local path is passed to the application; picker exception
/// messages and paths never enter presentation or diagnostics.
/// </summary>
internal static class LocalKeyPickerFlow
{
    internal static async Task GenerateAsync(
        SshManagementViewModel ssh,
        string? requestedName,
        Func<Task<string?>> chooseFolder)
    {
        ArgumentNullException.ThrowIfNull(ssh);
        ArgumentNullException.ThrowIfNull(chooseFolder);

        if (!LocalSshKeyNamePolicy.IsValid(requestedName))
        {
            await ssh.GenerateNamedAsync(null, requestedName);
            return;
        }

        string? folder;
        try
        {
            folder = await chooseFolder();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            ssh.ReportLocalFolderPickerFailure();
            return;
        }

        if (!string.IsNullOrWhiteSpace(folder))
        {
            await ssh.GenerateNamedAsync(folder, requestedName);
        }
    }

    internal static async Task SelectAsync(
        SshManagementViewModel ssh,
        Func<Task<string?>> chooseFile)
    {
        ArgumentNullException.ThrowIfNull(ssh);
        ArgumentNullException.ThrowIfNull(chooseFile);

        string? path;
        try
        {
            path = await chooseFile();
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception)
        {
            ssh.ReportLocalFilePickerFailure();
            return;
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            await ssh.SelectAsync(path);
        }
    }
}
