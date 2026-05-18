# Agentic Workflows

This repository uses [GitHub Agentic Workflows (`gh aw`)](https://github.github.com/gh-aw/)
to run a small suite of AI-powered automations alongside the standard CI workflows.
Each workflow is authored as a Markdown file with YAML frontmatter and compiled to a
sibling `*.lock.yml` GitHub Actions file by `gh aw compile`. Both files are committed.

The set is intentionally narrow and modeled on patterns from [Peli's Agent Factory](https://github.github.com/gh-aw/blog/2026-01-12-welcome-to-pelis-agent-factory/).

## Workflow catalog

| Workflow | Source | Trigger | Effect |
|---|---|---|---|
| [`issue-triage`](./issue-triage.md) | `githubnext/agentics/workflows/issue-triage` | `issues: [opened, reopened]` | Labels, comments on, types, or closes the triggering issue (spam only). |
| [`ci-doctor`](./ci-doctor.md) | `githubnext/agentics/workflows/ci-doctor` | `workflow_run` on `["Aspire Samples CI"]` completed on `main` | Files a `[ci-doctor]` issue (labels `automation`, `ci`) with root-cause analysis when CI fails. |
| [`doc-updater`](./doc-updater.md) | `githubnext/agentics/workflows/doc-updater` *(retuned)* | `schedule: daily` + `workflow_dispatch` | Opens a `[docs]` pull request that updates **only** `samples/*/README.md` files whose corresponding sample folder had material code changes in the last 24 hours. Strictly does not touch the root `README.md`, `CODE_OF_CONDUCT.md`, `SECURITY.md`, `LICENSE`, or anything outside `samples/`. |
| [`malicious-code-scan`](./malicious-code-scan.md) | `githubnext/agentics/workflows/malicious-code-scan` | `schedule: daily` + `workflow_dispatch` | Reviews the last 3 days of code changes for supply-chain / exfiltration patterns and files Code Scanning alerts to the Security tab (not the issue tracker). |

A fifth workflow, `agentics-maintenance.yml`, is generated automatically by
`gh aw compile` because `doc-updater` uses the `expires` field; it sweeps expired
draft items on a schedule and should be left alone.

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
gh aw disable issue-triage ci-doctor doc-updater malicious-code-scan
```

Re-enable with `gh aw enable <name>`.

## Why these four?

These map to the most-trafficked patterns from the agent factory tour: triage,
fault investigation, documentation hygiene, and security compliance. Other useful
agents (`daily-repo-status`, `pr-fix`, ChatOps-style assistants, etc.) can be
added later via `gh aw add githubnext/agentics/<workflow-name>` and are
intentionally deferred until this baseline is proven.
