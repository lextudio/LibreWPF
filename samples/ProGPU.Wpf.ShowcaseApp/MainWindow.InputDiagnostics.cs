using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.ProGPU;

namespace ProGPU.Wpf.ShowcaseApp;

public partial class MainWindow
{
    // Only the final failed attempt reads these values. Diagnostics neither
    // repair layout nor inject input, and cannot turn a failed gate into a pass.
    private string DescribeLiveThumbHitFailure(Thumb target)
    {
        try
        {
            return DescribeLiveThumbHitFailureCore(target);
        }
        catch (Exception error)
        {
            return $", ThumbDiagnosticError={error.Message}";
        }
    }

    private string DescribeLiveThumbHitFailureCore(Thumb target)
    {
        Point center = target.TranslatePoint(
            new Point(Math.Max(1.0, target.ActualWidth) / 2.0, Math.Max(1.0, target.ActualHeight) / 2.0),
            this);
        object? hit = InputHitTest(center);
        PresentationSource? targetSource = PresentationSource.FromVisual(target);
        PresentationSource? windowSource = PresentationSource.FromVisual(this);
        var tabs = FindName("ShowcaseTabControl") as TabControl;
        return $", SelectedTab={tabs?.SelectedIndex}, " +
            $"TargetSourcePresent={targetSource is not null}, WindowSourcePresent={windowSource is not null}, " +
            $"SameSource={targetSource is not null && ReferenceEquals(targetSource, windowSource)}, " +
            $"TargetPath=[{DescribeLiveInputVisualPath(target, center)}], " +
            $"HitPath=[{DescribeLiveInputVisualPath(hit as DependencyObject, center)}], " +
            $"TemplatePresent={target.Template is not null}, " +
            $"TargetSubtree=[{DescribeLiveThumbSubtree(target)}], " +
            $"PostFailureGpuQuery=[{DescribeLiveThumbGpuOwners(target, center)}]";
    }

    private string DescribeLiveInputVisualPath(DependencyObject? element, Point rootPoint)
    {
        var result = new StringBuilder();
        DependencyObject? current = element;
        for (int depth = 0; current is not null && depth < 64; depth++)
        {
            if (depth != 0)
                result.Append(" <- ");

            string kind = current switch
            {
                Window => "Window",
                Thumb => "Thumb",
                ScrollViewer => "ScrollViewer",
                ContentPresenter => "ContentPresenter",
                Border => "Border",
                Panel => "Panel",
                FrameworkElement => "FrameworkElement",
                UIElement => "UIElement",
                _ => "DependencyObject"
            };
            result.Append(kind).Append('(').Append(DescribeInputElement(current));
            if (current is FrameworkElement frameworkElement)
            {
                result.Append($", Size={frameworkElement.RenderSize}, " +
                    $"Visible={frameworkElement.IsVisible}, HitVisible={frameworkElement.IsHitTestVisible}, " +
                    $"MeasureValid={frameworkElement.IsMeasureValid}, ArrangeValid={frameworkElement.IsArrangeValid}, " +
                    $"TemplateOwner={DescribeInputElement(frameworkElement.TemplatedParent)}");
            }

            if (current is Visual visual)
            {
                Geometry? clip = VisualTreeHelper.GetClip(visual);
                result.Append($", Offset={VisualTreeHelper.GetOffset(visual)}, " +
                    $"Transform={VisualTreeHelper.GetTransform(visual)?.Value}, " +
                    $"Clip={(clip is null ? "none" : clip is RectangleGeometry rectangle ? rectangle.Rect.ToString() : "nonrectangular")}");
                if (clip is not null)
                {
                    try
                    {
                        Point localPoint = TransformToDescendant(visual).Transform(rootPoint);
                        result.Append($", PointInClipVisual={localPoint}, ClipTransform={clip.Transform?.Value}, " +
                            $"SourceClipContainsPoint={clip.FillContains(localPoint)}");
                    }
                    catch (Exception error)
                    {
                        result.Append($", ClipDiagnosticError={error.Message}");
                    }
                }
            }

            result.Append(')');
            if (ReferenceEquals(current, this))
            {
                result.Append(" ROOT");
                return result.ToString();
            }

            current = current is Visual
                ? VisualTreeHelper.GetParent(current)
                : current is FrameworkContentElement contentElement ? contentElement.Parent : null;
        }

        result.Append(current is null ? " DISCONNECTED" : " TRUNCATED");
        return result.ToString();
    }

