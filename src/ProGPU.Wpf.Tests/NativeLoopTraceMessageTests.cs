using System.Runtime.CompilerServices;
using System.Windows.Media.ProGPU;
using Xunit;

namespace ProGPU.Wpf.Tests;

public class NativeLoopTraceMessageTests
{
    [Fact]
    public void DisabledTraceDoesNotEvaluateStateOrFormatting()
    {
        int reads = 0;
        string ReadState() { reads++; return "state"; }
        var value = new FormattedValue();
        for (int i = 0; i < 1_000; i++)
            Assert.Equal(string.Empty, Render(false, $"loop {ReadState()}, {value}, {42:x}"));
        Assert.Equal(0, reads);
        Assert.Equal(0, value.Formats);
    }

    [Fact]
    public void DisabledTraceDoesNotInvokeThrowingExpressions()
    {
        Assert.Equal(string.Empty, Render(false, $"loop {ThrowIfCalled()}"));
    }

    [Fact]
    public void EnabledTracePreservesTextAndFormatting()
    {
        var value = new FormattedValue();
        Assert.Equal("loop state, value, 2a", Render(true, $"loop {"state"}, {value}, {42:x}"));
        Assert.Equal(1, value.Formats);
        Assert.Equal("literal", Render(true, $"literal"));
    }

    [Fact]
    public void EnabledTraceDoesNotHideExpressionFailures()
    {
        Assert.Throws<InvalidOperationException>(() => Render(true, $"loop {ThrowIfCalled()}"));
    }

    [Theory]
    [InlineData("public void DoEvents()", "public void Close()", 4)]
    [InlineData("private void OnRender(double deltaSeconds)", "private bool Present(", 9)]
    public void HostPumpAndRenderOnlyUseConditionalTraceMessages(string startMarker, string endMarker, int calls)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "src", "ProGPU.Wpf", "ProGpuWpfWindowHost.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        string source = File.ReadAllText(Path.Combine(root.FullName, "src", "ProGPU.Wpf", "ProGpuWpfWindowHost.cs"));
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start);
        string body = source[start..end];
        Assert.Equal(calls, body.Split("TraceNativeLoop(", StringSplitOptions.None).Length - 1);
        Assert.Equal(calls, body.Split("TraceNativeLoop(s_traceNativeLoop,", StringSplitOptions.None).Length - 1);
    }

    private static string Render(bool enabled,
        [InterpolatedStringHandlerArgument(nameof(enabled))] ref NativeLoopTraceMessage message)
        => enabled ? message.GetFormattedText() : string.Empty;

    private static string ThrowIfCalled() => throw new InvalidOperationException("Formatted expression evaluated");

    private sealed class FormattedValue
    {
        internal int Formats { get; private set; }
        public override string ToString() { Formats++; return "value"; }
    }
}
