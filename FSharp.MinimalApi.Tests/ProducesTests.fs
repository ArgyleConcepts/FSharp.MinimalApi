module ProducesTests

open System
open System.Net
open System.Net.Http
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Http.HttpResults
open Xunit
open FSharp.MinimalApi.Builder
open Harness
open type TypedResults

// One distinct result type per arity, in the order `produces` lists them.
type R1 = Ok<string>
type R2 = Created<string>
type R3 = Accepted<string>
type R4 = NoContent
type R5 = BadRequest<string>
type R6 = NotFound<string>
type R7 = Conflict<string>
type R8 = UnprocessableEntity<string>
type R9 = InternalServerError<string>
type R10 = ValidationProblem
type R11 = Ok

let r1 () = Ok "1"
let r2 () = Created("/created", "2")
let r3 () = Accepted("/accepted", "3")
let r4 () = NoContent()
let r5 () = BadRequest "5"
let r6 () = NotFound "6"
let r7 () = Conflict "7"
let r8 () = UnprocessableEntity "8"
let r9 () = InternalServerError "9"

let r10 () =
  ValidationProblem(dict [ "field", [| "10" |] ])

let r11 () = Ok()

/// What each result declares for OpenAPI and what it answers at runtime.
let declared =
  [
    200, typeof<string>
    201, typeof<string>
    202, typeof<string>
    204, typeof<Void>
    400, typeof<string>
    404, typeof<string>
    409, typeof<string>
    422, typeof<string>
    500, typeof<string>
    400, typeof<HttpValidationProblemDetails>
    200, typeof<Void>
  ]

let expectedFor arity =
  declared
  |> List.take arity
  |> List.distinct
  |> List.sortBy (fun (status, t) -> status, t.FullName)

let statusOf case = fst declared[case - 1]

type Case = {| case: int |}

let private unexpected (req: Case) : 'a =
  failwith $"unexpected case %d{req.case}"

type Arity11 = Results<R1, R2, R3, R4, R5, Results<R6, R7, R8, R9, R10, R11>>

// `!!` can't pick the conversion when both sides are Results with six cases, so arity 11 calls it directly.
let private arity11 (req: Case) : Arity11 =
  let inline rest x : Results<R6, R7, R8, R9, R10, R11> = !!x

  match req.case with
  | 1 -> !!r1()
  | 2 -> !!r2()
  | 3 -> !!r3()
  | 4 -> !!r4()
  | 5 -> !!r5()
  | 6 -> Arity11.op_Implicit (rest (r6 ()))
  | 7 -> Arity11.op_Implicit (rest (r7 ()))
  | 8 -> Arity11.op_Implicit (rest (r8 ()))
  | 9 -> Arity11.op_Implicit (rest (r9 ()))
  | 10 -> Arity11.op_Implicit (rest (r10 ()))
  | 11 -> Arity11.op_Implicit (rest (r11 ()))
  | _ -> unexpected req

