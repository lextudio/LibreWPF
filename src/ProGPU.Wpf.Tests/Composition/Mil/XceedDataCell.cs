using ProGPU.Wpf.Interop;

namespace Xceed.Wpf.DataGrid;

public sealed class DataCell : IPortableVisualLayoutStateSource
{
    private readonly double _width;
    private readonly double _height;

    public DataCell(double width, double height)
    {
        _width = width;
        _height = height;
    }

    public bool TryGetPortableVisualLayoutState(out PortableVisualLayoutState state)
    {
        state = new PortableVisualLayoutState
        {
            HasRenderSize = true,
            RenderSize = new PortableSize(_width, _height)
        };
        return true;
    }
}
