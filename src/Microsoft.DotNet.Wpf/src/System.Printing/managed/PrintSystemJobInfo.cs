using PrintingTools.Core;

namespace System.Printing;

/// <summary>One job as the backend reports it; the fields it does not report stay at their defaults.</summary>
public partial class PrintSystemJobInfo
{
    private readonly PrintQueue _queue;
    private PrintJobInfo _info;

    internal PrintSystemJobInfo(PrintQueue queue, PrintJobInfo info)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _info = info ?? throw new ArgumentNullException(nameof(info));
        base.Name = info.Name;
    }

    public PrintQueue HostingPrintQueue => _queue;

    public PrintServer HostingPrintServer => _queue.HostingPrintServer;

    public int JobIdentifier => _info.Id;

    public string JobName
    {
        get => _info.Name;
        set => throw new PlatformNotSupportedException("Renaming a print job is not supported.");
    }

    public PrintJobStatus JobStatus =>
        MapStatus(_info.State) | (_queue.IsOffline ? PrintJobStatus.Offline : PrintJobStatus.None);

    public int NumberOfPages => _info.TotalPages;

    public int NumberOfPagesPrinted => _info.PagesPrinted;

    public int PositionInPrintQueue => _info.Position;

    public string Submitter => _info.User ?? string.Empty;

    public DateTime TimeJobSubmitted => _info.SubmittedAt?.LocalDateTime ?? DateTime.MinValue;

    public bool IsBlocked => false;

    public bool IsCompleted => _info.State == PrintJobState.Completed;

    public bool IsDeleted => _info.State == PrintJobState.Canceled;

    public bool IsDeleting => false;

    public bool IsInError => _info.State is PrintJobState.Aborted or PrintJobState.Stopped;

    public bool IsOffline => _queue.IsOffline;

    public bool IsPaperOut => false;

    public bool IsPaused => _info.State == PrintJobState.PendingHeld;

    public bool IsPrinted => _info.State == PrintJobState.Completed;

    public bool IsPrinting => _info.State == PrintJobState.Processing;

    public bool IsRestarted => false;

    public bool IsRetained => false;

    public bool IsSpooling => _info.State == PrintJobState.Pending;

    public bool IsUserInterventionRequired => _info.State == PrintJobState.Stopped;

    public void Cancel()
    {
        Adapter().CancelJobAsync(_queue.Info.Id, _info.Id).GetAwaiter().GetResult();

        // A cancelled job is often purged before a follow-up read; keep the accepted cancellation locally instead of
        // turning a successful cancel into an "unknown job" error.
        _info = new PrintJobInfo(_info.Id, _info.Name, PrintJobState.Canceled, _info.User, _info.PagesPrinted, _info.TotalPages, _info.Position, _info.SubmittedAt, _info.StateReasons);
        base.Name = _info.Name;
    }

    public static PrintSystemJobInfo Get(PrintQueue printQueue, int jobIdentifier) =>
        (printQueue ?? throw new ArgumentNullException(nameof(printQueue))).GetJob(jobIdentifier);

    public override void Commit() =>
        throw new PlatformNotSupportedException("Committing print job changes is not supported.");

    public override void Refresh()
    {
        // A completed or cancelled job may be purged; like WPF's retained job, keep the last reported state.
        var refreshed = Adapter().GetJobAsync(_queue.Info.Id, _info.Id).GetAwaiter().GetResult();
        if (refreshed is not null)
        {
            _info = refreshed;
            base.Name = _info.Name;
        }
    }

    private static IPrintJobAdapter Adapter() =>
        PrintBackend.Adapter as IPrintJobAdapter
        ?? throw new PlatformNotSupportedException("This print backend does not expose print jobs.");

    private static PrintJobStatus MapStatus(PrintJobState state) => state switch
    {
        PrintJobState.Pending => PrintJobStatus.Spooling,
        PrintJobState.PendingHeld => PrintJobStatus.Paused,
        PrintJobState.Processing => PrintJobStatus.Printing,
        PrintJobState.Stopped => PrintJobStatus.Error | PrintJobStatus.UserIntervention,
        PrintJobState.Canceled => PrintJobStatus.Deleted,
        PrintJobState.Aborted => PrintJobStatus.Error,
        PrintJobState.Completed => PrintJobStatus.Printed | PrintJobStatus.Completed,
        _ => PrintJobStatus.None,
    };
}
