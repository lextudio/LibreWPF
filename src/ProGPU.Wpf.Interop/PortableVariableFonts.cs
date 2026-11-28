namespace ProGPU.Wpf.Interop;

/// <summary>
/// Produces static weight instances of variable fonts. The portable text pipeline shapes and
/// renders from font bytes with no per-run variation state, so weights are exposed to WPF as
/// ordinary static faces generated from the variable font.
/// </summary>
public interface IPortableVariableFontInstancer
{
    /// <summary>Reads the weight axis of a variable TrueType font file; false when it has none.</summary>
    bool TryGetWeightRange(string fontPath, out int minimum, out int maximum, out int defaultWeight);

    /// <summary>Returns a static font file image at the requested weight (user-space wght value).</summary>
    byte[] CreateWeightInstance(byte[] fontData, int weight);
}

public static partial class PortableWpfServiceRegistry
{
    private static readonly PortableDefaultServiceSlot<IPortableVariableFontInstancer> s_variableFontInstancer = new();

    public static bool TryGetVariableFontInstancer(out IPortableVariableFontInstancer service)
    {
        service = s_variableFontInstancer.Current!;
        return service != null;
    }

    /// <summary>Replaces the explicit override; disposal reveals the installed default, if any.</summary>
    public static IDisposable RegisterVariableFontInstancer(IPortableVariableFontInstancer service) => s_variableFontInstancer.Register(service);

    /// <summary>Installs a default without overriding an explicit provider; performs no font work.</summary>
    public static void EnsureVariableFontInstancer(IPortableVariableFontInstancer service) => s_variableFontInstancer.EnsureDefault(service);
}
