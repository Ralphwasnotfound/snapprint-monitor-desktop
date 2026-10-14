namespace SnapPrintMonitoringDesktop;

internal sealed record RequestedPrintSettings(ushort PublicSize, ushort DriverExtraBytes, uint Fields,
    short? PaperSizeCode, short? PaperWidthTenthsMm, short? PaperLengthTenthsMm, short? ScalePercent, uint? NupHandlingRaw)
{
    public string Paper => PaperSizeCode is { } code ? $"{(Enum.IsDefined(typeof(System.Drawing.Printing.PaperKind), (int)code) ? ((System.Drawing.Printing.PaperKind)code).ToString() : "Unknown size")} (DMPAPER {code})" : PaperWidthTenthsMm is { } w && PaperLengthTenthsMm is { } h ? $"{w/10m} × {h/10m} mm requested" : "Unknown";
    public string NupHandling => NupHandlingRaw switch { 1=>"Print system handles N-up (count unknown)",2=>"Print system does not handle N-up (application may)",null=>"Unknown",_=>$"Unknown raw mode {NupHandlingRaw}" };
    public int? PagesPerSheet => null;
    public string Diagnostic => $"DEVMODEW dmSize={PublicSize}; dmDriverExtra={DriverExtraBytes}; dmFields=0x{Fields:X8}; paper={Paper}; width10mm={PaperWidthTenthsMm}; length10mm={PaperLengthTenthsMm}; scale={ScalePercent}; dmNup={NupHandlingRaw}; {NupHandling}. Requested settings, not verified output; private driver data not parsed.";
}
