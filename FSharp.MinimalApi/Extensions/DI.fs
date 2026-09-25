[<AutoOpen>]
module FSharp.MinimalApi.DI

open Microsoft.Extensions.DependencyInjection

type IServiceCollection with

    member services.AddTuples() =
        services
            // Tuples of eight or more nest the rest in a smaller tuple, which starts at one element.
            .AddTransient(typedefof<System.Tuple<_>>)
            .AddTransient(typedefof<_ * _>)
            .AddTransient(typedefof<_ * _ * _>)
            .AddTransient(typedefof<_ * _ * _ * _>)
            .AddTransient(typedefof<_ * _ * _ * _ * _>)
            .AddTransient(typedefof<_ * _ * _ * _ * _ * _>)
            .AddTransient(typedefof<_ * _ * _ * _ * _ * _ * _>)
            .AddTransient(typedefof<_ * _ * _ * _ * _ * _ * _ * _>)
            .AddTransient(typedefof<_ * _ * _ * _ * _ * _ * _ * _ * _>)
            .AddTransient(typedefof<_ * _ * _ * _ * _ * _ * _ * _ * _ * _>)
