[<AutoOpen>]
module BasicApi.Extensions

open System
open System.Linq
open System.Linq.Expressions
open System.Runtime.CompilerServices
open System.Threading
open System.Threading.Tasks
open Microsoft.EntityFrameworkCore

// C#-style extensions, so the methods can require reference types that FirstOrDefaultAsync returns as null.
[<Extension>]
type QueryableExtensions =

    [<Extension>]
    static member TryFirstAsync(query: IQueryable<'T>) =
        task {
            let! r = query.FirstOrDefaultAsync()
            return Option.ofObj r
        }

    [<Extension>]
    static member TryFirstAsync(query: IQueryable<'T>, predicate: Expression<Func<'T, bool>>) =
        task {
            let! r = query.FirstOrDefaultAsync(predicate, CancellationToken.None)
            return Option.ofObj r
        }

type DbSet<'T when 'T: not struct and 'T: not null> with

    member this.add v = this.Add(v) |> ignore
    member this.remove v = this.Remove(v) |> ignore

type DbContext with

    member this.saveChangesAsync v =
        task {
            let! _ = this.SaveChangesAsync()
            return ()
        }
        :> Task
