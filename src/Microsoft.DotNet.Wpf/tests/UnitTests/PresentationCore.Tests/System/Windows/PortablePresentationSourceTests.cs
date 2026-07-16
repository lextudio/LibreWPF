// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Windows.Input;
using System.Windows.Media;

namespace System.Windows;

[Collection("Sequential")]
public class PortablePresentationSourceTests
{
    [Theory]
    [InlineData(1.0, 1.0)]
    [InlineData(2.0, 2.0)]
    public void ClientOriginParticipatesInScreenConversions(double dpiScaleX, double dpiScaleY)
    {
        using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create(dpiScaleX, dpiScaleY);
        var root = new DrawingVisual();
        source.RootVisual = root;
        source.SetClientSize(400.0, 300.0);
        source.SetClientOrigin(120.0, 80.0);

        Point screenPoint = root.PointToScreen(new Point(5.0, 7.0));

        screenPoint.X.Should().BeApproximately(125.0, 0.000001);
        screenPoint.Y.Should().BeApproximately(87.0, 0.000001);
        Point clientPoint = root.PointFromScreen(screenPoint);
        clientPoint.X.Should().BeApproximately(5.0, 0.000001);
        clientPoint.Y.Should().BeApproximately(7.0, 0.000001);
        CompositionTarget compositionTarget = ((PresentationSource)source).CompositionTarget;
        compositionTarget.TransformToDevice.M11.Should().BeApproximately(dpiScaleX, 0.000001);
        compositionTarget.TransformToDevice.M22.Should().BeApproximately(dpiScaleY, 0.000001);
    }

    [Fact]
    public void NonFiniteClientOriginFallsBackToZero()
    {
        using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create();
        var root = new DrawingVisual();
        source.RootVisual = root;
        source.SetClientSize(100.0, 100.0);
        source.SetClientOrigin(double.NaN, double.PositiveInfinity);

        root.PointToScreen(new Point()).Should().Be(new Point());
    }

    [Fact]
    public void PortableDragSourceDoesNotInvokeOleServices()
    {
        using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create();
        var root = new HitTestElement();
        source.RootVisual = root;
        source.SetClientSize(200.0, 100.0);

        int startedCount = 0;
        int completedCount = 0;
        root.AddHandler(DragDrop.DragDropStartedEvent, new RoutedEventHandler((_, _) => startedCount++));
        root.AddHandler(DragDrop.DragDropCompletedEvent, new RoutedEventHandler((_, _) => completedCount++));

        DragDropEffects result = DragDrop.DoDragDrop(
            root,
            "portable text",
            DragDropEffects.Copy | DragDropEffects.Move);

        result.Should().Be(DragDropEffects.None);
        startedCount.Should().Be(1);
        completedCount.Should().Be(1);
    }

    [StaFact]
    public void ReleaseMouseCaptureReportsOnlyCancelCaptureWithoutMovingThePointer()
    {
        using IPortablePresentationSourceHost source = PortablePresentationSourceHost.Create();
        var presentationSource = (PresentationSource)source;
        var root = new HitTestElement();
        source.RootVisual = root;
        source.SetClientSize(200.0, 100.0);

        ReportMouseInput(
            presentationSource,
            RawMouseActions.Activate | RawMouseActions.AbsoluteMove,
            x: 37,
            y: 41);

        root.CaptureMouse().Should().BeTrue();
        Mouse.Captured.Should().BeSameAs(root);

        var releaseReports = new List<RawMouseInputReport>();
        int mouseMoveCount = 0;
        int lostMouseCaptureCount = 0;
        PreProcessInputEventHandler inputHandler = (_, e) =>
        {
            if (e.StagingItem.Input is InputReportEventArgs inputReport &&
                inputReport.Report is RawMouseInputReport mouseReport &&
                ReferenceEquals(mouseReport.InputSource, presentationSource))
            {
                releaseReports.Add(mouseReport);
            }
        };

        root.MouseMove += (_, _) => mouseMoveCount++;
        root.LostMouseCapture += (_, _) => lostMouseCaptureCount++;
        InputManager.Current.PreProcessInput += inputHandler;
        try
        {
            root.ReleaseMouseCapture();
        }
        finally
        {
            InputManager.Current.PreProcessInput -= inputHandler;
        }

        releaseReports.Should().Contain(report =>
            report.Actions == RawMouseActions.CancelCapture);
        releaseReports.Should().NotContain(report =>
            (report.Actions & RawMouseActions.Activate) != 0);
        releaseReports.Should().NotContain(report =>
            (report.Actions & RawMouseActions.AbsoluteMove) != 0 &&
            report.X == 0 &&
            report.Y == 0);
        mouseMoveCount.Should().Be(0);
        lostMouseCaptureCount.Should().Be(1);
        Mouse.Captured.Should().BeNull();
        root.IsMouseCaptured.Should().BeFalse();
    }

