namespace CenteralES.Admin.Bootstrap.WinForms;

internal sealed record MvpServiceDescriptor(
    string Capability,
    string ProcessorKey,
    string Recognizer,
    int EndpointCount,
    string ContractVersion)
{
    public override string ToString()
    {
        return $"{Capability} / {ProcessorKey}";
    }
}

internal sealed record MvpServiceTestResult(
    string Step,
    string Status,
    string Message)
{
    public override string ToString()
    {
        return $"{Status}: {Step} - {Message}";
    }
}
