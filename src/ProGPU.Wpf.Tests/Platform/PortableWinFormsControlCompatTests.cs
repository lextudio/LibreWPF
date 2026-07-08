using System.ComponentModel;
using System.Linq;
using System.Windows.Forms;
using Xunit;

namespace ProGPU.Wpf.Tests.Platform;

public sealed class PortableWinFormsControlCompatTests
{
    [Fact]
    public void PictureBoxSupportsDesignerInitialization()
    {
        var pictureBox = new PictureBox();

        var initializer = Assert.IsAssignableFrom<ISupportInitialize>(pictureBox);
        initializer.BeginInit();
        pictureBox.SizeMode = PictureBoxSizeMode.Zoom;
        initializer.EndInit();

        Assert.Equal(PictureBoxSizeMode.Zoom, pictureBox.SizeMode);
    }

    [Fact]
    public void ErrorProviderSupportsDesignerGeneratedErrorState()
    {
        using var container = new Container();
        using var textBox = new TextBox();
        using var provider = new ErrorProvider(container);

        var initializer = Assert.IsAssignableFrom<ISupportInitialize>(provider);
        initializer.BeginInit();
        provider.BlinkStyle = ErrorBlinkStyle.NeverBlink;
        provider.SetIconAlignment(textBox, ErrorIconAlignment.MiddleLeft);
        provider.SetIconPadding(textBox, 6);
        provider.SetError(textBox, "Name is required.");
        initializer.EndInit();

        Assert.Contains(provider, container.Components.Cast<object>());
        Assert.True(((IExtenderProvider)provider).CanExtend(textBox));
        Assert.Equal(ErrorBlinkStyle.NeverBlink, provider.BlinkStyle);
        Assert.Equal(ErrorIconAlignment.MiddleLeft, provider.GetIconAlignment(textBox));
        Assert.Equal(6, provider.GetIconPadding(textBox));
        Assert.Equal("Name is required.", provider.GetError(textBox));

        provider.SetError(textBox, string.Empty);

        Assert.Equal(string.Empty, provider.GetError(textBox));
    }
}
