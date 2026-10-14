using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SnapPrintMonitoringDesktop;

internal sealed record PrintHistoryEntry(string Key, string Printer, uint JobId, DateTimeOffset? SubmittedAt,
    DateTimeOffset FirstSeen, DateTimeOffset LastSeen, uint PagesReported, uint TotalPages, int? CopiesRequested,
    string? DuplexRequested, uint RawStatus, string Status, bool Active, bool MonitoringGap)
{
    public RequestedPrintSettings? RequestedSettings { get; init; }
    public uint? DocumentPages => null;
    public string Document => "Not collected (privacy)";
    public int? EstimatedSheetsUsed { get; init; }
    public bool PhysicalCompletionVerified => false;
    public bool AutomaticEstimateRequested { get; init; }
    public bool InventoryDeducted { get; init; }
    public string? AccountingDecision { get; init; }
    public string Accounting => AccountingDecision ?? SpoolerEstimatePolicy.Decision(AutomaticEstimateRequested, RawStatus, TotalPages, PagesReported, CopiesRequested, DuplexRequested, MonitoringGap, SubmittedAt is not null);
    public string PagesText => PagesReported == 0 ? "Unknown" : PagesReported.ToString();
    public string TotalText => TotalPages == 0 ? "Unknown" : TotalPages.ToString();
}

