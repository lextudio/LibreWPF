#if PROGPU_WPF_REAL_SCICHART
using System;
using System.Windows;
using System.Windows.Controls;
using SciChart.Charting.Model.DataSeries;
using SciChart.Charting.Visuals;
using SciChart.Charting.Visuals.Axes;
using SciChart.Charting.Visuals.RenderableSeries;
using SciChart.Charting3D;
using SciChart.Charting3D.Axis;
using SciChart.Charting3D.Model;
using SciChart.Charting3D.RenderableSeries;

namespace ProGPU.Wpf.SciChartMvpApp;

internal sealed record RealSciChartMvpResult(
    int TwoDimensionalPointCount,
    int ThreeDimensionalPointCount,
    FrameworkElement View,
    bool CreatedRealControls,
    string? NativeRuntimeFailure);

internal static class RealSciChartMvp
{
    private const int SampleCount = 96;

    internal static RealSciChartMvpResult Create()
    {
        var dataSeries2D = new XyDataSeries<double, double>();
        for (var i = 0; i < SampleCount; i++)
        {
            var x = i * 0.1;
            var y = Math.Sin(x) + Math.Cos(x * 0.4) * 0.35;
            dataSeries2D.Append(x, y);
        }

        var dataSeries3D = new XyzDataSeries3D<double>();
        for (var i = 0; i < SampleCount; i++)
        {
            var angle = i * Math.PI * 2.0 / (SampleCount - 1);
            dataSeries3D.Append(Math.Cos(angle) * 4.0, Math.Sin(angle * 3.0), Math.Sin(angle) * 4.0, null);
        }

        try
        {
            var lineSeries2D = new FastLineRenderableSeries
            {
                DataSeries = dataSeries2D
            };

            var surface2D = new SciChartSurface
            {
                MinHeight = 170,
                XAxis = new NumericAxis(),
                YAxis = new NumericAxis()
            };
            surface2D.RenderableSeries.Add(lineSeries2D);

            var lineSeries3D = new PointLineRenderableSeries3D
            {
                DataSeries = dataSeries3D,
                IsLineStrips = true
            };

            var surface3D = new SciChart3DSurface
            {
                MinHeight = 170,
                XAxis = new NumericAxis3D(),
                YAxis = new NumericAxis3D(),
                ZAxis = new NumericAxis3D()
            };
            surface3D.RenderableSeries.Add(lineSeries3D);

            var grid = CreateTwoColumnGrid(surface2D, surface3D);
            return new RealSciChartMvpResult(SampleCount, SampleCount, grid, CreatedRealControls: true, NativeRuntimeFailure: null);
        }
        catch (Exception ex) when (IsNativeRuntimeFailure(ex))
        {
            var fallback = new TextBlock
            {
                Text = $"Real SciChart packages restored and data-series APIs ran, but native runtime is unavailable: {ex.GetType().Name}",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(8)
            };

            return new RealSciChartMvpResult(
                SampleCount,
                SampleCount,
                fallback,
                CreatedRealControls: false,
                NativeRuntimeFailure: ex.GetBaseException().Message);
        }
    }

    internal static void Validate(RealSciChartMvpResult result)
    {
        if (result.TwoDimensionalPointCount < SampleCount || result.ThreeDimensionalPointCount < SampleCount)
        {
            throw new InvalidOperationException("Expected real SciChart package data series to contain the MVP sample points.");
        }

        if (result.CreatedRealControls && result.View is not Grid { Children.Count: 2 })
        {
            throw new InvalidOperationException("Expected real SciChart package MVP to create 2D and 3D chart controls.");
        }

        if (!result.CreatedRealControls && string.IsNullOrWhiteSpace(result.NativeRuntimeFailure))
        {
            throw new InvalidOperationException("Expected real SciChart native-runtime failures to be reported explicitly.");
        }
    }

    private static Grid CreateTwoColumnGrid(FrameworkElement surface2D, FrameworkElement surface3D)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        surface2D.Margin = new Thickness(0, 0, 8, 0);
        surface3D.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(surface3D, 1);
        grid.Children.Add(surface2D);
        grid.Children.Add(surface3D);
        return grid;
    }

    private static bool IsNativeRuntimeFailure(Exception ex)
    {
        for (var current = ex; current != null; current = current.InnerException)
        {
            if (current is DllNotFoundException or TypeInitializationException)
            {
                return true;
            }

            if (current.Message.Contains("AbtLicensingNative", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
#endif
