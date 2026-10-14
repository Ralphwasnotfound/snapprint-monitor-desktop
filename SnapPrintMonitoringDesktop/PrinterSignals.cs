using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using System.Printing;

namespace SnapPrintMonitoringDesktop;

internal sealed record JobReading(uint Id, uint RawStatus, uint TotalPages, uint PagesPrinted)
{
    public DateTimeOffset? SubmittedAt { get; init; }
    public RequestedPrintSettings? RequestedSettings { get; init; }
    public uint? DocumentPages => null;
    public int? CopiesRequested { get; init; }
    public string? DuplexRequested { get; init; }
    public string LayoutEvidence => "Requested driver settings only; selected ranges, N-up and actual physical output are not established.";
    public bool DriverStatusTextPresent { get; init; }
    public string DriverStatusTextPolicy => "Not exported to avoid leaking driver-supplied document/user text; numeric flags are supplemental when text is present.";
    public bool ErrorReported => (RawStatus & (2u | 32u | 64u | 512u)) != 0;
    public bool CompletionReported => (RawStatus & (128u | 4096u)) != 0;
    public string Interpretation => CompletionReported ? "SPOOLER_COMPLETION_REPORTED" : ErrorReported ? "JOB_ERROR_REPORTED" : (RawStatus & 16) != 0 ? "PRINTING_REPORTED" : "QUEUED_OR_UNKNOWN";
}
internal sealed record UsbEvidence(string Mapping, string? DeviceInstanceId, bool? Present, uint? DeviceStatus, uint? ProblemCode, string Detail);
internal sealed record StatusEvidence(string PhysicalStatus, string ConnectionStatus, string QueueStatus, string DecisionSource,
    string Confidence, UsbEvidence Usb, IReadOnlyDictionary<string, string> Signals, IReadOnlyList<JobReading> Jobs,
    string JobQueryStatus, string ErrorAttribution);

internal static class PrinterSignals
{
    internal static StatusEvidence Classify(UsbEvidence usb, IReadOnlyDictionary<string, string> signals,
        IReadOnlyList<JobReading> jobs, string jobQuery, string queueStatus)
    {
        var connection = usb.Mapping != "EXACT_QUEUE_DEVICE_ID" ? "UNKNOWN" : usb.Present switch
        { true => "USB_PRESENT", false => "USB_NOT_PRESENT", _ => "UNKNOWN" };
        var errors = signals.Where(p => p.Key.EndsWith("QueueClassification") && p.Value == "ERROR")
            .Select(p => p.Key).ToList();
        if (jobs.Any(j => j.ErrorReported)) errors.Add("EnumJobs.JOB_INFO_2.Status");
        return new("UNKNOWN", connection, queueStatus,
            "No device-specific physical readiness protocol; queue flags and USB enumeration do not establish power/readiness.",
            "UNVERIFIED_PHYSICAL_STATUS", usb, signals, jobs, jobQuery,
            errors.Count == 0 ? "No error indication observed in this sample; previous successful-print error cannot be attributed without a captured sample."
                : "Windows-reported error signals: " + string.Join(", ", errors) + ". These do not prove physical failure; compare job IDs/timestamps and observed output.");
    }

