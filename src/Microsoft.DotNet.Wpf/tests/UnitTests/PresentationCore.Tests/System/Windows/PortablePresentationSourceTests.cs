// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

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

        screenPoint.X.Should().BeApproximately(120.0 + 5.0 * dpiScaleX, 0.000001);
        screenPoint.Y.Should().BeApproximately(80.0 + 7.0 * dpiScaleY, 0.000001);
        Point clientPoint = root.PointFromScreen(screenPoint);
        clientPoint.X.Should().BeApproximately(5.0, 0.000001);
        clientPoint.Y.Should().BeApproximately(7.0, 0.000001);
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
}
