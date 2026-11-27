using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Xps.Packaging;
using System.Windows.Xps.Serialization;

namespace System.Windows.Xps;

/// <summary>
/// Synchronous XPS writing into an <see cref="XpsDocument"/> through ReachFramework's serialization manager, as the
/// C++/CLI writer did. Writing to a print queue, asynchronous writing, collators and print tickets are not implemented
/// and throw <see cref="PlatformNotSupportedException"/>.
/// </summary>
public partial class XpsDocumentWriter
{
    private readonly XpsDocument? _destinationDocument;
    private bool _done;

    internal XpsDocumentWriter(XpsDocument document)
    {
        _destinationDocument = document ?? throw new ArgumentNullException(nameof(document));
    }

    internal XpsDocumentWriter(Printing.PrintQueue printQueue)
    {
        ArgumentNullException.ThrowIfNull(printQueue);
        // A queue needs the XPS bytes handed to the backend: the Win32 spooler takes XPS, CUPS takes PDF and there is no
        // XPS to PDF conversion yet. Refuse at the first Write rather than pretending.
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
            throw new PlatformNotSupportedException("Writing XPS to a print queue is not implemented.");
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
