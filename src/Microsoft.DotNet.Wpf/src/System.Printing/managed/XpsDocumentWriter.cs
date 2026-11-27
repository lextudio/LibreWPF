using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Xps.Packaging;
using System.Windows.Xps.Serialization;

namespace System.Windows.Xps;

/// <summary>
/// Synchronous writing. Into an <see cref="XpsDocument"/> it uses ReachFramework's serialization manager, as the C++/CLI writer
/// did. To a print queue it renders the pages to bitmaps and submits a PDF through the backend's document submitter (see
/// <c>QueuePrinter</c>); that needs a backend that can take a PDF (CUPS), the Win32 spooler path is not implemented.
/// Asynchronous writing, collators, print tickets and copying an XPS file to a queue are not implemented and throw
/// <see cref="PlatformNotSupportedException"/>.
/// </summary>
public partial class XpsDocumentWriter
{
    private readonly XpsDocument? _destinationDocument;
    private readonly Printing.PrintQueue? _destinationQueue;
    private bool _done;

    internal XpsDocumentWriter(XpsDocument document)
    {
        _destinationDocument = document ?? throw new ArgumentNullException(nameof(document));
    }

    internal XpsDocumentWriter(Printing.PrintQueue printQueue)
    {
        _destinationQueue = printQueue ?? throw new ArgumentNullException(nameof(printQueue));
    }

    public override void Write(Visual visual) => WriteCore(visual, null);

    public override void Write(Visual visual, Printing.PrintTicket printTicket) => WriteCore(visual, printTicket);

    public override void Write(DocumentPaginator documentPaginator) => WriteCore(documentPaginator, null);

    public override void Write(DocumentPaginator documentPaginator, Printing.PrintTicket printTicket) => WriteCore(documentPaginator, printTicket);

    public override void Write(FixedDocument fixedDocument) => WriteCore(fixedDocument, null);

    public override void Write(FixedDocument fixedDocument, Printing.PrintTicket printTicket) => WriteCore(fixedDocument, printTicket);

    public override void Write(FixedDocumentSequence fixedDocumentSequence) => WriteCore(fixedDocumentSequence, null);

    public override void Write(FixedDocumentSequence fixedDocumentSequence, Printing.PrintTicket printTicket) => WriteCore(fixedDocumentSequence, printTicket);

    public override void Write(FixedPage fixedPage) => WriteCore(fixedPage, null);

    public override void Write(FixedPage fixedPage, Printing.PrintTicket printTicket) => WriteCore(fixedPage, printTicket);

    public void Write(string documentPath) =>
        throw new PlatformNotSupportedException("Copying an XPS file to a print queue is not implemented.");

    public void Write(string documentPath, XpsDocumentNotificationLevel notificationLevel) => Write(documentPath);

    private void WriteCore(object content, Printing.PrintTicket? printTicket)
    {
        ArgumentNullException.ThrowIfNull(content);
        if (_done)
        {
            throw new XpsWriterException("This XpsDocumentWriter has already written its document.");
        }

        if (printTicket is not null)
        {
            throw new PlatformNotSupportedException("Writing with a print ticket is not implemented.");
        }

        if (_destinationDocument is null)
        {
            // Only a successful submission uses the writer up: after a failure (no submitter, queue error) the caller sees that
            // error again on a retry rather than a misleading "already written".
            Printing.QueuePrinter.Print(_destinationQueue!, content);
            _done = true;
            return;
        }

        _done = true;
        var manager = _destinationDocument.CreateSerializationManager(bBatchMode: false);
        try
        {
            manager.SaveAsXaml(content);
        }
        finally
        {
            _destinationDocument.DisposeSerializationManager();
        }
    }
}