    internal static IReadOnlyList<PrinterReading> Enrich(IReadOnlyList<PrinterReading> readings)
    {
        var mapped = readings.ToDictionary(r => r.Queue, r => ReadMapping(r.Queue), StringComparer.OrdinalIgnoreCase);
        return readings.Select(r =>
        {
            var signals = new Dictionary<string, string>(r.RawSignals, StringComparer.Ordinal);
            var usb = mapped[r.Queue];
            if (usb.DeviceInstanceId is not null && mapped.Values.Count(v => string.Equals(v.DeviceInstanceId, usb.DeviceInstanceId, StringComparison.OrdinalIgnoreCase)) > 1)
                usb = usb with { Mapping = "AMBIGUOUS_SHARED_DEVICE", Detail = "Multiple queues reference this device. Presence is not assigned to a unique queue." };
            var jobs = new List<JobReading>();
            var jobQuery = "UNAVAILABLE";
            var nativeQueue = "UNKNOWN";
            try
            {
                if (!OpenPrinter(r.Queue, out var printer, IntPtr.Zero)) throw new Win32Exception(Marshal.GetLastWin32Error());
                using (printer)
                {
                    try
                    {
                        GetPrinter(printer, 6, IntPtr.Zero, 0, out var required);
                        var error = Marshal.GetLastWin32Error();
                        if (required < 4 || required > 1024 * 1024) throw new Win32Exception(error);
                        var buffer = Marshal.AllocHGlobal(checked((int)required));
                        try
                        {
                            if (!GetPrinter(printer, 6, buffer, required, out _)) throw new Win32Exception(Marshal.GetLastWin32Error());
                            var status = unchecked((uint)Marshal.ReadInt32(buffer));
                            signals["GetPrinter.PRINTER_INFO_6.Status"] = $"0x{status:X8}";
                            nativeQueue = MapNative(status);
                            signals["GetPrinter.QueueClassification"] = nativeQueue;
                        }
                        finally { Marshal.FreeHGlobal(buffer); }
                    }
                    catch (Exception ex) { signals["GetPrinter.QueryError"] = SafeError(ex); }
                    ReadJobs(printer, jobs);
                    jobQuery = "AVAILABLE_FIRST_256_JOBS";
                }
            }
            catch (Exception ex) { signals["NativeSpooler.QueryError"] = SafeError(ex); }
            try
            {
                using var printServer = new LocalPrintServer();
                using var queue = printServer.GetPrintQueue(r.Queue);
                queue.Refresh();
                signals["PrintQueue.QueueStatus"] = $"{queue.QueueStatus} (0x{(uint)queue.QueueStatus:X8})";
                signals["PrintQueue.IsOffline"] = queue.IsOffline.ToString();
                signals["PrintQueue.IsInError"] = queue.IsInError.ToString();
                signals["PrintQueue.NumberOfJobs"] = queue.NumberOfJobs.ToString();
                signals["PrintQueue.QueueClassification"] = MapNative((uint)queue.QueueStatus);
                signals["PrintQueue.Driver"] = queue.QueueDriver.Name;
            }
            catch (Exception ex) { signals["PrintQueue.QueryError"] = SafeError(ex); }
            signals["DriverSpecific"] = "NOT_QUERIED: no validated Canon/Epson status protocol. EnableBIDI/installed monitor alone is not telemetry.";
            var classifications = signals.Where(p => p.Key.EndsWith("QueueClassification")).Select(p => p.Value).Where(v => v != "UNKNOWN").Distinct().ToArray();
            var queueState = ResolveQueueStatus(classifications);
            var evidence = Classify(usb, signals, jobs, jobQuery, queueState);
            var detail = $"Physical=UNKNOWN (unverified); Connection={evidence.ConnectionStatus}; Windows queue={queueState}\r\n" +
                $"USB mapping={usb.Mapping}; Device={usb.DeviceInstanceId ?? "unmapped"}; Present={usb.Present?.ToString() ?? "unknown"}; CMStatus={usb.DeviceStatus}; Problem={usb.ProblemCode}\r\n{usb.Detail}\r\n" +
                string.Join("; ", signals.Select(p => $"{p.Key}={p.Value}")) + $"\r\nJobs={jobs.Count}; JobQuery={jobQuery}; " +
                string.Join("; ", jobs.Select(j => $"Job {j.Id}: submitted={j.SubmittedAt:O}; flags=0x{j.RawStatus:X8}; pages={j.TotalPages}/{j.PagesPrinted}; copiesRequested={j.CopiesRequested}; duplexRequested={j.DuplexRequested}; {j.RequestedSettings?.Diagnostic ?? "DEVMODE unknown"}; documentPages=Unknown; {j.Interpretation}; {j.LayoutEvidence}")) + $"\r\n{evidence.ErrorAttribution}\r\nDecision: {evidence.DecisionSource}";
            return r with { State = evidence.PhysicalStatus, Detail = detail, Evidence = evidence };
        }).ToArray();
    }
    internal static string ResolveQueueStatus(IEnumerable<string> reports)
    {
        var values = reports.Where(v => v != "UNKNOWN").Distinct().ToArray();
        // A zero flag bitmap does not contradict an explicit WMI printing report.
        if (values.Contains("PRINTING") && values.All(v => v is "PRINTING" or "NORMAL_REPORTED")) return "PRINTING";
        return values.Length > 1 ? "CONFLICTING_WINDOWS_REPORTS" : values.FirstOrDefault() ?? "UNKNOWN";
    }
    internal static string MapNative(uint status)
    {
        if ((status & (2u | 8u | 16u | 64u | 524288u | 1048576u | 2097152u | 4194304u)) != 0) return "ERROR";
        if ((status & 128u) != 0) return "OFFLINE";
        if ((status & 1u) != 0) return "PAUSED";
        if ((status & 1024u) != 0) return "PRINTING";
        if (status == 0) return "NORMAL_REPORTED";
        return "OTHER_WINDOWS_FLAGS";
    }
    private static string SafeError(Exception ex) => ex is Win32Exception w ? $"Win32 {w.NativeErrorCode}" : ex.GetType().Name;
    private static UsbEvidence ReadMapping(string queue)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Print\Printers\" + queue + @"\PnPData");
            var id = key?.GetValue("DeviceInstanceId") as string;
            if (string.IsNullOrEmpty(id) || !id.StartsWith(@"USBPRINT\", StringComparison.OrdinalIgnoreCase))
                return new("UNMAPPED", id, null, null, null, "No exact USBPRINT device ID in queue PnPData. Port/name matching is deliberately not used.");
            uint node = 0;
            var result = CM_Locate_DevNode(ref node, id, 0); // Normal: connected devnode tree, not phantom nodes.
            if (result == 0x0D) return new("EXACT_QUEUE_DEVICE_ID", id, false, null, null,
                "Exact queue registry association; CM reports no connected devnode. Power-off, unplugging and enumeration failure cannot be distinguished.");
            if (result != 0) return new("EXACT_QUEUE_DEVICE_ID", id, null, null, null, $"CM_Locate_DevNode result=0x{result:X}; presence unknown.");
            result = CM_Get_DevNode_Status(out var status, out var problem, node, 0);
            if (result != 0) return new("EXACT_QUEUE_DEVICE_ID", id, null, null, null, $"CM_Get_DevNode_Status result=0x{result:X}; presence unknown.");
            var parentDetail = "";
            if (CM_Get_Parent(out var parent, node, 0) == 0)
            {
                var parentId = new StringBuilder(512);
                if (CM_Get_Device_ID(parent, parentId, parentId.Capacity, 0) == 0) parentDetail = " Parent=" + parentId;
            }
            return new("EXACT_QUEUE_DEVICE_ID", id, true, status, problem,
                $"Exact queue registry association; connected devnode observed. DN_STARTED={(status & 8) != 0}; driver problem={problem}. USB presence does not prove power or readiness." + parentDetail);
        }
        catch (Exception ex) { return new("UNAVAILABLE", null, null, null, null, "Queue/device query failed: " + SafeError(ex)); }
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct JobInfo2
    {
        public uint Id;
        public IntPtr PrinterName, MachineName, UserName, Document, NotifyName, DataType, PrintProcessor, Parameters, DriverName, DevMode, StatusText, SecurityDescriptor;
        public uint Status, Priority, Position, StartTime, UntilTime, TotalPages, Size;
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
        public uint Time, PagesPrinted;
    }
    private static void ReadJobs(PrinterHandle handle, List<JobReading> jobs)
    {
        var first = EnumJobs(handle, 0, 256, 2, IntPtr.Zero, 0, out var needed, out _);
        var error = Marshal.GetLastWin32Error();
        if (!first && error != 122) throw new Win32Exception(error);
        if (needed == 0) return;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (needed > 16 * 1024 * 1024) throw new InvalidOperationException("Job diagnostic buffer limit exceeded.");
            var size = needed;
            var buffer = Marshal.AllocHGlobal(checked((int)size));
            try
            {
                if (!EnumJobs(handle, 0, 256, 2, buffer, size, out needed, out var returned))
                {
                    error = Marshal.GetLastWin32Error();
                    if (error == 122) continue;
                    throw new Win32Exception(error);
                }
                var stride = Marshal.SizeOf<JobInfo2>();
                if ((ulong)returned * (ulong)stride > size) throw new InvalidOperationException("Invalid job response size.");
                for (int i = 0; i < returned; i++)
                {
                    var job = Marshal.PtrToStructure<JobInfo2>(IntPtr.Add(buffer, i * stride));
                    DateTimeOffset? submitted = null;
                    try { submitted = new DateTimeOffset(job.Year,job.Month,job.Day,job.Hour,job.Minute,job.Second,job.Milliseconds,TimeSpan.Zero); } catch(ArgumentOutOfRangeException) { }
                    var layout = ReadRequestedLayout(job.DevMode, buffer, size);
                    jobs.Add(new(job.Id, job.Status, job.TotalPages, job.PagesPrinted) { DriverStatusTextPresent = job.StatusText != IntPtr.Zero,
                        SubmittedAt = submitted, CopiesRequested = layout.Copies, DuplexRequested = layout.Duplex, RequestedSettings = ReadRequestedSettings(job.DevMode, buffer, size) });
                }
                return;
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        throw new InvalidOperationException("Job list changed during query.");
    }
    internal static (int? Copies, string? Duplex) ReadRequestedLayout(IntPtr devMode, IntPtr buffer, uint size)
    {
        // DEVMODEW fixed prefix: dmSize=68, dmFields=72, dmCopies=86, dmDuplex=94.
        var offset = devMode.ToInt64() - buffer.ToInt64();
        if (devMode == IntPtr.Zero || offset < 0 || offset > (long)size - 96) return (null,null);
        int dmSize = (ushort)Marshal.ReadInt16(devMode,68);
        if (dmSize < 96 || dmSize > (long)size-offset) return (null,null);
        var fields = unchecked((uint)Marshal.ReadInt32(devMode,72));
        var copies = Marshal.ReadInt16(devMode,86);
        var duplex = Marshal.ReadInt16(devMode,94);
        return ((fields & 0x100)!=0 && copies>0 ? copies : null,
            (fields & 0x1000)==0 ? null : duplex switch {1=>"SIMPLEX_REQUESTED",2=>"LONG_EDGE_DUPLEX_REQUESTED",3=>"SHORT_EDGE_DUPLEX_REQUESTED",_=>null});
    }
    internal static RequestedPrintSettings? ReadRequestedSettings(IntPtr devMode, IntPtr buffer, uint size)
    {
        var offset=devMode.ToInt64()-buffer.ToInt64();
        if(devMode==IntPtr.Zero || offset<0 || offset>(long)size-76) return null;
        var dmSize=unchecked((ushort)Marshal.ReadInt16(devMode,68));
        if(dmSize<76 || dmSize>(long)size-offset) return null;
        var fields=unchecked((uint)Marshal.ReadInt32(devMode,72));
        short? ReadShort(int position,uint bit) => (fields&bit)!=0 && dmSize>=position+2 && Marshal.ReadInt16(devMode,position)>0 ? Marshal.ReadInt16(devMode,position) : null;
        uint? nup=(fields&0x40)!=0 && dmSize>=184 ? unchecked((uint)Marshal.ReadInt32(devMode,180)) : null;
        return new(dmSize,unchecked((ushort)Marshal.ReadInt16(devMode,70)),fields,ReadShort(78,2),ReadShort(82,8),ReadShort(80,4),ReadShort(84,16),nup);
    }
    private sealed class PrinterHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public PrinterHandle() : base(true) { }
        protected override bool ReleaseHandle() => ClosePrinter(handle);
    }
    [DllImport("winspool.drv", EntryPoint = "OpenPrinterW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool OpenPrinter(string name, out PrinterHandle handle, IntPtr defaults);
    [DllImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool ClosePrinter(IntPtr handle);
    [DllImport("winspool.drv", EntryPoint = "GetPrinterW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetPrinter(PrinterHandle handle, uint level, IntPtr buffer, uint size, out uint needed);
    [DllImport("winspool.drv", EntryPoint = "EnumJobsW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool EnumJobs(PrinterHandle handle, uint first, uint count, uint level, IntPtr buffer, uint size, out uint needed, out uint returned);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Locate_DevNodeW", CharSet = CharSet.Unicode)]
    private static extern uint CM_Locate_DevNode(ref uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_DevNode_Status(out uint status, out uint problem, uint node, uint flags);
    [DllImport("cfgmgr32.dll")] private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32.dll", EntryPoint = "CM_Get_Device_IDW", CharSet = CharSet.Unicode)]
    private static extern uint CM_Get_Device_ID(uint node, StringBuilder buffer, int length, uint flags);
}
