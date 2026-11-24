// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Reflection;

namespace System.Windows.Markup
{
    /// <summary>
    /// Resource dictionaries compiled to code by the LibreWPF XAML source generator (XSG), keyed by the simple name of the assembly
    /// they belong to and their resource path (for example <c>PresentationFramework.Aero2</c> + <c>themes/aero2.normalcolor.xaml</c>).
    /// Generated code registers each dictionary from a module initializer. WPF's theme lookup asks here, by name, before it loads a
    /// theme assembly and looks for BAML, so a theme compiled by the generator needs neither BAML nor System.Xaml at run time, and
    /// under NativeAOT (which runs every module initializer at startup) not even the assembly load.
    /// </summary>
    public static class XamlSourceGeneratedResources
    {
        /// <summary>
        /// Sets <paramref name="property"/> of <paramref name="target"/> to a dynamic resource reference (<c>{DynamicResource key}</c>). Generated
        /// code uses this for objects that have no SetResourceReference (a Freezable such as the Color of a SolidColorBrush); a
        /// FrameworkElement or FrameworkContentElement is set with its own SetResourceReference.
        /// </summary>
        public static void SetDynamicResource(DependencyObject target, DependencyProperty property, object resourceKey)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(property);
            ArgumentNullException.ThrowIfNull(resourceKey);
            target.SetValue(property, new ResourceReferenceExpression(resourceKey));
        }

        /// <summary>
        /// The entry that <c>&lt;DynamicResource x:Key="K" ResourceKey="..."/&gt;</c> puts in a dictionary: a reference resolved when something asks for K
        /// (BAML stores the same expression there). Used by generated code.
        /// </summary>
        public static object DynamicResourceEntry(object resourceKey)
        {
            ArgumentNullException.ThrowIfNull(resourceKey);
            return new ResourceReferenceExpression(resourceKey);
        }

        /// <summary>
        /// What a StaticResource lookup inside a generated dictionary returns for <paramref name="entry"/>: the entry itself, except that a
        /// <see cref="DynamicResourceEntry"/> is resolved to the resource it names (in <paramref name="scope"/>, then the application, then the system).
        /// </summary>
        public static object ResolveEntry(object entry, ResourceDictionary scope)
        {
            for (int depth = 0; entry is ResourceReferenceExpression reference && depth < 16; depth++)
            {
                object key = reference.ResourceKey;
                object resolved = (scope != null && scope.Contains(key)) ? scope[key] : null;
                resolved ??= Application.Current?.TryFindResource(key);
                resolved ??= SystemResources.FindResourceInternal(key);
                if (resolved == null || resolved == DependencyProperty.UnsetValue)
                {
                    return entry;
                }
                entry = resolved;
            }

            return entry;
        }

        private static readonly object s_lock = new object();
        private static readonly Dictionary<string, Func<ResourceDictionary>> s_factories = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Registers the factory of a generated dictionary that belongs to <paramref name="assembly"/>.</summary>
        public static void RegisterDictionary(Assembly assembly, string resourcePath, Func<ResourceDictionary> factory)
        {
            ArgumentNullException.ThrowIfNull(assembly);
            RegisterDictionary(assembly.GetName().Name, resourcePath, factory);
        }

        /// <summary>
        /// Registers the factory of a generated dictionary under an assembly name, which need not be the registering assembly's own:
        /// an XSG-compiled theme can stand in for <c>PresentationFramework.Aero2</c>.
        /// </summary>
        public static void RegisterDictionary(string assemblyName, string resourcePath, Func<ResourceDictionary> factory)
        {
            ArgumentNullException.ThrowIfNull(assemblyName);
            ArgumentNullException.ThrowIfNull(resourcePath);
            ArgumentNullException.ThrowIfNull(factory);

            lock (s_lock)
            {
                s_factories[Key(assemblyName, resourcePath)] = factory;
            }
        }

        /// <summary>Creates the generated dictionary registered under the assembly name and path, if there is one.</summary>
        public static bool TryCreateDictionary(string assemblyName, string resourcePath, out ResourceDictionary dictionary)
        {
            ArgumentNullException.ThrowIfNull(assemblyName);
            ArgumentNullException.ThrowIfNull(resourcePath);

            Func<ResourceDictionary> factory;
            lock (s_lock)
            {
                s_factories.TryGetValue(Key(assemblyName, resourcePath), out factory);
            }

            dictionary = factory?.Invoke();
            return dictionary is not null;
        }

