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
