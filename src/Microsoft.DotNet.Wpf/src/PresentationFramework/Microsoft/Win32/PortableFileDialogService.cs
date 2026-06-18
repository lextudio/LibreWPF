// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;
using System.Threading;

namespace Microsoft.Win32
{
    internal static class PortableFileDialogService
    {
        private static readonly bool s_isWindows = RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        private static Func<object, string> s_showDialog;

        internal static bool IsEnabled
        {
            get
            {
                return !s_isWindows;
            }
        }

        internal static IDisposable Register(Func<object, string> showDialog)
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

            Func<object, string> showDialog = Volatile.Read(ref s_showDialog);
            if (showDialog == null)
            {
                return true;
            }

            selectedPath = showDialog(new PortableFileDialogRequest(dialog));
            return true;
        }

        private sealed class PortableFileDialogRequest
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

        private sealed class Registration : IDisposable
        {
            private Func<object, string> _showDialog;

            internal Registration(Func<object, string> showDialog)
            {
                _showDialog = showDialog;
            }

            public void Dispose()
            {
                Func<object, string> showDialog = _showDialog;
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
    }
}
