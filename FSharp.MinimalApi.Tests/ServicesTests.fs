module ServicesTests

open System
open Microsoft.Extensions.DependencyInjection
open Xunit
open FSharp.MinimalApi
open FSharp.MinimalApi.Builder
open Harness

type A() =
  member _.Name = "a"

type B() =
  member _.Name = "b"

let private provider () =
  ServiceCollection().AddSingleton<A>().AddSingleton<B>().AddTuples().BuildServiceProvider()

let private resolve<'t> () =
  use services = provider ()
  services.GetRequiredService<'t>()

[<Fact>]
let ``AddTuples resolves tuples of services`` () =
  let a, b = resolve<A * B>()
  Assert.Equal("a", a.Name)
  Assert.Equal("b", b.Name)

[<Fact>]
let ``AddTuples resolves every tuple size it registers`` () =
  use services = provider ()
  let a = services.GetRequiredService<A>()
  let b = services.GetRequiredService<B>()

  let expected: obj list =
    [
      box (a, b)
      box (a, b, a)
      box (a, b, a, b)
      box (a, b, a, b, a)
      box (a, b, a, b, a, b)
      box (a, b, a, b, a, b, a)
      box (a, b, a, b, a, b, a, b)
      box (a, b, a, b, a, b, a, b, a)
      box (a, b, a, b, a, b, a, b, a, b)
    ]

  for tuple in expected do
    Assert.Equal(tuple, services.GetRequiredService(tuple.GetType()))

[<Fact>]
let ``AddTuples creates a new tuple every time`` () =
  use services = provider ()
  Assert.NotSame(services.GetRequiredService<A * B>(), services.GetRequiredService<A * B>())

[<Fact>]
let ``handlers can take a tuple of services`` () =
  task {
    use! app =
      startWith (fun services -> services.AddSingleton<A>().AddSingleton<B>().AddTuples() |> ignore) (fun app ->
        (endpoints { get "/names" (fun (req: {| deps: A * B |}) -> let a, b = req.deps in a.Name + b.Name) }).Apply app
        |> ignore)

    let! response = get app "/names"
    do! expectBody ok "ab" response
  }