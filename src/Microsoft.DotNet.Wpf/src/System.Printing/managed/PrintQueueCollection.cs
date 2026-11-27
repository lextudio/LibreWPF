using System.Collections;

namespace System.Printing;

public partial class PrintQueueCollection
{
    private static readonly object SyncRootInstance = new();
    private readonly List<PrintQueue> _queues = new();

    public PrintQueueCollection()
    {
    }

    public PrintQueueCollection(PrintServer printServer, string[] propertyFilter)
    {
        ArgumentNullException.ThrowIfNull(printServer);
        foreach (var queue in printServer.GetPrintQueues(propertyFilter))
        {
            Add(queue);
        }
    }

    public PrintQueueCollection(PrintServer printServer, string[] propertyFilter, EnumeratedPrintQueueTypes[] enumerationFlag)
    {
        ArgumentNullException.ThrowIfNull(printServer);
        foreach (var queue in printServer.GetPrintQueues(propertyFilter, enumerationFlag))
        {
            Add(queue);
        }
    }

    public static object SyncRoot => SyncRootInstance;

    public void Add(PrintQueue printObject)
    {
        ArgumentNullException.ThrowIfNull(printObject);
        _queues.Add(printObject);
    }

    public virtual IEnumerator<PrintQueue> GetEnumerator() => _queues.GetEnumerator();

    public virtual IEnumerator GetNonGenericEnumerator() => _queues.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetNonGenericEnumerator();
}
