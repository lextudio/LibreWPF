// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using MS.Internal;
using System.Runtime.ExceptionServices;

namespace System.Windows
{
    internal abstract class ReverseInheritProperty
    {
        internal ReverseInheritProperty(
            DependencyPropertyKey flagKey,
            CoreFlags flagCache,
            CoreFlags flagChanged)
            : this(flagKey, flagCache, flagChanged, CoreFlags.None, CoreFlags.None)
        {
        }

        internal ReverseInheritProperty(
            DependencyPropertyKey flagKey,
            CoreFlags flagCache,
            CoreFlags flagChanged,
            CoreFlags flagOldOriginCache,
            CoreFlags flagNewOriginCache)
        {
            FlagKey = flagKey;
            FlagCache = flagCache;
            FlagChanged = flagChanged;
            FlagOldOriginCache = flagOldOriginCache;
            FlagNewOriginCache = flagNewOriginCache;
        }

        internal abstract void FireNotifications(UIElement uie, ContentElement ce, UIElement3D uie3D, bool oldValue);

        internal void OnOriginValueChanged(DependencyObject oldOrigin, DependencyObject newOrigin, ref DeferredElementTreeState oldTreeState)
        {
            OnOriginValueChanged(oldOrigin, newOrigin, null, ref oldTreeState, null);
        }

        internal void OnOriginValueChanged(DependencyObject oldOrigin, DependencyObject newOrigin,
            ref DeferredElementTreeState oldTreeState, bool completeNotifications)
        {
            OnOriginValueChanged(oldOrigin, newOrigin, null, ref oldTreeState, null, completeNotifications);
        }

        internal void OnOriginValueChanged(DependencyObject oldOrigin,
            DependencyObject newOrigin,
            IList<DependencyObject> otherOrigins,
            ref DeferredElementTreeState oldTreeState,
            Action<DependencyObject, bool> originChangedAction)
        {
            OnOriginValueChanged(oldOrigin, newOrigin, otherOrigins, ref oldTreeState, originChangedAction, false);
        }

        private void OnOriginValueChanged(DependencyObject oldOrigin,
            DependencyObject newOrigin,
            IList<DependencyObject> otherOrigins,
            ref DeferredElementTreeState oldTreeState,
            Action<DependencyObject, bool> originChangedAction,
            bool completeNotifications)
        {
            DeferredElementTreeState treeStateLocalCopy = oldTreeState;
            oldTreeState = null;

            // Determine if one needs to raise notifications of elements
            // affected by the origin change irrespective of other origins
            bool setOriginCacheFlag = ((originChangedAction != null) &&
                                       (FlagOldOriginCache != CoreFlags.None) &&
                                       (FlagNewOriginCache != CoreFlags.None));

            // Step #1
            // Update the cache flags for all elements in the ancestry 
            // of the element that got turned off and record the changed nodes
            if (oldOrigin != null)
            {
                SetCacheFlagInAncestry(oldOrigin, false, treeStateLocalCopy, true, setOriginCacheFlag);
            }

            // Step #2
            // Update the cache flags for all elements in the ancestry 
            // of the element that got turned on and record the changed nodes
            if (newOrigin != null)
            {
                SetCacheFlagInAncestry(newOrigin, true, null, true, setOriginCacheFlag);
            }
            int otherCount = (otherOrigins != null) ? otherOrigins.Count : 0;
            for (int i = 0; i < otherCount; i++)
            {
                // setOriginCacheFlag is false, because these flags should not be affected by other origins
                SetCacheFlagInAncestry(otherOrigins[i], true, null, false, setOriginCacheFlag: false);
            }

            ExceptionDispatchInfo failure = null;
            bool notificationsCompleted = false;
            try
            {
                // Steps #3 and #4: publish the changed properties on both paths.
                // Portable capture must complete ancestor values after a callback
                // failure; otherwise the cache and dependency property disagree.
                if (oldOrigin != null)
                    FirePropertyChangeInAncestry(oldOrigin, true, treeStateLocalCopy, originChangedAction,
                        completeNotifications, ref failure);
                if (newOrigin != null)
                    FirePropertyChangeInAncestry(newOrigin, false, null, originChangedAction,
                        completeNotifications, ref failure);
                notificationsCompleted = true;
            }
            finally
            {
                if ((completeNotifications || notificationsCompleted) && oldTreeState == null && treeStateLocalCopy != null)
                {
                    treeStateLocalCopy.Clear();
                    oldTreeState = treeStateLocalCopy;
                }
            }
            failure?.Throw();
        }