        /// <summary>
        /// Creates the generated dictionary registered for <paramref name="assembly"/> (by its simple name) and path, if there is one.
        /// This is a plain lookup with no side effect: it never loads or initializes anything, so for an application that has no generated
        /// dictionaries (every BAML application) it answers "no" and WPF's theme lookup continues exactly as before. A library compiled
        /// by the generator registers from its module initializer, which the runtime runs with the first use of any code of that
        /// assembly (a control library has run it by the time one of its controls looks up its default style) and NativeAOT runs for
        /// every assembly in the image at startup.
        /// </summary>
        public static bool TryCreateDictionary(Assembly assembly, string resourcePath, out ResourceDictionary dictionary)
        {
            ArgumentNullException.ThrowIfNull(assembly);

            return TryCreateDictionary(assembly.GetName().Name, resourcePath, out dictionary);
        }

        // ---- components (the root objects of XAML files: Window, Page, UserControl) -------------------------------------------------------

        private static readonly Dictionary<string, Func<object>> s_components = new(StringComparer.OrdinalIgnoreCase);
        private static volatile bool s_hasComponents;

        /// <summary>
        /// Registers the factory of the root object of a XAML file compiled by the XAML source generator, under its assembly name and
        /// resource path (<c>mainwindow.xaml</c>). <see cref="Application.LoadComponent(Uri)"/> (and with it <c>Application.StartupUri</c>)
        /// asks here before it looks for BAML, so a NativeAOT application can start a window from a URI without any BAML resource.
        /// </summary>
        public static void RegisterComponent(string assemblyName, string resourcePath, Func<object> factory)
        {
            ArgumentNullException.ThrowIfNull(assemblyName);
            ArgumentNullException.ThrowIfNull(resourcePath);
            ArgumentNullException.ThrowIfNull(factory);

            lock (s_lock)
            {
                s_components[Key(assemblyName, resourcePath)] = factory;
                s_hasComponents = true;
            }
        }

        /// <summary>
        /// Creates the component registered for a resource locator: a relative URI (<c>MainWindow.xaml</c>, resolved against the entry
        /// assembly), <c>pack://application:,,,/MainWindow.xaml</c> or <c>pack://application:,,,/Other;component/Page.xaml</c>. False, at
        /// once and without side effects, when no component was ever registered (every application that does not use the generator).
        /// </summary>
        public static bool TryCreateComponent(Uri resourceLocator, out object component)
        {
            component = null;
            if (!s_hasComponents || resourceLocator == null)
            {
                return false;
            }

            string path = resourceLocator.IsAbsoluteUri ? resourceLocator.AbsolutePath : resourceLocator.OriginalString;
            path = Uri.UnescapeDataString(path).Replace('\\', '/').TrimStart('/');
            string assemblyName = null;
            int marker = path.IndexOf(";component/", StringComparison.OrdinalIgnoreCase);
            if (marker >= 0)
            {
                string assemblyPart = path.Substring(0, marker);
                int version = assemblyPart.IndexOf(';');
                assemblyName = version >= 0 ? assemblyPart.Substring(0, version) : assemblyPart;
                path = path.Substring(marker + ";component/".Length);
            }

            Func<object> factory = null;
            lock (s_lock)
            {
                if (assemblyName != null)
                {
                    s_components.TryGetValue(Key(assemblyName, path), out factory);
                }
                else
                {
                    // Relative to the application: its entry assembly first, then the assembly that registered that path, if exactly one did
                    // (two libraries registering the same relative path would otherwise make the result depend on enumeration order).
                    string entry = System.Reflection.Assembly.GetEntryAssembly()?.GetName().Name;
                    if (entry == null || !s_components.TryGetValue(Key(entry, path), out factory))
                    {
                        string suffix = "|" + path.ToLowerInvariant();
                        int matches = 0;
                        foreach (KeyValuePair<string, Func<object>> candidate in s_components)
                        {
                            if (candidate.Key.EndsWith(suffix, StringComparison.Ordinal))
                            {
                                factory = candidate.Value;
                                matches++;
                            }
                        }

                        if (matches != 1)
                        {
                            factory = null;
                        }
                    }
                }
            }

            if (factory == null)
            {
                return false;
            }

            // A registered factory that makes nothing is a bug of the generated (or hand-written) code: say so, instead of letting the
            // caller report a missing resource.
            component = factory() ?? throw new InvalidOperationException(
                $"The factory registered for the component '{resourceLocator}' returned null.");
            return true;
        }

        private static string Key(string assemblyName, string resourcePath) =>
            assemblyName + "|" + resourcePath.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
    }
}
