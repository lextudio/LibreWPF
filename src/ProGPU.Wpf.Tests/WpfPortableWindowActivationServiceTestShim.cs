using System;

namespace System.Windows;

internal interface IPortableWindowActivationServiceTestTarget
{
}

internal static class PortableWindowActivationService
{
    public static int DropCount { get; private set; }

    public static int LastKind { get; private set; }

    public static string[] LastFiles { get; private set; } = Array.Empty<string>();

    public static string? LastText { get; private set; }

    public static double LastX { get; private set; }

    public static double LastY { get; private set; }

    public static int LastAllowedEffects { get; private set; }

    public static int LastAcceptedEffect { get; private set; }

    public static void Reset()
    {
        DropCount = 0;
        LastKind = 0;
        LastFiles = Array.Empty<string>();
        LastText = null;
        LastX = 0;
        LastY = 0;
        LastAllowedEffects = 0;
        LastAcceptedEffect = 0;
    }

    internal static int ProcessDragDrop(
        IPortableWindowActivationServiceTestTarget window,
        string[] files,
        string text,
        double x,
        double y,
        int allowedEffects,
        int acceptedEffect)
    {
        return ProcessDragDropEvent(
            window,
            dragDropEventKind: 0,
            files,
            text,
            x,
            y,
            allowedEffects,
            acceptedEffect);
    }

    internal static int ProcessDragDropEvent(
        IPortableWindowActivationServiceTestTarget window,
        int dragDropEventKind,
        string[] files,
        string text,
        double x,
        double y,
        int allowedEffects,
        int acceptedEffect)
    {
        DropCount++;
        LastKind = dragDropEventKind;
        LastFiles = files;
        LastText = text;
        LastX = x;
        LastY = y;
        LastAllowedEffects = allowedEffects;
        LastAcceptedEffect = acceptedEffect;
        return (int)Media.ProGPU.Platform.WpfDragDropEffects.Move;
    }
}