// Local metadata only. Never infer physical completion or inventory consumption from this journal.
internal sealed class PrintHistoryService : IDisposable
{
    internal const int Limit = 500;
    internal static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SnapPrintMonitoringDesktop", "print-history.json");
    private readonly string path;
    private FileStream? lease;
    private List<PrintHistoryEntry> entries = [];
    private DateTimeOffset? previous;
    private readonly string session = Guid.NewGuid().ToString("N");
    private readonly Dictionary<string,string> untimed = new();
    private bool readOnly;
    internal string? Error { get; private set; }
    internal IReadOnlyList<PrintHistoryEntry> Current => entries.ToArray();
    internal PrintHistoryService(string path)
    {
        this.path = Path.GetFullPath(path);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(this.path)!);
            lease = new(this.path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (File.Exists(this.path))
            {
                if(new FileInfo(this.path).Length > 2*1024*1024) throw new InvalidDataException();
                var loaded = JsonSerializer.Deserialize<List<PrintHistoryEntry>>(File.ReadAllText(this.path)) ?? throw new InvalidDataException();
                if(loaded.Count > Limit || loaded.Any(e=> e is null || !PaperInventoryService.PrinterNames.Contains(e.Printer) || e.Key is null || e.Key.Length!=64) || loaded.Select(e=>e.Key).Distinct().Count()!=loaded.Count) throw new InvalidDataException();
                entries = loaded.Select(e=>e.Active ? e with { Active=false, MonitoringGap=true, Status="Unknown outcome — application restarted" } : e).ToList();
                Save(entries);
            }
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        { readOnly=true; Error="Print history unavailable. Check local storage or another running monitor. Existing file preserved."; }
    }
    internal void Observe(PrinterSnapshot snapshot)
    {
        if(readOnly) return;
        var at=snapshot.LastUpdated;
        var gap=previous is not null && (at<previous || at-previous>TimeSpan.FromSeconds(15));
        previous=at;
        var next=entries.Where(e=>at-e.LastSeen<=TimeSpan.FromDays(30)).ToList();
        foreach(var printer in PaperInventoryService.PrinterNames)
        {
            var evidence=snapshot.Printers.FirstOrDefault(p=>string.Equals(p.Queue,printer,StringComparison.OrdinalIgnoreCase))?.Evidence;
            bool available=snapshot.DiscoveryError is null && evidence?.JobQueryStatus.StartsWith("AVAILABLE",StringComparison.Ordinal)==true;
            var seen=new HashSet<string>();
            if(available)
            foreach(var job in evidence!.Jobs)
            {
                var local=$"{printer}:{job.Id}";
                string identity;
                if(job.SubmittedAt is { } submitted) identity=$"{printer}:{job.Id}:{submitted.UtcTicks}";
                else { if(!untimed.TryGetValue(local,out var value)) untimed[local]=value=session+Guid.NewGuid().ToString("N"); identity=local+value; }
                var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
                seen.Add(key);
                var index=next.FindIndex(e=>e.Key==key);
                var old=index<0 ? null : next[index];
                bool cancelled=(job.RawStatus & (4u|256u))!=0;
                var status=cancelled ? "Cancelled/deleted reported — output unknown" : job.ErrorReported ? "Error reported — output unknown" : job.CompletionReported ? "Spooler completion reported — physical outcome unknown" : (job.RawStatus&16)!=0 ? "Printing — page progress unavailable." : "Queued/status unknown";
                // Preserve observed faults even if a later spooler flag clears them.
                var flags=(old?.RawStatus??0)|job.RawStatus;
                if((flags & (2u|4u|32u|64u|256u|512u))!=0 && !cancelled && !job.ErrorReported) status="Earlier error/cancellation reported — outcome unknown";
                var item=new PrintHistoryEntry(key,printer,job.Id,job.SubmittedAt,old?.FirstSeen??at,at,
                    Math.Max(old?.PagesReported??0,job.PagesPrinted),Math.Max(old?.TotalPages??0,job.TotalPages),job.CopiesRequested,job.DuplexRequested,flags,status,!job.CompletionReported && !cancelled,
                    gap || old?.MonitoringGap==true || (old is not null && !old.Active))
                { AutomaticEstimateRequested = old?.AutomaticEstimateRequested ?? snapshot.PaperInventories.FirstOrDefault(i=>i.PrinterName==printer)?.AutomaticPaperEstimateRequested == true };
                item = item with { AccountingDecision = item.Accounting, RequestedSettings = job.RequestedSettings ?? old?.RequestedSettings };
                if(old?.InventoryDeducted==true)
                    item=item with { InventoryDeducted=true, EstimatedSheetsUsed=old.EstimatedSheetsUsed, AccountingDecision=old.AccountingDecision };
                var inventory=snapshot.PaperInventories.FirstOrDefault(i=>i.PrinterName==printer);
                if(inventory?.PersistenceStatus=="AVAILABLE" && inventory.LastAutomaticDeduction is { SubmittedAt: not null } accepted &&
                    accepted.JobId==job.Id && accepted.SubmittedAt==job.SubmittedAt && accepted.At<=at && accepted.At>=accepted.SubmittedAt &&
                    inventory.AppliedUsageReceiptKeys.Contains(accepted.ReceiptKey))
                    item=item with { InventoryDeducted=true, EstimatedSheetsUsed=accepted.Sheets, AccountingDecision="Accounted — Unverified software estimate (persisted inventory receipt)" };
                if(index<0) next.Add(item); else next[index]=item;
            }
            for(int i=0;i<next.Count;i++)
            {
                var item=next[i];
                if(item.Printer!=printer || !item.Active || seen.Contains(item.Key)) continue;
                next[i]=item with { Active=false,MonitoringGap=item.MonitoringGap||gap||!available,Status=((item.RawStatus & (2u|4u|32u|64u|256u|512u))!=0 ? item.Status + " · " : "") + (available ? "Unknown outcome — no longer observed in queue" : "Unknown outcome — monitoring unavailable") };
                if(item.SubmittedAt is null) untimed.Remove($"{printer}:{item.JobId}");
            }
        }
        next=next.OrderByDescending(e=>e.LastSeen).Take(Limit).ToList();
        if(!next.SequenceEqual(entries) && Save(next)) entries=next;
    }
    private bool Save(List<PrintHistoryEntry> candidate)
    {
        var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        {
            using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var writer=new StreamWriter(stream)) { writer.Write(JsonSerializer.Serialize(candidate)); writer.Flush(); stream.Flush(true); }
            if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
            Error=null; return true;
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { Error="Print history could not be saved; displayed history may be incomplete."; return false; }
        finally { try { if(File.Exists(temp)) File.Delete(temp); } catch(IOException) { } catch(UnauthorizedAccessException) { } }
    }
    public void Dispose()=>lease?.Dispose();
}