let routes =
  endpoints {
    get "/arity/1" produces<R1> (fun (_: Case) -> r1 ())

    get "/arity/2" produces<R1, R2> (fun (req: Case) ->
      match req.case with
      | 1 -> !!r1()
      | 2 -> !!r2()
      | _ -> unexpected req)

    get "/arity/3" produces<R1, R2, R3> (fun (req: Case) ->
      match req.case with
      | 1 -> !!r1()
      | 2 -> !!r2()
      | 3 -> !!r3()
      | _ -> unexpected req)

    get "/arity/4" produces<R1, R2, R3, R4> (fun (req: Case) ->
      match req.case with
      | 1 -> !!r1()
      | 2 -> !!r2()
      | 3 -> !!r3()
      | 4 -> !!r4()
      | _ -> unexpected req)

    get "/arity/5" produces<R1, R2, R3, R4, R5> (fun (req: Case) ->
      match req.case with
      | 1 -> !!r1()
      | 2 -> !!r2()
      | 3 -> !!r3()
      | 4 -> !!r4()
      | 5 -> !!r5()
      | _ -> unexpected req)

    get "/arity/6" produces<R1, R2, R3, R4, R5, R6> (fun (req: Case) ->
      match req.case with
      | 1 -> !!r1()
      | 2 -> !!r2()
      | 3 -> !!r3()
      | 4 -> !!r4()
      | 5 -> !!r5()
      | 6 -> !!r6()
      | _ -> unexpected req)

    // From seven results up, the last ones are nested in an inner Results<...>.
    get "/arity/7" produces<R1, R2, R3, R4, R5, R6, R7> (fun (req: Case) ->
      let inline rest x : Results<R6, R7> = !!x

      match req.case with
      | 1 -> !!r1()
      | 2 -> !!r2()
      | 3 -> !!r3()
      | 4 -> !!r4()
      | 5 -> !!r5()
      | 6 -> !!(rest (r6 ()))
      | 7 -> !!(rest (r7 ()))
      | _ -> unexpected req)

    get "/arity/8" produces<R1, R2, R3, R4, R5, R6, R7, R8> (fun (req: Case) ->
      let inline rest x : Results<R6, R7, R8> = !!x

      match req.case with
      | 1 -> !!r1()
      | 2 -> !!r2()
      | 3 -> !!r3()
      | 4 -> !!r4()
      | 5 -> !!r5()
      | 6 -> !!(rest (r6 ()))
      | 7 -> !!(rest (r7 ()))
      | 8 -> !!(rest (r8 ()))
      | _ -> unexpected req)

    get "/arity/9" produces<R1, R2, R3, R4, R5, R6, R7, R8, R9> (fun (req: Case) ->
      let inline rest x : Results<R6, R7, R8, R9> = !!x

      match req.case with
      | 1 -> !!r1()
      | 2 -> !!r2()
      | 3 -> !!r3()
      | 4 -> !!r4()
      | 5 -> !!r5()
      | 6 -> !!(rest (r6 ()))
      | 7 -> !!(rest (r7 ()))
      | 8 -> !!(rest (r8 ()))
      | 9 -> !!(rest (r9 ()))
      | _ -> unexpected req)

    get "/arity/10" produces<R1, R2, R3, R4, R5, R6, R7, R8, R9, R10> (fun (req: Case) ->
      let inline rest x : Results<R6, R7, R8, R9, R10> = !!x

      match req.case with
      | 1 -> !!r1()
      | 2 -> !!r2()
      | 3 -> !!r3()
      | 4 -> !!r4()
      | 5 -> !!r5()
      | 6 -> !!(rest (r6 ()))
      | 7 -> !!(rest (r7 ()))
      | 8 -> !!(rest (r8 ()))
      | 9 -> !!(rest (r9 ()))
      | 10 -> !!(rest (r10 ()))
      | _ -> unexpected req)

    get "/arity/11" produces<R1, R2, R3, R4, R5, R6, R7, R8, R9, R10, R11> arity11
  }

let arities = TheoryData<int>([ 1..11 ])

[<Theory>]
[<MemberData(nameof arities)>]
let ``produces declares every result type`` (arity: int) =
  task {
    use! app = serve routes
    let e = endpoint app "GET" $"/arity/%d{arity}"
    Assert.Equal<(int * Type) list>(expectedFor arity, producedBy e)
  }

[<Theory>]
[<MemberData(nameof arities)>]
let ``produces handlers can return every declared result`` (arity: int) =
  task {
    use! app = serve routes

    for case in 1..arity do
      let! response = get app $"/arity/%d{arity}?case=%d{case}"
      let! _ = expect (enum<HttpStatusCode>(statusOf case)) response
      ()
  }

type Id = {| id: int |}

let private found (req: Id) : Results<Ok<string>, NotFound> =
  if req.id > 0 then
    !!(Ok $"found %d{req.id}")
  else
    !!NotFound()

