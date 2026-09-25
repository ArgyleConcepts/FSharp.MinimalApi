namespace FSharp.MinimalApi.Builder

open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.HttpResults

/// Construct either case of a two-outcome ASP.NET Core typed result.
/// The handler's return annotation supplies the other result type.
[<RequireQualifiedAccess>]
module Results2 =
    let first<'first, 'second when 'first :> IResult and 'second :> IResult>
        (value: 'first)
        : Results<'first, 'second> =
        Results<'first, 'second>.op_Implicit value

    let second<'first, 'second when 'first :> IResult and 'second :> IResult>
        (value: 'second)
        : Results<'first, 'second> =
        Results<'first, 'second>.op_Implicit value
