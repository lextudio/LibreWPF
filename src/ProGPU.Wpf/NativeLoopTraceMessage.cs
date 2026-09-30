using System.Runtime.CompilerServices;

namespace System.Windows.Media.ProGPU;

// Skip formatted expressions entirely when tracing is disabled. A check inside
// a string-taking logger cannot avoid their work or allocations.
[InterpolatedStringHandler]
internal ref struct NativeLoopTraceMessage
{
    private DefaultInterpolatedStringHandler _message;

    public NativeLoopTraceMessage(int literalLength, int formattedCount,
        bool enabled, out bool shouldAppend)
    {
        shouldAppend = enabled;
        _message = enabled ? new(literalLength, formattedCount) : default;
    }

    public void AppendLiteral(string value) => _message.AppendLiteral(value);
    public void AppendFormatted<T>(T value) => _message.AppendFormatted(value);
    public void AppendFormatted<T>(T value, string? format) => _message.AppendFormatted(value, format);
    internal string GetFormattedText() => _message.ToStringAndClear();
}