        private void SetCacheFlagInAncestry(DependencyObject element,
            bool newValue,
            DeferredElementTreeState treeState,
            bool shortCircuit,
            bool setOriginCacheFlag)
        {
            UIElement uie;
            ContentElement ce;
            UIElement3D uie3D;
            CastElement(element, out uie, out ce, out uie3D);

            bool isFlagSet = IsFlagSet(uie, ce, uie3D, FlagCache);
            bool isFlagOriginCacheSet = (setOriginCacheFlag ? IsFlagSet(uie, ce, uie3D, (newValue ? FlagNewOriginCache : FlagOldOriginCache)) : false);

            // If the cache flag value is undergoing change, record it and
            // propagate the change to the ancestors.
            if ((newValue != isFlagSet) ||
                (setOriginCacheFlag && !isFlagOriginCacheSet) ||
                !shortCircuit)
            {
                if (newValue != isFlagSet)
                {
                    SetFlag(uie, ce, uie3D, FlagCache, newValue);

                    // NOTE: we toggle the changed flag instead of setting it so that that way common 
                    // ancestors show resultant unchanged and do not receive any change notification.
                    SetFlag(uie, ce, uie3D, FlagChanged, !IsFlagSet(uie, ce, uie3D, FlagChanged));
                }

                if (setOriginCacheFlag && !isFlagOriginCacheSet)
                {
                    SetFlag(uie, ce, uie3D, (newValue ? FlagNewOriginCache : FlagOldOriginCache), true);
                }

                // Check for block reverse inheritance flag, elements like popup want to set this.
                if (BlockReverseInheritance(uie, ce, uie3D))
                {
                    return;
                }

                // Propagate the flag up the visual and logical trees.  Note our
                // minimal optimization check to avoid walking both the core
                // and logical parents if they are the same.
                {
                    DependencyObject coreParent = DeferredElementTreeState.GetInputElementParent(element, treeState);
                    DependencyObject logicalParent = DeferredElementTreeState.GetLogicalParent(element, treeState);
                    
                    if (coreParent != null)
                    {
                        SetCacheFlagInAncestry(coreParent, newValue, treeState, shortCircuit, setOriginCacheFlag);
                    }                
                    if (logicalParent != null && logicalParent != coreParent)
                    {
                        SetCacheFlagInAncestry(logicalParent, newValue, treeState, shortCircuit, setOriginCacheFlag);
                    }
                }
            }
        }

