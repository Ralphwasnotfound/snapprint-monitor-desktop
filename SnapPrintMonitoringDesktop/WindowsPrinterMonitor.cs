using System.Runtime.InteropServices;

namespace SnapPrintMonitoringDesktop;



internal static class WindowsPrinterMonitor
{


    internal static IReadOnlyList<PrinterReading> Read()
    {
        try { return PrinterSignals.Enrich(ReadWmi()); }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            return PrinterSignals.Enrich(ReadSpooler(ex.GetType().Name));
        }
    }

    private static IReadOnlyList<PrinterReading> ReadWmi()
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
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    bool? offline = Property(item, "WorkOffline") is null ? null : Convert.ToBoolean(Property(item, "WorkOffline"));
                    int status = Number(Property(item, "PrinterStatus"));
                    int extended = Number(Property(item, "ExtendedPrinterStatus"));
                    int error = Number(Property(item, "DetectedErrorState"));
                    int extendedError = Number(Property(item, "ExtendedDetectedErrorState"));
                    string state = Map(offline, status, extended, error, extendedError);
                    found[name] = new(name, state,
                        $"Port={Property(item, "PortName")}; WorkOffline={offline?.ToString() ?? "unknown"}; Status={status}; Extended={extended}; Error={error}/{extendedError}")
                    {
                        RawSignals = new Dictionary<string, string>
                        {
                            ["Win32_Printer.Raw"] = $"WorkOffline={offline}; PrinterStatus={status}; ExtendedPrinterStatus={extended}; DetectedErrorState={error}; ExtendedDetectedErrorState={extendedError}",
                            ["Win32_Printer.QueueClassification"] = state,
                            ["Win32_Printer.WorkOffline"] = offline?.ToString() ?? "unknown",
                            ["Win32_Printer.PrinterStatus"] = status.ToString(),
                            ["Win32_Printer.ExtendedPrinterStatus"] = extended.ToString(),
                            ["Win32_Printer.DetectedErrorState"] = error.ToString(),
                            ["Win32_Printer.ExtendedDetectedErrorState"] = extendedError.ToString()
                        }
                    };
                }
                finally { Release(item); }
            }
            return found.Values.OrderBy(r => r.Queue, StringComparer.OrdinalIgnoreCase).ToArray();
        }
        finally
        {
            Release(results);
            Release(services);
            Release(locator);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo4
    {
        public IntPtr Name;
        public IntPtr ServerName;
        public uint Attributes;
    }
    [DllImport("winspool.drv", EntryPoint = "EnumPrintersW", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumPrinters(uint flags, string? name, uint level, IntPtr buffer,
        uint size, out uint needed, out uint returned);

    private static IReadOnlyList<PrinterReading> ReadSpooler(string wmiError)
    {
        const uint flags = 2 | 4; // PRINTER_ENUM_LOCAL | PRINTER_ENUM_CONNECTIONS
        var first = EnumPrinters(flags, null, 4, IntPtr.Zero, 0, out var needed, out _);
        var error = Marshal.GetLastWin32Error();
        if (!first && error != 122) throw new System.ComponentModel.Win32Exception(error);
        if (needed == 0) return Array.Empty<PrinterReading>();
        // Queue changes between calls can increase the required buffer; retry without unbounded allocation.
        for (var attempt = 0; attempt < 3; attempt++)
        {
            if (needed > 16 * 1024 * 1024) throw new InvalidOperationException("Spooler discovery buffer limit exceeded.");
            var size = needed;
            var buffer = Marshal.AllocHGlobal(checked((int)size));
            try
            {
                if (!EnumPrinters(flags, null, 4, buffer, size, out needed, out var count))
                {
                    error = Marshal.GetLastWin32Error();
                    if (error == 122) continue;
                    throw new System.ComponentModel.Win32Exception(error);
                }
                var readings = new List<PrinterReading>();
                var stride = Marshal.SizeOf<PrinterInfo4>();
                for (var i = 0; i < count; i++)
                {
                    var info = Marshal.PtrToStructure<PrinterInfo4>(IntPtr.Add(buffer, checked((int)i * stride)));
                    var name = Marshal.PtrToStringUni(info.Name);
                    if (!string.IsNullOrWhiteSpace(name)) readings.Add(new(name, "UNKNOWN",
                        $"Installed Windows spooler queue. WMI status unavailable ({wmiError}); physical readiness unknown.")
                    { RawSignals = new Dictionary<string, string> { ["Win32_Printer.QueryError"] = wmiError } });
                }
                return readings.OrderBy(r => r.Queue, StringComparer.OrdinalIgnoreCase).ToArray();
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new InvalidOperationException("Printer queues changed repeatedly during discovery.");
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
        if (offline == true || status == 7 || extended == 7)
            return "OFFLINE";
        // WMI: 0 unknown, 1 other, 2 no error; 3..14 reported faults/warnings; 9 service requested.
        if (error is >= 4 and <= 11 and not 5 || extendedError is >= 4 and <= 14 and not 5)
            return "ERROR";
        if (extended == 9) return "ERROR";
        if (error is 3 or 5 || extendedError is 3 or 5) return "WARNING";
        if (extended is 5 or 6 or 8 or 10 or 11 or 12 or 13 or 14 or 15 or 16 or 17 or 18 || status is 5 or 6) return "NOT READY";
        if (status == 4 || extended == 4) return "PRINTING";
        // Idle/no-error reports can be cached driver defaults, not physical readiness.
        return "UNKNOWN";
    }

    private static void Release(object? value)
    {
        if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
    }
}
