using System.Runtime.InteropServices;

namespace SnapPrintMonitoringDesktop;

internal sealed record PrinterReading(string Queue, string State, string Detail);

internal static class WindowsPrinterMonitor
{
    internal static readonly string[] QueueNames = ["Canon MG3000 series", "EPSON L3210 Series"];

    internal static IReadOnlyList<PrinterReading> Read()
    {
        // WMI COM is built into Windows; no extra package or PowerShell process is needed.
        object? locator = null;
        object? services = null;
        object? results = null;
        var found = new Dictionary<string, PrinterReading>(StringComparer.OrdinalIgnoreCase);
        try
        {
            locator = Activator.CreateInstance(Type.GetTypeFromProgID("WbemScripting.SWbemLocator")
                ?? throw new InvalidOperationException("Windows WMI is unavailable."));
            services = ((dynamic)locator!).ConnectServer(".", "root\\cimv2");
            results = ((dynamic)services).ExecQuery(
                "SELECT Name, PortName, WorkOffline, PrinterStatus, ExtendedPrinterStatus, DetectedErrorState, ExtendedDetectedErrorState FROM Win32_Printer",
                "WQL", 0);
            foreach (object item in (System.Collections.IEnumerable)results)
            {
                try
                {

                    string name = Convert.ToString(Property(item, "Name")) ?? "";
                    if (!QueueNames.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
                    bool? offline = Property(item, "WorkOffline") is null ? null : Convert.ToBoolean(Property(item, "WorkOffline"));
                    int status = Number(Property(item, "PrinterStatus"));
                    int extended = Number(Property(item, "ExtendedPrinterStatus"));
                    int error = Number(Property(item, "DetectedErrorState"));
                    int extendedError = Number(Property(item, "ExtendedDetectedErrorState"));
                    string state = Map(offline, status, extended, error, extendedError);
                    found[name] = new(name, state,
                        $"Port={Property(item, "PortName")}; WorkOffline={offline?.ToString() ?? "unknown"}; Status={status}; Extended={extended}; Error={error}/{extendedError}");
                }
                finally { Release(item); }
            }
            return QueueNames.Select(name => found.TryGetValue(name, out var reading)
                ? reading : new PrinterReading(name, "NOT FOUND", "Expected Windows queue is not installed or was renamed.")).ToArray();
        }
        finally
        {
            Release(results);
            Release(services);
            Release(locator);
        }
    }

    private static object? Property(object item, string name)
    {
        object? properties = null;
        object? property = null;
        try
        {
            properties = ((dynamic)item).Properties_;
            property = ((dynamic)properties).Item(name, 0);
            return ((dynamic)property).Value;
        }
        finally { Release(property); Release(properties); }
    }

    private static int Number(object? value) => value is null ? -1 : Convert.ToInt32(value);

    internal static string Map(bool? offline, int status, int extended, int error, int extendedError)
    {
        if (offline == true || status == 7 || extended == 7 || error == 9 || extendedError == 9)
            return "OFFLINE";
        // WMI detected-error values 2..8 and 10..11 identify specific faults/warnings.
        if (error is >= 2 and <= 8 or >= 10 and <= 11 || extendedError is >= 2 and <= 8 or >= 10 and <= 15)
            return "ERROR";
        if (extended == 9) return "ERROR";
        if (extended is 5 or 6 or 8 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18 || status is 5 or 6) return "NOT READY";
        if (status == 4 || extended == 4) return "PRINTING";
        // Basic PrinterStatus=Idle can be a driver default. Require a more explicit report.
        if (offline == false && (extended == 3 || (status == 3 && error == 1)))
            return "READY (Windows-reported)";
        return "UNKNOWN";
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
}



