// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace System.Windows.Media
{
    internal sealed class ObjectRenderDataDrawingContextSink : IRenderDataDrawingContextSink
    {
        private const BindingFlags MethodFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        private readonly object _sink;
        private readonly Dictionary<string, MethodInfo> _methods = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);

        internal ObjectRenderDataDrawingContextSink(object sink)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        void IRenderDataDrawingContextSink.DrawLine(Pen pen, Point point0, Point point1)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.DrawLine), pen, point0, point1);
        }

        void IRenderDataDrawingContextSink.DrawLine(Pen pen, Point point0, AnimationClock point0Animations, Point point1, AnimationClock point1Animations)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.DrawLine), pen, point0, point0Animations, point1, point1Animations);
        }

        void IRenderDataDrawingContextSink.DrawRectangle(Brush brush, Pen pen, Rect rectangle)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.DrawRectangle), brush, pen, rectangle);
        }

        void IRenderDataDrawingContextSink.DrawRectangle(Brush brush, Pen pen, Rect rectangle, AnimationClock rectangleAnimations)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.DrawRectangle), brush, pen, rectangle, rectangleAnimations);
        }

        void IRenderDataDrawingContextSink.DrawRoundedRectangle(Brush brush, Pen pen, Rect rectangle, double radiusX, double radiusY)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.DrawRoundedRectangle), brush, pen, rectangle, radiusX, radiusY);
        }

        void IRenderDataDrawingContextSink.DrawRoundedRectangle(
            Brush brush,
            Pen pen,
            Rect rectangle,
            AnimationClock rectangleAnimations,
            double radiusX,
            AnimationClock radiusXAnimations,
            double radiusY,
            AnimationClock radiusYAnimations)
        {
            Invoke(
                nameof(IRenderDataDrawingContextSink.DrawRoundedRectangle),
                brush,
                pen,
                rectangle,
                rectangleAnimations,
                radiusX,
                radiusXAnimations,
                radiusY,
                radiusYAnimations);
        }

        void IRenderDataDrawingContextSink.DrawEllipse(Brush brush, Pen pen, Point center, double radiusX, double radiusY)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.DrawEllipse), brush, pen, center, radiusX, radiusY);
        }

        void IRenderDataDrawingContextSink.DrawEllipse(
            Brush brush,
            Pen pen,
            Point center,
            AnimationClock centerAnimations,
            double radiusX,
            AnimationClock radiusXAnimations,
            double radiusY,
            AnimationClock radiusYAnimations)
        {
            Invoke(
                nameof(IRenderDataDrawingContextSink.DrawEllipse),
                brush,
                pen,
                center,
                centerAnimations,
                radiusX,
                radiusXAnimations,
                radiusY,
                radiusYAnimations);
        }

        void IRenderDataDrawingContextSink.DrawGeometry(Brush brush, Pen pen, Geometry geometry)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.DrawGeometry), brush, pen, geometry);
        }

        void IRenderDataDrawingContextSink.DrawImage(ImageSource imageSource, Rect rectangle)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.DrawImage), imageSource, rectangle);
        }

        void IRenderDataDrawingContextSink.DrawImage(ImageSource imageSource, Rect rectangle, AnimationClock rectangleAnimations)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.DrawImage), imageSource, rectangle, rectangleAnimations);
        }

        void IRenderDataDrawingContextSink.DrawGlyphRun(Brush foregroundBrush, GlyphRun glyphRun)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.DrawGlyphRun), foregroundBrush, glyphRun);
        }

        void IRenderDataDrawingContextSink.DrawDrawing(Drawing drawing)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.DrawDrawing), drawing);
        }

        void IRenderDataDrawingContextSink.DrawVideo(MediaPlayer player, Rect rectangle)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.DrawVideo), player, rectangle);
        }

        void IRenderDataDrawingContextSink.DrawVideo(MediaPlayer player, Rect rectangle, AnimationClock rectangleAnimations)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.DrawVideo), player, rectangle, rectangleAnimations);
        }

        void IRenderDataDrawingContextSink.PushClip(Geometry clipGeometry)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.PushClip), clipGeometry);
        }

        void IRenderDataDrawingContextSink.PushOpacityMask(Brush opacityMask)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.PushOpacityMask), opacityMask);
        }

        void IRenderDataDrawingContextSink.PushOpacity(double opacity)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.PushOpacity), opacity);
        }

        void IRenderDataDrawingContextSink.PushOpacity(double opacity, AnimationClock opacityAnimations)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.PushOpacity), opacity, opacityAnimations);
        }

        void IRenderDataDrawingContextSink.PushTransform(Transform transform)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.PushTransform), transform);
        }

        void IRenderDataDrawingContextSink.PushGuidelineSet(GuidelineSet guidelines)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.PushGuidelineSet), guidelines);
        }

        void IRenderDataDrawingContextSink.PushGuidelineY1(double coordinate)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.PushGuidelineY1), coordinate);
        }

        void IRenderDataDrawingContextSink.PushGuidelineY2(double leadingCoordinate, double offsetToDrivenCoordinate)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.PushGuidelineY2), leadingCoordinate, offsetToDrivenCoordinate);
        }

        [Obsolete(MS.Internal.Media.VisualTreeUtils.BitmapEffectObsoleteMessage)]
        void IRenderDataDrawingContextSink.PushEffect(BitmapEffect effect, BitmapEffectInput effectInput)
        {
            Invoke(nameof(IRenderDataDrawingContextSink.PushEffect), effect, effectInput);
        }

        void IRenderDataDrawingContextSink.Pop()
        {
            Invoke(nameof(IRenderDataDrawingContextSink.Pop));
        }

        void IRenderDataDrawingContextSink.Close()
        {
            Invoke(nameof(IRenderDataDrawingContextSink.Close));
        }

        private void Invoke(string methodName, params object[] args)
        {
            MethodInfo method = GetMethod(methodName, args.Length);

            try
            {
                method.Invoke(_sink, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                throw ex.InnerException;
            }
        }

        private MethodInfo GetMethod(string methodName, int parameterCount)
        {
            string key = methodName + ":" + parameterCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (_methods.TryGetValue(key, out MethodInfo method))
            {
                return method;
            }

            foreach (MethodInfo candidate in _sink.GetType().GetMethods(MethodFlags))
            {
                if (candidate.Name == methodName && candidate.GetParameters().Length == parameterCount)
                {
                    _methods.Add(key, candidate);
                    return candidate;
                }
            }

            throw new MissingMethodException(
                _sink.GetType().FullName,
                methodName + "(" + parameterCount.ToString(System.Globalization.CultureInfo.InvariantCulture) + " args)");
        }
    }
}
