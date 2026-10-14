namespace SnapPrintMonitoringDesktop;

// Opt-in grants permission, not missing page/copy/layout semantics or physical evidence.
internal static class SpoolerEstimatePolicy
{
    internal static string Decision(bool requested, uint flags, uint pages, uint printed, int? copies, string? duplex, bool gap, bool hasIdentity)
    {
        if(!requested) return "Not deducted — Automatic Paper Estimate is off";
        if((flags & (2u|4u|32u|64u|256u|512u|1024u|2048u))!=0) return "Not deducted — Cancelled, failed or interrupted job reported";
        if(gap || !hasIdentity) return "Not deducted — Monitoring continuity or job identity unavailable";
        if((flags & (128u|4096u))==0) return "Not deducted — Unknown job outcome";
        if(pages==0 || printed==0) return "Not deducted — Missing reported page counts";
        if(copies is null or < 1 || duplex is not ("SIMPLEX_REQUESTED" or "LONG_EDGE_DUPLEX_REQUESTED" or "SHORT_EDGE_DUPLEX_REQUESTED")) return "Not deducted — Missing print settings";
        if(printed!=pages) return "Not deducted — Partial or inconsistent reported pages";
        return "Not deducted — Page/copy semantics, page ranges and driver layout are not established; spooler completion is unverified";
    }
}
