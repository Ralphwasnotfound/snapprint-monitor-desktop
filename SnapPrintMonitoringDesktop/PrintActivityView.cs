namespace SnapPrintMonitoringDesktop;

internal sealed class PrintActivityView : UserControl
{
    private readonly DataGridView grid = new() { Dock=DockStyle.Fill,ReadOnly=true,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,RowHeadersVisible=false,BackgroundColor=Color.White,AutoGenerateColumns=false,SelectionMode=DataGridViewSelectionMode.FullRowSelect };
    private readonly ComboBox filter=new() { DropDownStyle=ComboBoxStyle.DropDownList,Width=230 };
    private readonly Label info=new() { AutoSize=true,MaximumSize=new Size(850,0),Padding=new Padding(0,8,0,8) };
    private readonly Label paperSummary = new() { AutoSize=true,Padding=new Padding(0,8,0,8) };
    private IReadOnlyList<PaperInventory> inventories=[];
    internal void UpdatePaper(IReadOnlyList<PaperInventory> value) { inventories=value; RenderSummary(); }
    private readonly bool current;
    private IReadOnlyList<PrintHistoryEntry> entries=[];
    private string? storageError;
    internal PrintActivityView(bool current)
    {
        grid.DefaultCellStyle.WrapMode=DataGridViewTriState.True; grid.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells; grid.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.AutoSize;
        this.current=current; Dock=DockStyle.Fill; Padding=new Padding(20); BackColor=Color.White;
        var layout=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=4 };
        layout.ColumnStyles.Add(new(SizeType.Percent,100)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.AutoSize)); layout.RowStyles.Add(new(SizeType.Percent,100));
        filter.Items.AddRange(["All printers","Canon MG3000 series","EPSON L3210 Series"]);filter.SelectedIndex=0;
        filter.SelectedIndexChanged+=(_,_)=>Render();
        layout.Controls.Add(filter,0,0);layout.Controls.Add(info,0,1);layout.Controls.Add(paperSummary,0,2);layout.Controls.Add(grid,0,3);Controls.Add(layout);
        foreach(var name in new[]{"Date / time","Printer","Document","PagesPrinted reported (highest)","TotalPages reported (highest)","Estimated sheets","Last known status","Accounting","Copies requested","Duplex requested","Paper requested","N-up handling","Document pages"}) grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText=name,FillWeight=name=="Last known status"?170:100,MinimumWidth=90,SortMode=DataGridViewColumnSortMode.NotSortable });
    }
    internal void Update(IReadOnlyList<PrintHistoryEntry> value,string? error) { entries=value; storageError=error; Render(); }
    protected override void OnResize(EventArgs e)
    { base.OnResize(e); if(filter.SelectedIndex>=0) { paperSummary.MaximumSize=new Size(Math.Max(280,ClientSize.Width-40),0); info.MaximumSize=paperSummary.MaximumSize; RenderSummary(); } }
    private void RenderSummary()
    {
        var selected=filter.SelectedIndex==0 ? PaperInventoryService.PrinterNames : new[]{(string)filter.SelectedItem!};
        paperSummary.Text=string.Join("\r\n",selected.Select(name=>
        {
            var entry=inventories.FirstOrDefault(i=>i.PrinterName==name)??PaperInventory.Empty(name);
            var state=entry.EstimatedSheetsRemaining is null?"Unknown":entry.EmptyWarning?"PAPER EMPTY":entry.LowPaperWarning?"LOW PAPER":"NORMAL";
            return $"{name}\r\nSheets Loaded: {entry.SheetsLoaded?.ToString()??"Unknown"} · Low Paper Alert At: {entry.LowPaperThreshold} · Paper Remaining (Estimated): {entry.EstimatedSheetsRemaining?.ToString()??"Unknown"}\r\nTotal Estimated Sheets Accounted For: {entry.TotalEstimatedSheetsAccountedFor?.ToString()??"Unknown"} · Paper Status: {state} · Accounting confidence: {(entry.ReconciliationRequired?"Needs Checking — Paper count needs checking":"Estimated")}";
        }));
    }
    private void Render()
    {
        RenderSummary();
        grid.Rows.Clear();
        foreach(var item in entries.Where(e=>(!current||e.Active)&&(filter.SelectedIndex==0||e.Printer==(string)filter.SelectedItem!)))
            grid.Rows.Add(item.FirstSeen.ToLocalTime().ToString("MMM d HH:mm:ss"),item.Printer,item.Document,item.PagesText,item.TotalText,item.EstimatedSheetsUsed?.ToString()??"Unknown",item.Status+(item.MonitoringGap?" · monitoring gap":""),item.Accounting,item.CopiesRequested?.ToString()??"Unknown",item.DuplexRequested?.Replace("_REQUESTED", " requested").Replace("_", " ")??"Unknown",item.RequestedSettings?.Paper??"Unknown",item.RequestedSettings?.NupHandling??"Unknown","Unknown");
        info.Text=current ? "Currently observed jobs. Printing — page progress unavailable. No percentage is inferred from spooler counters." : "Local history: last 30 days, up to 500 observed jobs. Document names are not collected. Pages are not sheets. Use the persisted inventory summary above for accounted sheets.";
        if(grid.Rows.Count==0) info.Text+=" No jobs observed.";
        if(storageError is not null) info.Text+=" " + storageError;
    }
}
