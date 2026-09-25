using System.Reflection;
using Microsoft.FSharp.Control;

namespace FSharp.MinimalApi;

using Microsoft.FSharp.Core;
using Microsoft.AspNetCore.Http;

/// <summary>
/// Creates a delegate with AsParametersAttribute
/// </summary>
public static class AsParameters
{
    /// <summary>
    /// Creates delegates with AsParametersAttribute
    /// </summary>
    public static Delegate Of<TParam, TResult>(FSharpFunc<TParam, TResult> requestDelegate) =>
        IsTask<TResult>()
            ? CreateDynamic(nameof(OfTask), requestDelegate)
            : IsAsync<TResult>()
                ? CreateDynamic(nameof(OfAsync), requestDelegate)
                : IsValueTaskOfUnit<TResult>()
                    ? CreateDynamic(nameof(OfValueTask), requestDelegate)
                    : typeof(TParam) == typeof(Unit)
                        ? typeof(TResult) == typeof(Unit)
                            ? void () => requestDelegate.Invoke(Operators.Unchecked.DefaultOf<TParam>())
                            : () => requestDelegate.Invoke(Operators.Unchecked.DefaultOf<TParam>())
                        : typeof(TResult) == typeof(Unit)
                            ? void ([AsParameters] TParam parameters) =>
                            requestDelegate.Invoke(parameters)
                            : ([AsParameters] TParam parameters) => requestDelegate.Invoke(parameters);

    /// <summary>
    /// Creates task delegates with AsParametersAttribute
    /// </summary>
    public static Delegate OfTask<TParam, TResult>(
        FSharpFunc<TParam, Task<TResult>> requestDelegate) =>
        typeof(TParam) == typeof(Unit)
            ? typeof(TResult) == typeof(Unit)
                ? Task () => requestDelegate.Invoke(Operators.Unchecked.DefaultOf<TParam>())
                : Task<TResult> () => requestDelegate.Invoke(Operators.Unchecked.DefaultOf<TParam>())
            : typeof(TResult) == typeof(Unit)
                ? Task ([AsParameters] TParam parameters) => requestDelegate.Invoke(parameters)
                : Task<TResult> ([AsParameters] TParam parameters) => requestDelegate
                    .Invoke(parameters);

    /// <summary>
    /// Creates async delegates with AsParametersAttribute
    /// </summary>
    public static Delegate OfAsync<TParam, TResult>(
        FSharpFunc<TParam, FSharpAsync<TResult>> requestDelegate) =>
        typeof(TParam) == typeof(Unit)
            ? typeof(TResult) == typeof(Unit)
                ? Task (CancellationToken cancellationToken) =>
                FSharpAsync.StartImmediateAsTask(
                    requestDelegate.Invoke(Operators.Unchecked.DefaultOf<TParam>()),
                    cancellationToken)
                : Task<TResult> (CancellationToken cancellationToken) =>
                    FSharpAsync.StartImmediateAsTask(
                        requestDelegate.Invoke(Operators.Unchecked.DefaultOf<TParam>()),
                        cancellationToken)
            : typeof(TResult) == typeof(Unit)
                ? Task ([AsParameters] TParam parameters, CancellationToken cancellationToken) =>
                FSharpAsync.StartImmediateAsTask(
                    requestDelegate.Invoke(parameters),
                    cancellationToken)
                : ([AsParameters] TParam parameters, CancellationToken cancellationToken) =>
                FSharpAsync.StartImmediateAsTask(requestDelegate.Invoke(parameters),
                    cancellationToken);

    /// <summary>
    /// Creates ValueTask delegates while preserving the concrete result type and metadata.
    /// </summary>
    public static Delegate OfValueTask<TParam, TResult>(
        FSharpFunc<TParam, ValueTask<TResult>> requestDelegate) =>
        typeof(TParam) == typeof(Unit)
            ? typeof(TResult) == typeof(Unit)
                ? ValueTask () => IgnoreResult(requestDelegate.Invoke(Operators.Unchecked.DefaultOf<TParam>()))
                : ValueTask<TResult> () => requestDelegate.Invoke(Operators.Unchecked.DefaultOf<TParam>())
            : typeof(TResult) == typeof(Unit)
                ? ValueTask ([AsParameters] TParam parameters) => IgnoreResult(requestDelegate.Invoke(parameters))
                : ValueTask<TResult> ([AsParameters] TParam parameters) => requestDelegate.Invoke(parameters);

    static async ValueTask IgnoreResult<T>(ValueTask<T> value) => await value;

    // A plain Task has no result to unwrap; the delegate returns it as is and ASP.NET awaits it.
    static bool IsTask<T>() =>
        typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(Task<>);

    static bool IsAsync<T>() =>
        (typeof(T).IsGenericType && typeof(T).GetGenericTypeDefinition() == typeof(FSharpAsync<>));

    // ValueTask<T> already works through the ordinary delegate. Only Unit needs
    // adapting to a non-generic ValueTask so it produces an empty response.
    static bool IsValueTaskOfUnit<T>() => typeof(T) == typeof(ValueTask<Unit>);

    static Delegate CreateDynamic<TParam, TResult>(string methodName,
        FSharpFunc<TParam, TResult> requestDelegate)
    {
        var underType = typeof(TResult).GetGenericArguments()[0];
        var method = typeof(AsParameters)
            .GetMethod(methodName, BindingFlags.Public | BindingFlags.Static)!
            .MakeGenericMethod(typeof(TParam), underType);

        return (Delegate) method.Invoke(null, new object?[] {requestDelegate})!;
    }
}
