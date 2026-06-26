using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ProGPU.Wpf.SciChartMvpApp;

public partial class MainWindow : Window
{
    private SciChartMvpRenderResult? _lastRenderResult;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        RenderMvpScene();
    }

    private void OnRenderButtonClick(object sender, RoutedEventArgs e)
    {
        RenderMvpScene();
    }

    internal void ValidateRenderedChart()
    {
        if (_lastRenderResult == null || ChartImage.Source == null)
        {
            RenderMvpScene();
        }

        SciChartMvpSelfTest.Validate(_lastRenderResult ?? throw new InvalidOperationException("SciChart MVP renderer did not produce a result."));
        if (ChartImage.Source is not BitmapSource bitmap || bitmap.PixelWidth <= 0 || bitmap.PixelHeight <= 0)
        {
            throw new InvalidOperationException("Expected SciChart MVP Image.Source to hold a populated BitmapSource.");
        }

#if PROGPU_WPF_REAL_SCICHART
        ValidateRealSciChartPackageSurface();
#endif
    }

    private void RenderMvpScene()
    {
        try
        {
            var result = SciChartMvpRenderer.Render();
            _lastRenderResult = result;
            ChartImage.Source = CreateBitmap(result);
            StatusText.Text = $"Rendered {result.Width}x{result.Height} ProGPU SciChart MVP surface";
            DrawCountText.Text = $"Draws: {result.SubmittedDrawCount}";
            BackendText.Text = result.BackendSummary;
#if PROGPU_WPF_REAL_SCICHART
            AttachRealSciChartPackageSurface();
#endif
        }
        catch (Exception ex)
        {
            StatusText.Text = ex.Message;
            throw;
        }
    }

    private static BitmapSource CreateBitmap(SciChartMvpRenderResult result)
    {
        var bitmap = new WriteableBitmap(
            result.Width,
            result.Height,
            96,
            96,
            PixelFormats.Pbgra32,
            null);
        bitmap.WritePixels(
            new Int32Rect(0, 0, result.Width, result.Height),
            result.Pbgra32Pixels,
            checked(result.Width * 4),
            0);
        bitmap.Freeze();
        return bitmap;
    }

#if PROGPU_WPF_REAL_SCICHART
    private RealSciChartMvpResult? _realSciChartResult;

    private void AttachRealSciChartPackageSurface()
    {
        _realSciChartResult ??= RealSciChartMvp.Create();
        RealSciChartMvp.Validate(_realSciChartResult);
        RealSciChartLabel.Visibility = Visibility.Visible;
        RealSciChartHost.Visibility = Visibility.Visible;
        RealSciChartHost.Content = _realSciChartResult.View;
        if (!_realSciChartResult.CreatedRealControls)
        {
            BackendText.Text = $"Real SciChart native runtime unavailable: {_realSciChartResult.NativeCompatibilitySummary}; exports: {_realSciChartResult.NativeExportSummary}; resolver: {_realSciChartResult.NativeResolverSummary}";
        }
    }

    private void ValidateRealSciChartPackageSurface()
    {
        AttachRealSciChartPackageSurface();
        if (RealSciChartHost.Content is not FrameworkElement view || view.Parent != RealSciChartHost)
        {
            throw new InvalidOperationException("Expected real SciChart package controls to be hosted by the MVP sample.");
        }
    }
#else
    private void AttachRealSciChartPackageSurface()
    {
        RealSciChartLabel.Visibility = Visibility.Collapsed;
        RealSciChartHost.Visibility = Visibility.Collapsed;
        RealSciChartHost.Content = null;
    }
#endif
}