        private void FirePropertyChangeInAncestry(DependencyObject element,
            bool oldValue,
            DeferredElementTreeState treeState,
            Action<DependencyObject, bool> originChangedAction,
            bool completeNotifications,
            ref ExceptionDispatchInfo failure)
        {
            UIElement uie;
            ContentElement ce;
            UIElement3D uie3D;
            CastElement(element, out uie, out ce, out uie3D);

            bool flagChanged = IsFlagSet(uie, ce, uie3D, FlagChanged);
            bool isFlagOldOriginCacheSet = ((FlagOldOriginCache == CoreFlags.None) ? false : IsFlagSet(uie, ce, uie3D, FlagOldOriginCache));
            bool isFlagNewOriginCacheSet = ((FlagNewOriginCache == CoreFlags.None) ? false : IsFlagSet(uie, ce, uie3D, FlagNewOriginCache));

            if (flagChanged || isFlagOldOriginCacheSet || isFlagNewOriginCacheSet)
            {
                if (flagChanged)
                {
                    // if FlagChanged bit is set, then the value has changed effectively
                    // after considering all the origins. Hence change the property value
                    // and fire notifications.
                    SetFlag(uie, ce, uie3D, FlagChanged, false);

                    try
                    {
                        if (oldValue)
                            element.ClearValue(FlagKey);
                        else
                            element.SetValue(FlagKey, true);

                        FireNotifications(uie, ce, uie3D, oldValue);
                    }
                    catch (Exception exception) when (completeNotifications)
                    {
                        RecordFailure(ref failure, exception);
                    }
                }

                if (isFlagOldOriginCacheSet || isFlagNewOriginCacheSet)
                {
                    SetFlag(uie, ce, uie3D, FlagOldOriginCache, false);
                    SetFlag(uie, ce, uie3D, FlagNewOriginCache, false);
                    if (isFlagOldOriginCacheSet != isFlagNewOriginCacheSet)
                    {
                        // if either FlagOldOriginCache or FlagNewOriginCache
                        // are set, then the origin change has affected this node
                        // and hence originChangedAction should be executed.
                        Debug.Assert(originChangedAction != null);
                        try { originChangedAction(element, oldValue); }
                        catch (Exception exception) when (completeNotifications) { RecordFailure(ref failure, exception); }
                    }
                }

                // Check for block reverse inheritance flag, elements like popup want to set this.
                if (BlockReverseInheritance(uie, ce, uie3D))
                {
                    return;
                }

                // Call FirePropertyChange up the visual and logical trees.
                // Note our minimal optimization check to avoid walking both
                // the core and logical parents if they are the same.
                {
                    DependencyObject coreParent = DeferredElementTreeState.GetInputElementParent(element, treeState);
                    DependencyObject logicalParent = DeferredElementTreeState.GetLogicalParent(element, treeState);

                    if (coreParent != null)
                    {
                        FirePropertyChangeInAncestry(coreParent, oldValue, treeState, originChangedAction,
                            completeNotifications, ref failure);
                    }
                    if (logicalParent != null && logicalParent != coreParent)
                    {
                        FirePropertyChangeInAncestry(logicalParent, oldValue, treeState, originChangedAction,
                            completeNotifications, ref failure);
                    }
                }
            }
        }

        /////////////////////////////////////////////////////////////////////

        private static void RecordFailure(ref ExceptionDispatchInfo failure, Exception exception) =>
            failure = ExceptionDispatchInfo.Capture(failure == null ? exception :
                new AggregateException(failure.SourceException, exception));

        private static bool BlockReverseInheritance(UIElement uie, ContentElement ce, UIElement3D uie3D)
        {
            if (uie != null)
            {
                return uie.BlockReverseInheritance();
            }
            else if (ce != null)
            {
                return ce.BlockReverseInheritance();
            }
            else if (uie3D != null)
            {
                return uie3D.BlockReverseInheritance();
            }

            return false;
        }

        /////////////////////////////////////////////////////////////////////

        private static void SetFlag(UIElement uie, ContentElement ce, UIElement3D uie3D, CoreFlags flag, bool value)
        {
            if (uie != null)
            {
                uie.WriteFlag(flag, value);
            }
            else if (ce != null)
            {
                ce.WriteFlag(flag, value);
            }
            else
            {
                uie3D?.WriteFlag(flag, value);
            }
        }

        /////////////////////////////////////////////////////////////////////

        private static bool IsFlagSet(UIElement uie, ContentElement ce, UIElement3D uie3D, CoreFlags flag)
        {
            if (uie != null)
            {
                return uie.ReadFlag(flag);
            }
            else if (ce != null)
            {
                return ce.ReadFlag(flag);
            }
            else if (uie3D != null)
            {
                return uie3D.ReadFlag(flag);
            }

            return false;
        }

        /////////////////////////////////////////////////////////////////////

        private static void CastElement(DependencyObject o, out UIElement uie, out ContentElement ce, out UIElement3D uie3D)
        {
            uie = o as UIElement;
            ce = (uie != null) ? null : o as ContentElement;
            uie3D = (uie != null || ce != null) ? null : o as UIElement3D;
        }

        /////////////////////////////////////////////////////////////////////

        protected DependencyPropertyKey FlagKey;
        protected CoreFlags FlagCache;
        protected CoreFlags FlagChanged;
        protected CoreFlags FlagOldOriginCache; // Flag to keep track of elements in the path of old origin
        protected CoreFlags FlagNewOriginCache; // Flag to keep track of elements in the path of new origin
    }
} 
