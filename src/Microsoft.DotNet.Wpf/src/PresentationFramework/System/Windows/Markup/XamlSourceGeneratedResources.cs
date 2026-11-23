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

        private static string Key(string assemblyName, string resourcePath) =>
            assemblyName + "|" + resourcePath.Replace('\\', '/').TrimStart('/').ToLowerInvariant();
    }
}
