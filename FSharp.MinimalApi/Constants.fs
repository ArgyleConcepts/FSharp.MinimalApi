namespace FSharp.MinimalApi

[<RequireQualifiedAccess>]
module internal HttpMethodName =
    [<Literal>]
    let Get = "get"

    [<Literal>]
    let Post = "post"

    [<Literal>]
    let Put = "put"

    [<Literal>]
    let Delete = "delete"

    [<Literal>]
    let Patch = "patch"

    [<Literal>]
    let Methods = "methods"
