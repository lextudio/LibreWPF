namespace ProGPU.Wpf.Interop;

/// <summary>
/// A visual whose point hit testing is decided by a <c>HitTestCore</c> override rather than by
/// what it draws. WPF calls <c>HitTestCore</c> anywhere inside <c>UIElement.GetHitTestBounds</c>
/// (the render size together with the drawing content), so an override such as
/// <c>return new PointHitTestResult(this, point)</c> makes an element clickable where it draws
/// nothing at all. The GPU hit-test cache only knows drawn primitives, so without this region such
/// an element is never a pointer target there (the click falls through to whatever is behind it).
/// </summary>
/// <remarks>
/// Optional, unlike <see cref="IPortablePointHitRegionSource"/>: <c>false</c> means "no override,
/// keep the drawn coverage", never an error. The region is the outer bound WPF itself tests; the
/// pointer path then asks <see cref="PortableHitTestCoreAccepts"/> so a conditional override still
/// decides, exactly as WPF's own hit test would.
/// </remarks>
public interface IPortableHitTestCoreSource
{
    /// <summary>Gets the bounds inside which this visual's own <c>HitTestCore</c> is consulted, in
    /// its own coordinate space. Returns <c>false</c> when the visual's type does not override it.</summary>
    bool TryGetPortableHitTestCoreRegion(out PortableRect rectangle);

    /// <summary>Whether this visual's own <c>HitTestCore</c> accepts the point, given in the
    /// coordinates of <paramref name="rootVisual"/>. Returns <c>true</c> when the point cannot be
    /// mapped into this visual, so an unmappable case keeps the GPU result.</summary>
    bool PortableHitTestCoreAccepts(object rootVisual, double rootX, double rootY);
}
