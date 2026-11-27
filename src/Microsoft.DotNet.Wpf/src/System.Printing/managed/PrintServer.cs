using System.Linq;

namespace System.Printing;

public partial class PrintServer
{
    private static readonly EnumeratedPrintQueueTypes[] NoFlags = Array.Empty<EnumeratedPrintQueueTypes>();

    public PrintServer() : this(string.Empty)
    {
    }

    public PrintServer(PrintSystemDesiredAccess desiredAccess) : this(string.Empty)
    {
    }

    public PrintServer(string path)
    {
        // Only the local machine is supported: both backends talk to the local spooler or CUPS. Every accepted spelling
        // ("", "MACHINE", "\\MACHINE", "\\machine\") is stored as the canonical UNC form "\\MACHINE".
        ArgumentNullException.ThrowIfNull(path);
        var machine = path.Trim('\\');
        if (machine.Length > 0 && !machine.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase))
        {
            throw new PlatformNotSupportedException($"Remote print servers are not supported ('{path}').");
        }

        base.Name = "\\\\" + Environment.MachineName;
    }

    public PrintServer(string path, PrintServerIndexedProperty[] propertiesFilter) : this(path)
    {
    }

    public PrintServer(string path, PrintServerIndexedProperty[] propertiesFilter, PrintSystemDesiredAccess desiredAccess) : this(path)
    {
    }

    public PrintServer(string path, PrintSystemDesiredAccess desiredAccess) : this(path)
    {
    }

    public PrintServer(string path, string[] propertiesFilter) : this(path)
    {
    }

    public PrintServer(string path, string[] propertiesFilter, PrintSystemDesiredAccess desiredAccess) : this(path)
    {
    }

    /// <summary>The server's canonical UNC name (the ref API declares this override sealed and get-only).</summary>
    public sealed override string Name => base.Name;

    public PrintQueueCollection GetPrintQueues() => GetPrintQueues(NoFlags);

    public PrintQueueCollection GetPrintQueues(EnumeratedPrintQueueTypes[] enumerationFlag) => Enumerate(enumerationFlag);

    public PrintQueueCollection GetPrintQueues(PrintQueueIndexedProperty[] propertiesFilter) => Enumerate(NoFlags);

    public PrintQueueCollection GetPrintQueues(PrintQueueIndexedProperty[] propertiesFilter, EnumeratedPrintQueueTypes[] enumerationFlag) => Enumerate(enumerationFlag);

    public PrintQueueCollection GetPrintQueues(string[] propertiesFilter) => Enumerate(NoFlags);

    public PrintQueueCollection GetPrintQueues(string[] propertiesFilter, EnumeratedPrintQueueTypes[] enumerationFlag) => Enumerate(enumerationFlag);

    // Nothing is cached (every GetPrintQueues call asks the backend) and no property is writable, so there is neither
    // pending state to commit nor stale state to refresh.
    public override void Commit()
    {
    }

    public override void Refresh()
    {
    }

    private PrintQueueCollection Enumerate(EnumeratedPrintQueueTypes[] flags)
    {
        ArgumentNullException.ThrowIfNull(flags);

        // The backends only know local, queued destinations. Any other category cannot be answered, so refuse it
        // instead of returning a misleading (empty or full) collection.
        const EnumeratedPrintQueueTypes Supported = EnumeratedPrintQueueTypes.Local | EnumeratedPrintQueueTypes.Queued;
        foreach (var flag in flags)
        {
            if ((flag & ~Supported) != 0)
            {
                throw new PlatformNotSupportedException($"Enumerating print queues of type {flag} is not supported; only Local and Queued are.");
            }
        }

        var collection = new PrintQueueCollection();
        foreach (var info in PrintBackend.GetPrinters())
        {
            collection.Add(new PrintQueue(this, info));
        }

        return collection;
    }
}
