using System.Runtime.InteropServices;
using SnapPrintMonitoringDesktop;
internal static class PrintSettingsChecks
{
    internal static void Run()
    {
        int checks=0;void Check(bool value,string message) { if(!value) throw new Exception(message);checks++; }
        var buffer=Marshal.AllocHGlobal(220);
        try
        {
            Marshal.Copy(new byte[220],0,buffer,220);Marshal.WriteInt16(buffer,68,220);Marshal.WriteInt16(buffer,70,40);
            Marshal.WriteInt32(buffer,72,0x111E|0x40);Marshal.WriteInt16(buffer,78,9);Marshal.WriteInt16(buffer,80,2970);Marshal.WriteInt16(buffer,82,2100);Marshal.WriteInt16(buffer,84,100);Marshal.WriteInt16(buffer,86,3);Marshal.WriteInt16(buffer,94,2);Marshal.WriteInt32(buffer,180,1);
            var settings=PrinterSignals.ReadRequestedSettings(buffer,buffer,220)!;
            Check(settings.PaperSizeCode==9 && settings.PaperWidthTenthsMm==2100 && settings.PaperLengthTenthsMm==2970,"Declared requested paper size and dimensions");
            Check(settings.PublicSize==220 && settings.DriverExtraBytes==40 && settings.ScalePercent==100,"Public header and scale, not driver private bytes");
            Check(settings.NupHandlingRaw==1 && settings.PagesPerSheet is null,"System N-up handler does not invent pages per sheet");
            Marshal.WriteInt32(buffer,180,2);
            Check(PrinterSignals.ReadRequestedSettings(buffer,buffer,220)!.NupHandling.Contains("application may"),"Oneup is not proof of no application N-up");
            var layout=PrinterSignals.ReadRequestedLayout(buffer,buffer,220);
            Check(layout.Copies==3 && layout.Duplex=="LONG_EDGE_DUPLEX_REQUESTED","Multiple copies and duplex stay requested");
            Marshal.WriteInt16(buffer,86,1);Marshal.WriteInt16(buffer,94,1);
            Check(PrinterSignals.ReadRequestedLayout(buffer,buffer,220)==(1,"SIMPLEX_REQUESTED"),"One copy simplex metadata");
            Marshal.WriteInt32(buffer,72,0);
            var missing=PrinterSignals.ReadRequestedSettings(buffer,buffer,220)!;
            Check(missing.Paper=="Unknown" && missing.NupHandling=="Unknown" && missing.ScalePercent is null,"Absent dmFields yield unknown");
            Check(PrinterSignals.ReadRequestedSettings(buffer,buffer,70) is null,"Truncated buffer rejected");
            Marshal.WriteInt16(buffer,68,76);Marshal.WriteInt32(buffer,72,0x40);
            Check(PrinterSignals.ReadRequestedSettings(buffer,buffer,220)!.NupHandlingRaw is null,"Short public layout cannot expose N-up field");
            Check(PrinterSignals.ReadRequestedSettings(IntPtr.Add(buffer,221),buffer,220) is null,"Out of bounds pointer ignored");
            var inconsistent=new JobReading(1,128,1,5) { RequestedSettings=settings };
            Check(inconsistent.DocumentPages is null && inconsistent.TotalPages==1 && inconsistent.PagesPrinted==5,"5/1 counters preserved without document or sheet inference");
        }
        finally { Marshal.FreeHGlobal(buffer); }
        Console.WriteLine($"PASS: {checks} supported print-settings checks.");
    }
}