    [StaFact]
    public void DeactivatingPortableSourceClearsCaptureAfterActiveSourceChanges()
    {
        using IPortablePresentationSourceHost firstSource = PortablePresentationSourceHost.Create();
        using IPortablePresentationSourceHost secondSource = PortablePresentationSourceHost.Create();
        var firstPresentationSource = (PresentationSource)firstSource;
        var secondPresentationSource = (PresentationSource)secondSource;
        var firstRoot = new HitTestElement();
        var secondRoot = new HitTestElement();
        firstSource.RootVisual = firstRoot;
        secondSource.RootVisual = secondRoot;
        firstSource.SetClientSize(200.0, 100.0);
        secondSource.SetClientSize(200.0, 100.0);

        ReportMouseInput(
            firstPresentationSource,
            RawMouseActions.Activate | RawMouseActions.AbsoluteMove,
            x: 37,
            y: 41);
        Mouse.Capture(firstRoot, CaptureMode.SubTree).Should().BeTrue();
        Mouse.Captured.Should().BeSameAs(firstRoot);

        var firstSourceReports = new List<RawMouseInputReport>();
        int lostMouseCaptureCount = 0;
        PreProcessInputEventHandler inputHandler = (_, e) =>
        {
            if (e.StagingItem.Input is InputReportEventArgs inputReport &&
                inputReport.Report is RawMouseInputReport mouseReport &&
                ReferenceEquals(mouseReport.InputSource, firstPresentationSource))
            {
                firstSourceReports.Add(mouseReport);
            }
        };
        firstRoot.LostMouseCapture += (_, _) => lostMouseCaptureCount++;
        InputManager.Current.PreProcessInput += inputHandler;
        try
        {
            ReportMouseInput(
                secondPresentationSource,
                RawMouseActions.Activate | RawMouseActions.AbsoluteMove,
                x: 37,
                y: 41);
        }
        finally
        {
            InputManager.Current.PreProcessInput -= inputHandler;
        }

        Mouse.PrimaryDevice.ActiveSource.Should().BeSameAs(secondPresentationSource);
        Mouse.Captured.Should().BeNull();
        firstRoot.IsMouseCaptured.Should().BeFalse();
        lostMouseCaptureCount.Should().Be(1);
        firstSourceReports.Should().Contain(report => report.Actions == RawMouseActions.CancelCapture);
        firstSourceReports.Should().NotContain(report => (report.Actions & RawMouseActions.Activate) != 0);
    }

    [StaFact]
    public void CancelCaptureFromDifferentProviderDoesNotClearCapture()
    {
        using IPortablePresentationSourceHost firstSource = PortablePresentationSourceHost.Create();
        using IPortablePresentationSourceHost secondSource = PortablePresentationSourceHost.Create();
        var firstPresentationSource = (PresentationSource)firstSource;
        var secondPresentationSource = (PresentationSource)secondSource;
        var firstRoot = new HitTestElement();
        var secondRoot = new HitTestElement();
        firstSource.RootVisual = firstRoot;
        secondSource.RootVisual = secondRoot;
        firstSource.SetClientSize(200.0, 100.0);
        secondSource.SetClientSize(200.0, 100.0);

        ReportMouseInput(
            firstPresentationSource,
            RawMouseActions.Activate | RawMouseActions.AbsoluteMove,
            x: 37,
            y: 41);
        Mouse.Capture(firstRoot, CaptureMode.SubTree).Should().BeTrue();
        Mouse.Captured.Should().BeSameAs(firstRoot);

        int lostMouseCaptureCount = 0;
        firstRoot.LostMouseCapture += (_, _) => lostMouseCaptureCount++;
        try
        {
            ReportMouseInput(
                secondPresentationSource,
                RawMouseActions.CancelCapture,
                x: 0,
                y: 0);

            Mouse.PrimaryDevice.ActiveSource.Should().BeSameAs(firstPresentationSource);
            Mouse.Captured.Should().BeSameAs(firstRoot);
            firstRoot.IsMouseCaptured.Should().BeTrue();
            lostMouseCaptureCount.Should().Be(0);
        }
        finally
        {
            firstRoot.ReleaseMouseCapture();
        }
    }

    private static void ReportMouseInput(
        PresentationSource source,
        RawMouseActions actions,
        int x,
        int y)
    {
        var report = new RawMouseInputReport(
            InputMode.Foreground,
            Environment.TickCount,
            source,
            actions,
            x,
            y,
            wheel: 0,
            extraInformation: IntPtr.Zero);
        var input = new InputReportEventArgs(inputDevice: null, report: report)
        {
            RoutedEvent = InputManager.PreviewInputReportEvent
        };

        InputManager.Current.ProcessInput(input);
    }

    private sealed class HitTestElement : UIElement
    {
        protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
        {
            return new PointHitTestResult(this, hitTestParameters.HitPoint);
        }
    }
}
