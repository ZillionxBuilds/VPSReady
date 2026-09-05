using System.Globalization;
using VpsReady.Core.Diagnostics;
using VpsReady.Core.Remote;

namespace VpsReady.Infrastructure.Remote;

/// <summary>
/// C303's narrow production command catalog. User input is validated into a
/// typed request before this catalog can construct command metadata or shell
/// text. The safe metadata is not raw shell text and is not sent to diagnostics.
/// </summary>
public static class UbuntuFirewallCommandCatalog
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(15);

    private const string PredictableLocalePrefix = "LC_ALL=C LANG=C; export LC_ALL LANG; ";

    public static RemoteCommand CreateAllowRuleRequest(UfwAllowRuleRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RemoteCommand.Create(
            RemoteCommandCatalog.RequireKnown(RemoteCommandCatalog.UbuntuUfwAllowRuleAdd),
            [
                new("family", request.Family.ToString().ToLowerInvariant()),
                new("port", request.Port.ToString(CultureInfo.InvariantCulture)),
                new("protocol", request.Protocol.ToString().ToLowerInvariant()),
                new("source", request.ToCommandSource()),
            ],
            DefaultTimeout,
            OutputCapturePolicy.MetadataOnly,
            maximumOutputBytes: 0);
    }

    public static RemoteCommand CreateSelectedRuleRemovalRequest(UfwRuleRemovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return RemoteCommand.Create(
            RemoteCommandCatalog.RequireKnown(RemoteCommandCatalog.UbuntuUfwSelectedRuleRemove),
            [
                new("action", request.Action.ToString().ToLowerInvariant()),
                new("family", request.Family.ToString().ToLowerInvariant()),
                new("port", request.Port.ToString(CultureInfo.InvariantCulture)),
                new("protocol", request.Protocol.ToString().ToLowerInvariant()),
                new("source", request.ToCommandSource()),
            ],
            DefaultTimeout,
            OutputCapturePolicy.MetadataOnly,
            maximumOutputBytes: 0);
    }

    /// <summary>
    /// Resolves only the C303 allow-rule command from validated compact metadata.
    /// This is the sole production shell construction path for the card.
    /// </summary>
    public static string RequireShellCommand(RemoteCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (string.Equals(command.Id.Value, RemoteCommandCatalog.UbuntuUfwSelectedRuleRemove, StringComparison.Ordinal))
        {
            return RequireSelectedRuleRemovalShellCommand(command);
        }

        if (!string.Equals(command.Id.Value, RemoteCommandCatalog.UbuntuUfwAllowRuleAdd, StringComparison.Ordinal))
        {
            throw new ArgumentOutOfRangeException(nameof(command), command.Id.Value, "The command is not a known Ubuntu firewall command.");
        }

        if (!TryParseRequest(command.SafeArgumentSummary, out var request))
        {
            throw new ArgumentException("Firewall command metadata is not a validated allow-rule request.", nameof(command));
        }

        var validatedRequest = request!;
        var source = RemoteCommandArguments.QuotePosixArgument(validatedRequest.ToCommandSource());
        var port = RemoteCommandArguments.QuotePosixArgument(validatedRequest.Port.ToString(CultureInfo.InvariantCulture));
        var protocol = RemoteCommandArguments.QuotePosixArgument(validatedRequest.Protocol.ToString().ToLowerInvariant());
        return PredictableLocalePrefix
            + "if ! command -v ufw >/dev/null 2>&1; then exit 127; "
            + "elif [ \"$(id -u)\" -eq 0 ]; then ufw allow from " + source + " to any port " + port + " proto " + protocol + "; "
            + "elif command -v sudo >/dev/null 2>&1 && sudo -n true >/dev/null 2>&1; then sudo -n ufw allow from " + source + " to any port " + port + " proto " + protocol + "; "
            + "else exit 77; fi";
    }

    private static string RequireSelectedRuleRemovalShellCommand(RemoteCommand command)
    {
        if (!TryParseSelectedRuleRemoval(command.SafeArgumentSummary, out var request))
        {
            throw new ArgumentException("Firewall command metadata is not a validated selected-rule removal request.", nameof(command));
        }

        var selectedRule = request!;
        var action = RemoteCommandArguments.QuotePosixArgument(selectedRule.Action.ToString().ToLowerInvariant());
        var source = RemoteCommandArguments.QuotePosixArgument(selectedRule.ToCommandSource());
        var port = RemoteCommandArguments.QuotePosixArgument(selectedRule.Port.ToString(CultureInfo.InvariantCulture));
        var protocol = RemoteCommandArguments.QuotePosixArgument(selectedRule.Protocol.ToString().ToLowerInvariant());
        return PredictableLocalePrefix
            + "if ! command -v ufw >/dev/null 2>&1; then exit 127; "
            + "elif [ \"$(id -u)\" -eq 0 ]; then ufw --force delete " + action + " from " + source + " to any port " + port + " proto " + protocol + "; "
            + "elif command -v sudo >/dev/null 2>&1 && sudo -n true >/dev/null 2>&1; then sudo -n ufw --force delete " + action + " from " + source + " to any port " + port + " proto " + protocol + "; "
            + "else exit 77; fi";
    }

    private static bool TryParseRequest(string safeSummary, out UfwAllowRuleRequest? request)
    {
        request = null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var token in safeSummary.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = token.IndexOf('=');
            if (separator <= 0 || !values.TryAdd(token[..separator], token[(separator + 1)..]))
            {
                return false;
            }
        }

        if (values.Count != 4
            || !values.TryGetValue("protocol", out var protocolText)
            || !values.TryGetValue("port", out var portText)
            || !values.TryGetValue("source", out var source)
            || !values.TryGetValue("family", out var familyText)
            || !Enum.TryParse<UfwRuleProtocol>(protocolText, true, out var protocol)
            || !int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || !Enum.TryParse<UfwIpFamily>(familyText, true, out var family))
        {
            return false;
        }

        return UfwAllowRuleRequest.TryCreate(new UfwAllowRuleInput(protocol, port, source, family), out request, out _);
    }

    private static bool TryParseSelectedRuleRemoval(string safeSummary, out UfwRuleRemovalRequest? request)
    {
        request = null;
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var token in safeSummary.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = token.IndexOf('=');
            if (separator <= 0 || !values.TryAdd(token[..separator], token[(separator + 1)..]))
            {
                return false;
            }
        }

        if (values.Count != 5
            || !values.TryGetValue("action", out var actionText)
            || !values.TryGetValue("family", out var familyText)
            || !values.TryGetValue("port", out var portText)
            || !values.TryGetValue("protocol", out var protocolText)
            || !values.TryGetValue("source", out var source)
            || !Enum.TryParse<UfwRuleAction>(actionText, true, out var action)
            || !Enum.TryParse<UfwIpFamily>(familyText, true, out var family)
            || !Enum.TryParse<UfwRuleProtocol>(protocolText, true, out var protocol)
            || !int.TryParse(portText, NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            || !UfwAllowRuleRequest.TryCreate(new UfwAllowRuleInput(protocol, port, source, family), out var semantic, out _)
            || semantic is null)
        {
            return false;
        }

        // This request is reconstructed only from validated semantic fields;
        // its original opaque identity remains strictly a local selection key.
        var synthetic = new UfwRule(
            UfwRuleIdentity.Create(1, protocol, port, semantic.Source, action, family),
            1,
            protocol,
            port,
            semantic.Source,
            action,
            family);
        return UfwRuleRemovalRequest.TryCreate(synthetic, out request);
    }
}
