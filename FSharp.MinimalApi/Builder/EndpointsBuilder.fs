namespace FSharp.MinimalApi.Builder

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open FSharp.MinimalApi

type EndpointsMap =
    internal
        { MapFn: RouteGroupBuilder -> RouteGroupBuilder
          GroupName: string option }

    member this.Apply(r: IEndpointRouteBuilder) =
        this.GroupName |> Option.defaultValue String.Empty |> r.MapGroup |> this.MapFn

/// Every value passing through the builder is the state of the group being built.
/// Nested endpoints are mapped as subgroups of it, in the order they are declared.
type EndpointsBuilder(?groupName: string) =
    inherit RouterBaseBuilder<EndpointsMap>()

    let trimPath (path: string) = path.Trim('/')
    let concatPath (paths: string seq) = String.concat "/" paths

    override this.Append state f =
        { state with
            MapFn = state.MapFn >> tap f }

    member _.Zero() = { MapFn = id; GroupName = groupName }

    member _.Run(route: EndpointsMap) = route
    member _.Run(()) = ()

    member this.Yield(()) = this.Zero()

    member this.Yield(route: EndpointsMap) =
        { this.Zero() with
            MapFn = tap route.Apply }

    member _.Delay(f) = f ()

    member this.Combine(endpoints1: EndpointsMap, endpoints2: EndpointsMap) =
        { endpoints1 with
            MapFn = endpoints1.MapFn >> endpoints2.MapFn }

    member this.For(state: EndpointsMap, f: unit -> EndpointsMap) = this.Combine(state, f ())

    [<CustomOperation("group")>]
    member _.Group(state, name) = { state with GroupName = Some name }

    [<CustomOperation("useRoutes")>]
    member _.useRoutes(state, endpoints) =
        { state with
            MapFn = state.MapFn >> endpoints.MapFn }

    [<CustomOperation("path")>]
    member _.Path(state, [<ParamArray>] segments: string[]) =
        { state with
            GroupName = segments |> Array.map trimPath |> concatPath |> Some }

    [<CustomOperation("set")>]
    member _.Set(state, f) =
        { state with
            MapFn = state.MapFn << tap f }

    [<CustomOperation("allowAnonymous")>]
    member _.AllowAnonymous(state) =
        { state with
            MapFn = state.MapFn >> (fun e -> e.AllowAnonymous()) }

    [<CustomOperation("tags")>]
    member _.Tags(state, [<ParamArray>] tags) =
        { state with
            MapFn = state.MapFn >> (fun e -> e.WithTags(tags)) }

    [<CustomOperation("description")>]
    member _.Description(state, desc) =
        { state with
            MapFn = state.MapFn >> (fun e -> e.WithDescription(desc)) }

    [<CustomOperation("requireAuthorization")>]
    member _.RequireAuth(state) =
        { state with
            MapFn = state.MapFn >> (fun e -> e.RequireAuthorization()) }

    [<CustomOperation("requireAuthorization")>]
    member _.RequireAuth(state, [<ParamArray>] policies: string[]) =
        { state with
            MapFn = state.MapFn >> (fun e -> e.RequireAuthorization(policies)) }

    [<CustomOperation("requireAuthorization")>]
    member _.RequireAuth(state, [<ParamArray>] policies: IAuthorizeData[]) =
        { state with
            MapFn = state.MapFn >> (fun e -> e.RequireAuthorization(policies)) }

    [<CustomOperation("requireAuthorization")>]
    member _.RequireAuth(state, policy: AuthorizationPolicy) =
        { state with
            MapFn = state.MapFn >> (fun e -> e.RequireAuthorization(policy)) }

    [<CustomOperation("filter")>]
    member _.Filter<'args, 'f when 'f :> IEndpointFilter>(state, ctor: 'args -> 'f) =
        { state with
            MapFn = state.MapFn >> (fun e -> e.AddEndpointFilter<'f>()) }

    [<CustomOperation("filter")>]
    member _.Filter<'f when 'f :> IEndpointFilter>(state, filter: 'f) =
        { state with
            MapFn = state.MapFn >> (fun e -> e.AddEndpointFilter(filter)) }

    [<CustomOperation("filter")>]
    member _.Filter
        (
            state,
            f: EndpointFilterInvocationContext -> (EndpointFilterInvocationContext -> ValueTask<obj>) -> ValueTask<obj>
        ) =
        let filter =
            { new IEndpointFilter with
                member _.InvokeAsync(ctx, next) = f ctx next.Invoke }

        { state with
            MapFn = state.MapFn >> (fun e -> e.AddEndpointFilter(filter)) }

    [<CustomOperation("filter")>]
    member _.Filter
        (state, f: EndpointFilterInvocationContext -> (EndpointFilterInvocationContext -> ValueTask<obj>) -> Task<obj>)
        =
        let filter =
            { new IEndpointFilter with
                member _.InvokeAsync(ctx, next) = ValueTask<obj>(f ctx next.Invoke) }

        { state with
            MapFn = state.MapFn >> (fun e -> e.AddEndpointFilter(filter)) }

    [<CustomOperation("requireAuthorization")>]
    member _.RequireAuth(state, builder: AuthorizationPolicyBuilder -> unit) =
        { state with
            MapFn = state.MapFn >> (fun e -> e.RequireAuthorization(builder)) }

    [<CustomOperation("apply")>]
    member this.Apply(state: EndpointsMap, app) =
        let mapper = this.Run state
        mapper.Apply app |> ignore