    private static string DescribeLiveThumbSubtree(Thumb target)
    {
        var result = new StringBuilder();
        var pending = new Stack<(Visual Visual, int Depth)>();
        pending.Push((target, 0));
        int remaining = 64;
        try
        {
            while (pending.Count != 0 && remaining-- > 0)
            {
                var (visual, depth) = pending.Pop();
                int children = VisualTreeHelper.GetChildrenCount(visual);
                DrawingGroup? drawing = VisualTreeHelper.GetDrawing(visual);
                result.Append($"{{Depth={depth}, Name={DescribeInputElement(visual)}, Children={children}, " +
                    $"DrawingPresent={drawing is not null}, DrawingChildren={drawing?.Children.Count}");
                if (visual is FrameworkElement element)
                    result.Append($", Size={element.RenderSize}, TemplateOwner={DescribeInputElement(element.TemplatedParent)}");
                if (visual is Border border)
                    result.Append($", BorderBackgroundPresent={border.Background is not null}, " +
                        $"BackgroundOpacity={border.Background?.Opacity}, BorderThickness={border.BorderThickness}, CornerRadius={border.CornerRadius}");
                result.Append('}');
                if (depth >= 8)
                {
                    if (children != 0)
                        result.Append(" DEPTH_TRUNCATED");
                    continue;
                }

                int admitted = Math.Min(children, Math.Max(0, remaining - pending.Count));
                if (admitted != children)
                    result.Append(" CHILDREN_TRUNCATED");
                for (int index = admitted - 1; index >= 0; index--)
                {
                    if (VisualTreeHelper.GetChild(visual, index) is Visual child)
                        pending.Push((child, depth + 1));
                }
            }

            if (pending.Count != 0)
                result.Append(" NODE_TRUNCATED");
        }
        catch (Exception error)
        {
            result.Append($" SubtreeDiagnosticError={error.Message}");
        }

        return result.ToString();
    }

    private string DescribeLiveThumbGpuOwners(Thumb target, Point center)
    {
        // This additional query is diagnostic only, after the unchanged final
        // failed attempt. The existing query seam may refresh its device index;
        // before/after frame records prevent treating it as the original query.
        try
        {
            if (!ProGpuWpfDiagnostics.TryGetWindowHost(this, out var host) || host is null)
                return "HostUnavailable";
            string before = FormatLivePresentedFrameState(ReadLivePresentedFrameState(host));
            bool hasCache = ProGpuWpfDiagnostics.TryGetGpuHitTestCacheSnapshot(this, out var cache);
            object?[] owners = new object?[128];
            bool query = ProGpuWpfDiagnostics.TryHitTestOwners(this, center.X, center.Y, owners.AsSpan(), out int count);
            var result = new StringBuilder($"RequestedRenderer={AppContext.GetData("LibreWPF.RequestedRendererMode")}, " +
                $"NativeSessionPresent={host.LastNativeMilSessionFrame is not null}, RootIsWindow={ReferenceEquals(host.WpfRootVisual, this)}, " +
                $"FrameBefore={before}, HasCache={hasCache}, Cache={cache}, QuerySucceeded={query}, Count={count}, AtCapacity={count == owners.Length}");
            for (int index = 0; index < Math.Min(count, owners.Length); index++)
            {
                object? owner = owners[index];
                bool withinTarget = owner is DependencyObject dependencyObject &&
                    (ReferenceEquals(dependencyObject, target) || target.IsAncestorOf(dependencyObject));
                result.Append($"; Owner[{index}]={DescribeInputElement(owner)}, WithinTarget={withinTarget}");
                if (owner is FrameworkElement element)
                    result.Append($", TemplateOwner={DescribeInputElement(element.TemplatedParent)}, Size={element.RenderSize}");
            }

            result.Append($"; FrameAfter={FormatLivePresentedFrameState(ReadLivePresentedFrameState(host))}");
            return result.ToString();
        }
        catch (Exception error)
        {
            // Never replace the original assertion with a diagnostic failure.
            return $"GpuDiagnosticError={error.Message}";
        }
    }
}
