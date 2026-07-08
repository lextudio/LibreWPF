using System.ComponentModel;
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
}
