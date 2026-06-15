using System;
using System.Linq.Expressions;
using System.Reflection;
using MediaDrawingContext = System.Windows.Media.DrawingContext;

namespace System.Windows.Media.ProGPU;

public static class WpfRenderDataSinkProviderBridge
{
    internal const string ProviderTypeName = "System.Windows.Media.RenderDataDrawingContextSinkProvider";
    private const string PushDrawingContextFactoryMethodName = "PushDrawingContextFactory";

    public static bool TryRegisterDrawingContextFactory(
        ProGpuWpfDrawingFrame drawingFrame,
        out IDisposable? registration)
    {
        ArgumentNullException.ThrowIfNull(drawingFrame);

        return TryRegisterDrawingContextFactory(
            drawingFrame.CreateDrawingContextFactory(),
            out registration);
    }

    public static bool TryRegisterDrawingContextFactory(
        Func<object?, MediaDrawingContext> drawingContextFactory,
        out IDisposable? registration)
    {
        ArgumentNullException.ThrowIfNull(drawingContextFactory);

        Type? providerType = typeof(MediaDrawingContext).Assembly.GetType(
            ProviderTypeName,
            throwOnError: false);
        if (providerType == null)
        {
            registration = null;
            return false;
        }

        return TryRegisterDrawingContextFactory(
            providerType,
            drawingContextFactory,
            out registration);
    }

    internal static bool TryRegisterDrawingContextFactory(
        Type providerType,
        Func<object?, MediaDrawingContext> drawingContextFactory,
        out IDisposable? registration)
    {
        ArgumentNullException.ThrowIfNull(providerType);
        ArgumentNullException.ThrowIfNull(drawingContextFactory);

        MethodInfo? method = FindPushDrawingContextFactoryMethod(providerType);
        if (method == null)
        {
            registration = null;
            return false;
        }

        Type delegateType = method.GetParameters()[0].ParameterType;
        if (!TryCreateDrawingContextFactoryDelegate(
                delegateType,
                drawingContextFactory,
                out Delegate? typedFactory))
        {
            registration = null;
            return false;
        }

        object? result = method.Invoke(null, new object?[] { typedFactory });
        registration = result as IDisposable;
        return registration != null;
    }

    private static MethodInfo? FindPushDrawingContextFactoryMethod(Type providerType)
    {
        MethodInfo[] methods = providerType.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        foreach (MethodInfo method in methods)
        {
            if (!string.Equals(method.Name, PushDrawingContextFactoryMethodName, StringComparison.Ordinal))
            {
                continue;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 1 || !typeof(Delegate).IsAssignableFrom(parameters[0].ParameterType))
            {
                continue;
            }

            if (typeof(IDisposable).IsAssignableFrom(method.ReturnType))
            {
                return method;
            }
        }

        return null;
    }

    private static bool TryCreateDrawingContextFactoryDelegate(
        Type delegateType,
        Func<object?, MediaDrawingContext> drawingContextFactory,
        out Delegate? typedFactory)
    {
        MethodInfo? invokeMethod = delegateType.GetMethod("Invoke");
        if (invokeMethod == null)
        {
            typedFactory = null;
            return false;
        }

        ParameterInfo[] parameters = invokeMethod.GetParameters();
        Type returnType = invokeMethod.ReturnType;
        if (parameters.Length != 1 || !returnType.IsAssignableFrom(typeof(MediaDrawingContext)))
        {
            typedFactory = null;
            return false;
        }

        ParameterExpression ownerVisual = Expression.Parameter(parameters[0].ParameterType, "ownerVisual");
        MethodInfo invokeFactoryMethod = typeof(WpfRenderDataSinkProviderBridge).GetMethod(
            nameof(InvokeDrawingContextFactory),
            BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodCallExpression factoryCall = Expression.Call(
            invokeFactoryMethod,
            Expression.Constant(drawingContextFactory),
            Expression.Convert(ownerVisual, typeof(object)));
        Expression body = factoryCall.Type == returnType
            ? factoryCall
            : Expression.Convert(factoryCall, returnType);

        typedFactory = Expression.Lambda(delegateType, body, ownerVisual).Compile();
        return true;
    }

    private static MediaDrawingContext InvokeDrawingContextFactory(
        Func<object?, MediaDrawingContext> drawingContextFactory,
        object? ownerVisual)
    {
        return drawingContextFactory(ownerVisual);
    }
}
