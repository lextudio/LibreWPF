namespace System.Printing;

public abstract partial class PrintSystemObject
{
    private string? _name;

    protected PrintSystemObject()
    {
    }

    protected PrintSystemObject(PrintSystemObjectLoadMode mode)
    {
    }

    public virtual string Name
    {
        get => _name ?? string.Empty;
        internal set => _name = value;
    }
}
