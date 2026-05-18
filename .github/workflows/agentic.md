# Agentic Workflows

This repository uses [GitHub Agentic Workflows (`gh aw`)](https://github.github.com/gh-aw/)
to run a small suite of AI-powered automations alongside the standard CI workflows.
Each workflow is authored as a Markdown file with YAML frontmatter and compiled to a
sibling `*.lock.yml` GitHub Actions file by `gh aw compile`. Both files are committed.

The set is intentionally narrow and modeled on patterns from [Peli's Agent Factory](https://github.github.com/gh-aw/blog/2026-01-12-welcome-to-pelis-agent-factory/).

## Workflow catalog

| Workflow | Source | Trigger | Effect |
|---|---|---|---|
| [`issue-triage`](./issue-triage.md) | `githubnext/agentics/workflows/issue-triage` *(retuned)* | `issues: [opened, reopened]` | Labels, comments on, types, or closes the triggering issue (spam only). Rubric tightened to the repo's actual label set — see the workflow source. |
| [`backlog-triage`](./backlog-triage.md) | Custom (this repo) | `schedule: weekly on monday` + `workflow_dispatch` | Picks up to 10 oldest open issues with no area / type label and no prior triage comment, then applies the `issue-triage` rubric to each. Idempotent via a `<!-- backlog-triage -->` marker comment. |
| [`ci-doctor`](./ci-doctor.md) | `githubnext/agentics/workflows/ci-doctor` | `workflow_run` on `["Aspire Samples CI"]` completed on `main` | Files a `[ci-doctor]` issue (labels `automation`, `ci`) with root-cause analysis when CI fails. **Only investigates main-branch runs — PR-CI failures are out of scope.** |
| [`doc-updater`](./doc-updater.md) | `githubnext/agentics/workflows/doc-updater` *(retuned)* | `schedule: weekly on monday` + `workflow_dispatch` | Opens a `[docs]` pull request that updates **only** `samples/*/README.md` files whose corresponding sample folder had material code changes in the last week. Strictly does not touch the root `README.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `LICENSE`, or anything outside `samples/`. |
| [`malicious-code-scan`](./malicious-code-scan.md) | `githubnext/agentics/workflows/malicious-code-scan` | `schedule: daily` + `workflow_dispatch` | Reviews the last 3 days of code changes for supply-chain / exfiltration patterns and files Code Scanning alerts to the Security tab (not the issue tracker). |

A sixth workflow, `agentics-maintenance.yml`, is generated automatically by
`gh aw compile` because `doc-updater` uses the `expires` field; it sweeps expired
draft items on a schedule and should be left alone.

## Known caveats

- **`set-issue-type` may silently no-op.** This safe output sets a repo-level
  *issue type* (Bug/Feature/Task), not a label. It only works if the parent
  organization has issue types configured and exposed to this repo. If the org
  hasn't enabled them, the call fails silently and the agent falls back to a type
  *label* (`bug` / `enhancement` / `documentation`) per the rubric — this is
  intentional and not a bug.
- **`ci-doctor` only investigates `main` failures.** PR-CI failures are
  deliberately out of scope: the doctor's job is to alert maintainers when the
  protected branch breaks, not to comment on every red PR. If you want a per-PR
  fix-it agent, add `pr-fix` (`gh aw add githubnext/agentics/workflows/pr-fix`) in
  a follow-up PR.
- **`malicious-code-scan` requires Code Scanning to be enabled.** The workflow
  publishes alerts via the `create-code-scanning-alert` safe output, which uses
  the GitHub Code Scanning API. Microsoft repos generally have Advanced Security
  (and therefore Code Scanning) enabled. If alerts don't appear in the Security
  tab after a run, confirm Code Scanning is on for the repo before assuming a
  workflow bug.

## Authentication model

Two credential systems are involved. Both must be configured by a repository
administrator before any workflow can run successfully.

### 1. AI engine — Copilot (`COPILOT_GITHUB_TOKEN`)

All four workflows use the `copilot` engine. Copilot requires a fine-grained
Personal Access Token with the `Copilot Requests: Read` permission, stored as a
repository secret named `COPILOT_GITHUB_TOKEN`. Per the `gh aw` auth model this
secret **cannot** be a GitHub App token; it must be a PAT owned by a user with an
active Copilot license. See
[gh-aw auth reference](https://github.github.com/gh-aw/reference/auth/#copilot_github_token)
for the setup link with the correct permission pre-selected.

### 2. GitHub tools + safe-outputs — `ASPIRE_BOT` GitHub App

All write-back operations (labels, comments, issues, PRs, code-scanning alerts) go
through a dedicated GitHub App, `ASPIRE_BOT`, using
[`actions/create-github-app-token`](https://github.com/actions/create-github-app-token)
to mint a short-lived, scope-minimized token per job. Two repository secrets:

| Secret | What it is |
|---|---|
| `ASPIRE_BOT_APP_ID` | The App ID (or Client ID) of the `ASPIRE_BOT` GitHub App. |
| `ASPIRE_BOT_PRIVATE_KEY` | The PEM-encoded private key for the App. |

The `ASPIRE_BOT` App must be **installed on this repository** with at minimum:

- `Contents`: Read & write (for `doc-updater`'s PR branch pushes)
- `Issues`: Read & write (for `issue-triage` and `ci-doctor`)
- `Pull requests`: Read & write (for `doc-updater`)
- `Metadata`: Read
- `Code scanning alerts`: Read & write (for `malicious-code-scan`)
- `Actions`: Read (for `ci-doctor` reading workflow run logs)

## Running locally

The `gh aw` CLI is required for development:

```bash
gh extension install github/gh-aw
```

Useful commands when editing a workflow:

```bash
gh aw validate                 # schema-check all workflows
gh aw compile                  # regenerate *.lock.yml files (commit both)
gh aw status                   # show registered workflows and last-run info
gh aw run <workflow-name>      # trigger a workflow on GitHub Actions
gh aw audit <run-id-or-url>    # download logs and generate a report
```

`*.lock.yml` files are marked `linguist-generated=true merge=ours` in
`.gitattributes`. Do not edit them by hand — re-run `gh aw compile` after changing
the source `.md`.

## Customizing a workflow

1. Edit the relevant `.github/workflows/<name>.md`. The frontmatter (between the
   `---` markers) controls triggers, permissions, tools, safe-outputs, and the
   engine. The body below is the natural-language prompt.
2. Run `gh aw compile --approve` from the repo root.
3. Commit **both** the `.md` and the regenerated `.lock.yml`.

For `ci-doctor`, the list of workflows it watches lives in
`ci-doctor.md` under `on.workflow_run.workflows`. Add more workflow `name:` values
there if you want the doctor to investigate failures of other workflows on `main`.

## Disabling the suite

To temporarily silence the agents without removing them:

```bash
gh aw disable issue-triage backlog-triage ci-doctor doc-updater malicious-code-scan
```

Re-enable with `gh aw enable <name>`.

## Why these workflows?

These map to the most-trafficked patterns from the agent factory tour: triage
(both new issues and the stale backlog), fault investigation, documentation
hygiene, and security compliance. Other useful agents (`daily-repo-status`,
`pr-fix`, ChatOps-style assistants, etc.) can be added later via
`gh aw add githubnext/agentics/<workflow-name>` and are intentionally deferred
until this baseline is proven.
