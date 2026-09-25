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

GitHub does not allow PR authors to approve their own PRs. With a single code owner and protections enforced for admins, David-authored PRs need another eligible code owner or a deliberate policy change before they can merge. Do not silently bypass the policy or add another owner without agreement.

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
