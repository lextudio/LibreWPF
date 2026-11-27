using PrintingTools.Core;

namespace System.Printing;

public partial class PrintQueue
{
    private PrinterInfo _info;
    private PrintServer _hostingServer;

    public PrintQueue(PrintServer printServer, string printQueueName)
        : this(printServer, Find(printServer, printQueueName))
    {
    }

    public PrintQueue(PrintServer printServer, string printQueueName, int printSchemaVersion)
        : this(printServer, printQueueName)
    {
    }

    public PrintQueue(PrintServer printServer, string printQueueName, int printSchemaVersion, PrintSystemDesiredAccess desiredAccess)
        : this(printServer, printQueueName)
    {
    }

    public PrintQueue(PrintServer printServer, string printQueueName, PrintQueueIndexedProperty[] propertyFilter)
        : this(printServer, printQueueName)
    {
    }

    public PrintQueue(PrintServer printServer, string printQueueName, PrintQueueIndexedProperty[] propertyFilter, PrintSystemDesiredAccess desiredAccess)
        : this(printServer, printQueueName)
    {
    }

    public PrintQueue(PrintServer printServer, string printQueueName, PrintSystemDesiredAccess desiredAccess)
        : this(printServer, printQueueName)
    {
    }

    public PrintQueue(PrintServer printServer, string printQueueName, string[] propertyFilter)
        : this(printServer, printQueueName)
    {
    }

    public PrintQueue(PrintServer printServer, string printQueueName, string[] propertyFilter, PrintSystemDesiredAccess desiredAccess)
        : this(printServer, printQueueName)
    {
    }

    internal PrintQueue(PrintServer printServer, PrinterInfo info)
    {
        _hostingServer = printServer ?? throw new ArgumentNullException(nameof(printServer));
        _info = info ?? throw new ArgumentNullException(nameof(info));
        base.Name = info.Name;
    }

    /// <summary>The backend's printer id (the CUPS destination or the spooler printer name).</summary>
    internal PrinterInfo Info => _info;

    public sealed override string Name
    {
        get => base.Name;
        internal set => base.Name = value;
    }

    public string FullName => _hostingServer.Name.TrimEnd('\\') + "\\" + Name;

    public virtual string Description => Attribute("printer-info") ?? Attribute("Description") ?? string.Empty;

    public virtual string Comment
    {
        get => Attribute("Comment") ?? string.Empty;
        set => throw new PlatformNotSupportedException("Changing the queue comment is not supported.");
    }

    public virtual string Location
    {
        get => Attribute("printer-location") ?? Attribute("Location") ?? string.Empty;
        set => throw new PlatformNotSupportedException("Changing the queue location is not supported.");
    }

    public virtual PrintServer HostingPrintServer
    {
        get => _hostingServer;
        protected set => _hostingServer = value ?? throw new ArgumentNullException(nameof(value));
    }

    public bool IsOffline => !_info.IsOnline;

    public virtual PrintTicket DefaultPrintTicket
    {
        get
        {
            var capabilities = Capabilities(null);
            return TicketFrom(PrintSchema.Defaults(capabilities), capabilities);
        }
        set => throw new PlatformNotSupportedException("Changing the queue's default print ticket is not supported.");
    }

    public virtual PrintTicket UserPrintTicket
    {
        get => DefaultPrintTicket;
        set => throw new PlatformNotSupportedException("Changing the user print ticket is not supported.");
    }

    public PrintCapabilities GetPrintCapabilities() => new(GetPrintCapabilitiesAsXml());

    public PrintCapabilities GetPrintCapabilities(PrintTicket printTicket) => new(GetPrintCapabilitiesAsXml(printTicket));

    public MemoryStream GetPrintCapabilitiesAsXml() => PrintSchema.CapabilitiesXml(Capabilities(null));

    public MemoryStream GetPrintCapabilitiesAsXml(PrintTicket printTicket)
    {
        ArgumentNullException.ThrowIfNull(printTicket);
        return PrintSchema.CapabilitiesXml(Capabilities(printTicket));
    }

    public ValidationResult MergeAndValidatePrintTicket(PrintTicket basePrintTicket, PrintTicket deltaPrintTicket) =>
        MergeAndValidatePrintTicket(basePrintTicket, deltaPrintTicket, PrintTicketScope.JobScope);

