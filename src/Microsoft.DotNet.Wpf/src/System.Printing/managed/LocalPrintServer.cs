namespace System.Printing;

public sealed partial class LocalPrintServer
{
    public LocalPrintServer()
    {
    }

    public LocalPrintServer(LocalPrintServerIndexedProperty[] propertiesFilter)
    {
    }

    public LocalPrintServer(LocalPrintServerIndexedProperty[] propertiesFilter, PrintSystemDesiredAccess desiredAccess)
    {
    }

    public LocalPrintServer(PrintSystemDesiredAccess desiredAccess)
    {
    }

    public LocalPrintServer(string[] propertiesFilter)
    {
    }

    public LocalPrintServer(string[] propertiesFilter, PrintSystemDesiredAccess desiredAccess)
    {
    }

    public PrintQueue DefaultPrintQueue
    {
        get
        {
            var info = PrintBackend.GetPrinters().FirstOrDefault(p => p.IsDefault)
                       ?? throw new InvalidOperationException("There is no default print queue.");
            return new PrintQueue(this, info);
        }

        set => throw new PlatformNotSupportedException("Changing the default print queue is not supported.");
    }

    public static PrintQueue GetDefaultPrintQueue()
    {
        using var server = new LocalPrintServer();
        return server.DefaultPrintQueue;
    }

    public override void Commit()
    {
    }

    public override void Refresh()
    {
    }
}
