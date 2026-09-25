# Contributing

Thank you for helping improve FSharp.MinimalApi. Contributions to code, tests, examples, and documentation are welcome.

## Discuss a change

Search the [issues](https://github.com/ArgyleConcepts/FSharp.MinimalApi/issues) and open pull requests before starting. For a bug, include a small reproduction, expected and actual behavior, package versions, and `dotnet --info`. For a larger feature or breaking API change, open an issue first to discuss the design. Questions can also be opened as issues. No internal ticket or company account is required.

Do not include credentials, personal data, or private application code. Follow [SECURITY.md](SECURITY.md) for vulnerabilities and our [Code of Conduct](CODE_OF_CONDUCT.md) in all project spaces.

## Set up locally

Install Git and the .NET 10 SDK selected by [global.json](global.json). Bash and Python 3 are also needed for package verification; on Windows, use Git Bash or WSL for that script.

Fork the repository on GitHub, then clone your fork and branch from `develop`:

```sh
git clone https://github.com/YOUR-USERNAME/FSharp.MinimalApi.git
cd FSharp.MinimalApi
git remote add upstream https://github.com/ArgyleConcepts/FSharp.MinimalApi.git
git fetch upstream
git switch -c describe-your-change upstream/develop
dotnet restore FSharp.MinimalApi.sln
dotnet tool restore
```

Use a short branch name describing the change. `develop` is the integration branch and PR target; `master` is reserved for maintainer-managed release preparation. Do not push changes directly to either branch.

## Validate your changes

Run the same checks as Azure PR validation from the repository root:

```sh
dotnet build FSharp.MinimalApi.sln --configuration Release --no-restore
dotnet test --solution FSharp.MinimalApi.sln --configuration Release --no-build --no-restore
dotnet fantomas check .
bash eng/verify-packages.sh
```

Run `dotnet fantomas .` to apply formatting. The tests use xUnit v3 with Microsoft Testing Platform; keep the `--solution` option when specifying the solution. `./fake.sh test` additionally collects coverage and enforces a 90% line-coverage threshold per library assembly.

The repository contains the endpoint library (`FSharp.MinimalApi`), C# implementation helpers (`FSharp.MinimalApi.Interop`), optional schema support (`FSharp.MinimalApi.OpenApi`), tests (`FSharp.MinimalApi.Tests`), and a runnable example (`BasicApi`). Start the example with `dotnet run --project BasicApi`.

For behavior changes, add a regression test that demonstrates the problem or new behavior. Update examples and docs for public API changes. Follow existing F# conventions and `.editorconfig`; keep unrelated refactoring out of a focused fix.

## Open a pull request

Push your branch to your fork and open a PR against **ArgyleConcepts/FSharp.MinimalApi:develop**. Describe the problem, the resulting behavior, and the validation performed. Link related public issues and call out breaking changes. Draft PRs are welcome for early feedback.

The branch must be up to date, the **FSharp.MinimalApi PR Validation** check must pass, and review conversations must be resolved before merging. Reviews are encouraged; a second maintainer's approval is not mandatory. Maintainers may request changes before merging.

Fork PRs may wait for a maintainer to inspect the changes and authorize Azure validation. Contributors do not need Azure credentials. If validation does not start, mention it in the PR; maintainers should follow the [maintainer guide](docs/MAINTAINING.md).

## License and releases

Contributions are made under the project's existing [MIT license](LICENSE). Retain upstream attribution. Package publishing is a separate maintainer decision; PR validation builds and tests packages locally and never publishes them. See the README for current package availability.
