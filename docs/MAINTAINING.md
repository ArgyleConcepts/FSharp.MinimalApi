# Maintainer guide

## Branches and merging

- `develop` is the default branch and normal PR target.
- `master` is retained for release preparation. Promote reviewed changes through a PR; merging does not publish packages.
- Both branches require PRs and resolved review conversations, disallow force pushes and deletion, and apply protections to administrators. Code-owner approval is required, with no additional numeric reviewer quota.
- Both branches require the **FSharp.MinimalApi PR Validation** check from the Azure Pipelines GitHub App (app ID `9426`) with an up-to-date branch.
- The first promotion to `master` must include the updated `azure-pipelines.yml` from this setup so its PR triggers cover `master`. Until then, its historical YAML cannot satisfy the required check; keep normal contributions targeted at `develop`.

Keep merge commits available for promotion between long-lived branches. Squash or merge focused contribution PRs as appropriate. Do not delete `develop` after a promotion. Remove obsolete topic branches after verifying their work is merged and no open PR depends on them.

## Code ownership

[.github/CODEOWNERS](../.github/CODEOWNERS) assigns all files, including the ownership policy itself, to `@david-cyman-argyle`. Both protected branches enable required code-owner reviews with the generic approval count set to zero. GitHub reads ownership from the PR's base branch: merge this setup into `develop` and include it in the first promotion to `master` to activate ownership on each branch.

GitHub does not allow PR authors to approve their own PRs. `@david-cyman-argyle` has an explicit pull-request review bypass on both protected branches so he can merge his own PRs. This is a per-user exception, not a blanket admin exemption: required CI, an up-to-date branch, conversation resolution, and force-push/deletion protections still apply. Other contributors require code-owner approval. Keep using PRs for all changes.

## Access

Repository access is managed in GitHub Settings → Collaborators and teams. Public contributors use forks and need no write access. Use Triage for issue triage, Write for trusted code contributors, Maintain for routine repository administration, and Admin only for people responsible for permissions and security settings. Review access periodically and remove it when no longer needed. Current administrator assignments are preserved by this setup.

GitHub Actions has read-only default token permissions and cannot approve PRs. Grant additional workflow permissions only where needed. Keep publishing credentials out of PR validation and source control.

## Azure PR validation

[Pipeline 33](https://dev.azure.com/ArgyleConceptsLLC/Argyle%20Converge/_build?definitionId=33) uses `azure-pipelines.yml` and the ArgyleConcepts GitHub App service connection. Its default branch is `develop`. The YAML validates PRs targeting `develop` and `master`; it has no push or publishing trigger.

Fork builds must use Microsoft-hosted agents, have no secrets, and have no full-access job token. Require a maintainer comment before building fork PRs. Inspect changes, including build scripts and YAML, before authorizing them with `/AzurePipelines run`. This comment must be posted by a maintainer with Write access or above.

Check both pipeline Triggers and organization/project Pipelines Settings if fork validation does not start: centralized settings can override the pipeline. Do not resolve a blocked fork build by granting secrets or broad repository access. Verify the first real external contribution end to end; a same-repository PR does not prove fork authorization works. See [Azure's GitHub integration documentation](https://learn.microsoft.com/en-us/azure/devops/pipelines/repos/github#contributions-from-forks).

## Security and dependency maintenance

Keep private vulnerability reporting, Dependabot alerts/security updates, secret scanning, and push protection enabled. Triage security reports through private advisories. Dependency update PRs target `develop` and follow the same CI and review rules as other changes.

The upstream MIT notice must remain intact. Publishing packages requires a separate release decision and the package verification described in the README. Do not add release secrets to the PR pipeline.

## Preparing and publishing a beta

`Directory.Build.props` is the package-version authority; the historical `GitVersion.yml` is not used by PR validation or local packing. For this release, both packages must use `0.3.0-beta.1`.

1. Run the validation commands in the README, including `bash eng/verify-packages.sh`. The package check verifies repository commit metadata, symbol files, and generated SourceLink mappings, then runs a fresh consumer.
2. Merge the reviewed preparation PR into `develop`, then promote it to `master` through a PR with required Azure validation.
3. From the clean release commit, pack both projects using the README commands. Inspect the two `.nupkg` and two `.snupkg` files, their beta version, and source links before publishing. Preserve these exact artifacts for the release.
4. Confirm access to the intended Argyle Concepts NuGet organization and a publishing credential scoped to the two package IDs. Keep credentials outside source control and PR validation.
5. After the release decision, publish both packages and their symbols to NuGet using the approved release environment. Confirm both NuGet listings show `0.3.0-beta.1` and organization ownership.
6. Create a GitHub release tagged `0.3.0-beta.1` at the packaged commit, explicitly mark it as a prerelease, and attach the verified artifacts. Describe the .NET 10 requirement, included Interop assembly, optional OpenAPI package, and documented binding/AOT limitations.

Preparation and validation do not publish packages. A stable `0.3.0` release requires a later explicit version change and release decision.

## Azure NuGet release setup

The separate `azure-release.yml` pipeline has no push or PR triggers. Its default run validates and retains both package/symbol pairs without publishing or loading the secret group. Set `publishPackages=true` only for an approved manual run on `master`. The publish job downloads the validated artifacts without checking out or rebuilding source.

Set up these resources in the **Argyle Converge** Azure DevOps project:

- Create a YAML pipeline named **FSharp.MinimalApi Release** using the existing GitHub App service connection, this repository, and `azure-release.yml`. Set its default branch to `master` after release preparation is promoted there.
- Create an environment named `fsharp-minimalapi-nuget`. Add a maintainer approval check and a branch-control check allowing only protected `refs/heads/master`. Authorize only the release pipeline.
- Create a Library variable group named `fsharp-minimalapi-nuget-release`, with `NUGET_API_KEY` marked secret. Authorize only the release pipeline; do not enable access for all pipelines. Add the same protected-master branch-control check to the variable group.
- On NuGet.org, sign in with your individual account and create or select the Argyle Concepts organization. Create a short-lived API key with that organization selected as Package Owner, permission to push new packages and versions, and scope restricted to the two package IDs. For first publication, use the glob `ArgyleConcepts.FSharp.MinimalApi*` for this package family; it covers Core and OpenAPI, including any future packages with that prefix. Exclude unlisting permission. Paste the key directly into the Azure secret field and record its expiration for renewal.

First run the release pipeline with publishing disabled and inspect its `packages` artifact. After promotion and approval, manually run on `master` with publishing enabled. Package pushes automatically include adjacent symbols; explicit symbol pushes allow retry after a partial publish. Duplicate pushes are skipped. GitHub tagging and prerelease creation remain a separate step.

Never place the NuGet key in chat, a YAML parameter, a source file, or the PR validation pipeline.