let shapes =
  endpoints {
    get "/sync/{id}" produces<Ok<string>, NotFound> found
    get "/task/{id}" produces<Ok<string>, NotFound> (fun req -> task { return found req })
    get "/async/{id}" produces<Ok<string>, NotFound> (fun req -> async { return found req })

    post "/sync/{id}" produces<Ok<string>, NotFound> found
    post "/task/{id}" produces<Ok<string>, NotFound> (fun req -> task { return found req })
    post "/async/{id}" produces<Ok<string>, NotFound> (fun req -> async { return found req })

    put "/sync/{id}" produces<Ok<string>, NotFound> found
    put "/task/{id}" produces<Ok<string>, NotFound> (fun req -> task { return found req })
    put "/async/{id}" produces<Ok<string>, NotFound> (fun req -> async { return found req })

    delete "/sync/{id}" produces<Ok<string>, NotFound> found
    delete "/task/{id}" produces<Ok<string>, NotFound> (fun req -> task { return found req })
    delete "/async/{id}" produces<Ok<string>, NotFound> (fun req -> async { return found req })
  }

let verbsAndShapes =
  TheoryData<string, string>(
    [
      for verb in [ "GET"; "POST"; "PUT"; "DELETE" ] do
        for shape in [ "sync"; "task"; "async" ] do
          struct (verb, shape)
    ]
  )

[<Theory>]
[<MemberData(nameof verbsAndShapes)>]
let ``produces works for every verb and handler shape`` (verb: string, shape: string) =
  task {
    use! app = serve shapes
    let e = endpoint app verb $"/%s{shape}/{{id}}"
    Assert.Equal<(int * Type) list>([ 200, typeof<string>; 404, typeof<Void> ], producedBy e)

    let! found = send app (request (HttpMethod verb) $"/%s{shape}/5")
    do! expectBody HttpStatusCode.OK "\"found 5\"" found
    let! missing = send app (request (HttpMethod verb) $"/%s{shape}/0")
    do! expectBody HttpStatusCode.NotFound "" missing
  }

[<Fact>]
let ``produces works with parameterless handlers`` () =
  task {
    use! app =
      serve (
        endpoints {
          get "/sync" produces<Ok<string>> (fun () -> Ok "sync")
          get "/task" produces<Ok<string>> (fun () -> task { return Ok "task" })
          get "/async" produces<Ok<string>> (fun () -> async { return Ok "async" })
        }
      )

    for shape in [ "sync"; "task"; "async" ] do
      Assert.Equal<(int * Type) list>([ 200, typeof<string> ], producedBy (endpoint app "GET" $"/%s{shape}"))
      let! response = get app $"/%s{shape}"
      do! expectBody HttpStatusCode.OK $"\"%s{shape}\"" response
  }

[<Fact>]
let ``handlers without produces declare only what the result type provides`` () =
  task {
    use! app =
      serve (
        endpoints {
          get "/typed" (fun () -> Ok "typed")
          get "/untyped" (fun () -> Results.Ok "untyped")
        }
      )

    Assert.Equal<(int * Type) list>([ 200, typeof<string> ], producedBy (endpoint app "GET" "/typed"))
    Assert.Empty(producedBy (endpoint app "GET" "/untyped"))
    let! typed = get app "/typed"
    do! expectBody HttpStatusCode.OK "\"typed\"" typed
    let! untyped = get app "/untyped"
    do! expectBody HttpStatusCode.OK "\"untyped\"" untyped
  }

// The builder only reads the type from produces<...>, it never calls it for a value.
[<Fact>]
let ``produces only carries the result type`` () =
  let results: IResult list =
    [
      produces<R1>(())
      produces<R1, R2>(())
      produces<R1, R2, R3>(())
      produces<R1, R2, R3, R4>(())
      produces<R1, R2, R3, R4, R5>(())
      produces<R1, R2, R3, R4, R5, R6>(())
      produces<R1, R2, R3, R4, R5, R6, R7>(())
      produces<R1, R2, R3, R4, R5, R6, R7, R8>(())
      produces<R1, R2, R3, R4, R5, R6, R7, R8, R9>(())
      produces<R1, R2, R3, R4, R5, R6, R7, R8, R9, R10>(())
      produces<R1, R2, R3, R4, R5, R6, R7, R8, R9, R10, R11>(())
    ]

  Assert.All(results, (fun result -> Assert.Null result))