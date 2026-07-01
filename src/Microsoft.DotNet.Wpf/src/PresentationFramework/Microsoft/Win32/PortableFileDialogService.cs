// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using System.Threading;
using ProGPU.Wpf.Interop;

namespace Microsoft.Win32
{
    internal static class PortableFileDialogService
    {
        private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static readonly FileDialogServiceRegistrar s_registrar = new FileDialogServiceRegistrar();
        private static IDisposable s_registrarRegistration;
        private static Func<PortableFileDialogRequest, string> s_showDialog;

        internal static bool IsEnabled
        {
            get
            {
                return !s_isWindows;
            }
        }

        internal static void RegisterPortableInteropService()
        {
            s_registrarRegistration ??= PortableWpfServiceRegistry.RegisterFileDialogService(s_registrar);
        }

        internal static IDisposable Register(Func<object, string> showDialog)
        {
            ArgumentNullException.ThrowIfNull(showDialog);

            return Register((Func<PortableFileDialogRequest, string>)(request => showDialog(request)));
        }

        internal static IDisposable Register(Func<PortableFileDialogRequest, string> showDialog)
        {
            ArgumentNullException.ThrowIfNull(showDialog);

            if (s_isWindows)
            {
                return EmptyRegistration.Instance;
            }

            Volatile.Write(ref s_showDialog, showDialog);
            return new Registration(showDialog);
        }

        internal static void Clear()
        {
            Volatile.Write(ref s_showDialog, null);
        }

        internal static bool TryShowDialog(CommonItemDialog dialog, out string selectedPath)
        {
            selectedPath = null;
            if (s_isWindows)
            {
                return false;
            }

            Func<PortableFileDialogRequest, string> showDialog = Volatile.Read(ref s_showDialog);
            if (showDialog == null)
            {
                return true;
            }

            selectedPath = showDialog(new PortableFileDialogRequest(dialog));
            return true;
        }

        internal sealed class PortableFileDialogRequest
        {
            internal PortableFileDialogRequest(CommonItemDialog dialog)
            {
                Kind = GetKind(dialog);
                Title = dialog.Title;
                InitialDirectory = dialog.InitialDirectory;
                DefaultDirectory = dialog.DefaultDirectory;
                SuggestedItemName = GetSuggestedItemName(dialog);

                if (dialog is FileDialog fileDialog)
                {
                    DefaultExtension = fileDialog.DefaultExt;
                    Filter = fileDialog.Filter;
                    FilterIndex = fileDialog.FilterIndex;
                }
                else
                {
                    DefaultExtension = string.Empty;
                    Filter = string.Empty;
                    FilterIndex = 1;
                }
            }

            public string Kind { get; }

            public string Title { get; }

            public string InitialDirectory { get; }

            public string DefaultDirectory { get; }

            public string SuggestedItemName { get; }

            public string DefaultExtension { get; }

            public string Filter { get; }

            public int FilterIndex { get; }

            private static string GetKind(CommonItemDialog dialog)
            {
                if (dialog is SaveFileDialog)
                {
                    return "SaveFile";
                }

                if (dialog is OpenFolderDialog)
                {
                    return "PickFolder";
                }

                return "OpenFile";
            }

            private static string GetSuggestedItemName(CommonItemDialog dialog)
            {
                if (dialog is OpenFolderDialog folderDialog)
                {
                    return folderDialog.FolderName;
                }

                if (dialog is FileDialog fileDialog)
                {
                    return fileDialog.FileName;
                }

                return string.Empty;
            }
        }

        private static ProGPU.Wpf.Interop.PortableFileDialogRequest CreateInteropRequest(
            PortableFileDialogRequest request)
        {
            return new ProGPU.Wpf.Interop.PortableFileDialogRequest(
                request.Kind,
                request.Title,
                request.InitialDirectory,
                request.DefaultDirectory,
                request.SuggestedItemName,
                request.DefaultExtension,
                request.Filter,
                request.FilterIndex);
        }

        private sealed class Registration : IDisposable
        {
            private Func<PortableFileDialogRequest, string> _showDialog;

            internal Registration(Func<PortableFileDialogRequest, string> showDialog)
            {
                _showDialog = showDialog;
            }

            public void Dispose()
            {
                Func<PortableFileDialogRequest, string> showDialog = _showDialog;
                if (showDialog == null)
                {
                    return;
                }

                _showDialog = null;

                if (ReferenceEquals(Volatile.Read(ref s_showDialog), showDialog))
                {
                    Volatile.Write(ref s_showDialog, null);
                }
            }
        }

        private sealed class EmptyRegistration : IDisposable
        {
            internal static readonly EmptyRegistration Instance = new EmptyRegistration();

            public void Dispose()
            {
            }
        }

        private sealed class FileDialogServiceRegistrar : IPortableFileDialogServiceRegistrar
        {
            public PortableWpfServiceKey ServiceKey
            {
                get
                {
                    return PortableWpfServiceKey.PresentationFramework;
                }
            }

            public IDisposable Register(Func<ProGPU.Wpf.Interop.PortableFileDialogRequest, string> showDialog)
            {
                ArgumentNullException.ThrowIfNull(showDialog);

                return PortableFileDialogService.Register(
                    request => showDialog(CreateInteropRequest(request)));
            }

            public void Clear()
            {
                PortableFileDialogService.Clear();
            }
        }
    }
}