    public ValidationResult MergeAndValidatePrintTicket(PrintTicket basePrintTicket, PrintTicket deltaPrintTicket, PrintTicketScope scope)
    {
        // A null base ticket means "the queue's defaults", as in WPF; the delta is required.
        ArgumentNullException.ThrowIfNull(deltaPrintTicket);
        var capabilities = Capabilities(null);
        var requested = PrintSchema.Apply(deltaPrintTicket, PrintSchema.Apply(basePrintTicket, PrintSchema.Defaults(capabilities), capabilities), capabilities);

        // MergeWithCapabilities replaces what the queue cannot do with a supported value and records a warning for each.
        var validated = requested.MergeWithCapabilities(capabilities);
        var status = validated.Warnings.Count == 0 ? ConflictStatus.NoConflict : ConflictStatus.ConflictResolved;
        return new ValidationResult(PrintSchema.TicketXml(validated, includePageSize: PrintSchema.ListsPageSizes(capabilities)), status);
    }

    private PrintingTools.Core.PrintCapabilities Capabilities(PrintTicket? baseTicket)
    {
        var adapter = PrintBackend.Adapter;
        var queueCapabilities = adapter.GetCapabilitiesAsync(_info.Id, null).GetAwaiter().GetResult();
        if (baseTicket is null)
        {
            return queueCapabilities;
        }

        // Capabilities can depend on the ticket (a paper size can change what is available). The ticket's page size is resolved
        // against this queue's own sizes, not against a generic list.
        var defaults = PrintSchema.Defaults(queueCapabilities);
        var model = PrintSchema.Apply(baseTicket, defaults, queueCapabilities);
        if (!queueCapabilities.PageMediaSizes.Any(info => info.Size.Equals(model.PageMediaSize)))
        {
            // The ticket named a size this queue does not have (Apply marked it with a placeholder so that validation can
            // report it). The adapter must only ever see a real size: ask with the queue's default.
            model.PageMediaSize = defaults.PageMediaSize;
        }

        return adapter.GetCapabilitiesAsync(_info.Id, model).GetAwaiter().GetResult();
    }

    private static PrintTicket TicketFrom(PrintingTools.Core.PrintTicketModel model, PrintingTools.Core.PrintCapabilities capabilities) =>
        new(PrintSchema.TicketXml(model, includePageSize: PrintSchema.ListsPageSizes(capabilities)));

    // No property of a queue is writable (the setters throw), so there is never anything pending to commit.
    public override void Commit()
    {
    }

    /// <summary>Re-reads the queue from the backend. The queue may have disappeared, which is an error.</summary>
    public override void Refresh()
    {
        _info = FindById(_info.Id.Value);
        base.Name = _info.Name;
    }

    private string? Attribute(string key) => _info.Attributes.TryGetValue(key, out var value) ? value : null;

    private static PrinterInfo FindById(string id) =>
        PrintBackend.GetPrinters().FirstOrDefault(p => string.Equals(p.Id.Value, id, StringComparison.Ordinal))
        ?? throw new InvalidOperationException($"The print queue '{id}' no longer exists.");

    private static PrinterInfo Find(PrintServer printServer, string printQueueName)
    {
        ArgumentNullException.ThrowIfNull(printServer);
        ArgumentException.ThrowIfNullOrEmpty(printQueueName);
        var name = printQueueName;
        if (printQueueName.StartsWith("\\\\", StringComparison.Ordinal))
        {
            // "\\\\server\\queue": both parts are required. Only the local machine's own prefix can be dropped.
            var rest = printQueueName[2..];
            var separator = rest.IndexOf('\\');
            if (separator <= 0 || separator == rest.Length - 1)
            {
                throw new ArgumentException($"'{printQueueName}' is not a print queue name of the form \\\\server\\queue.", nameof(printQueueName));
            }

            if (!rest[..separator].Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
            {
                throw new PlatformNotSupportedException($"Print queues on other servers are not supported ('{printQueueName}').");
            }

            name = rest[(separator + 1)..];
        }
        else if (printQueueName.Contains('\\'))
        {
            throw new ArgumentException($"'{printQueueName}' is not a valid print queue name.", nameof(printQueueName));
        }

        return PrintBackend.GetPrinters().FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Id.Value, name, StringComparison.OrdinalIgnoreCase))
               ?? throw new ArgumentException($"The print queue '{printQueueName}' does not exist.", nameof(printQueueName));
    }
}
