namespace VpsReady.Application;

public sealed class AppViewModel
{
    private readonly string productTitle = "VPSReady";
    private readonly string disconnectedStatus = "No server is connected.";

    public string Title => productTitle;
    public string Status => disconnectedStatus;
}
