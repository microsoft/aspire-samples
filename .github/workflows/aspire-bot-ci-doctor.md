---
name: Aspire Bot CI Doctor
description: Diagnoses failed CI runs on the default branch and opens a fix pull request or a tracking issue.
on:
  workflow_run:
    workflows: ["Aspire Samples CI"]
    types: [completed]
    branches: [main]
  workflow_dispatch:
    inputs:
      run_id:
        description: CI run ID to diagnose
        required: true
  # Emergency stop: skip while any open issue or pull request has the aspire-bot:stop label.
  steps:
    - name: Check the aspire-bot:stop emergency stop
      id: kill_switch
      continue-on-error: true
      uses: actions/github-script@3a2844b7e9c422d3c10d287c895573f7108da1b3 # v9.0.0
      with:
        script: |
          const { data } = await github.rest.issues.listForRepo({ ...context.repo, state: "open", labels: "aspire-bot:stop", per_page: 1 });
          if (data.length) core.setFailed(`Emergency stop: aspire-bot:stop is on #${data[0].number}.`);
if: >-
  needs.pre_activation.outputs.kill_switch_result == 'success' &&
  (vars.ASPIRE_BOT_MODE == 'active' || vars.ASPIRE_BOT_MODE == 'paused') &&
  (github.event_name == 'workflow_dispatch' ||
   (github.event.workflow_run.conclusion == 'failure' &&
    github.event.workflow_run.event != 'pull_request' &&
    github.event.workflow_run.head_repository.full_name == github.repository))
# The App credentials are secrets of this environment, which only the default branch can use.
environment:
  name: aspire-bot
  deployment: false
permissions:
  contents: read
  issues: read
  pull-requests: read
  actions: read
  copilot-requests: write
engine: copilot
strict: true
timeout-minutes: 45
imports:
  - shared/aspire-bot.md
concurrency:
  # Every workflow that can open a pull request shares one queue, so each run checks the open
  # pull request limit while nothing else is publishing.
  group: aspire-bot-publish
  cancel-in-progress: false
  queue: max
  job-discriminator: ${{ github.run_id }}
network:
  allowed: [defaults, github, dotnet, node, python, go, java]
tools:
  github:
    toolsets: [default, actions]
  edit:
  bash: ["*"]
  cache-memory: true
steps:
  - name: Set up .NET
    uses: actions/setup-dotnet@0f4a354dd5547a28c70b7853dbd95d5b67886e73 # v6.1.0
    with:
      global-json-file: ./build/global.json
safe-outputs:
  create-pull-request:
    title-prefix: "[aspire-bot] "
    labels: [aspire-bot]
    draft: false
    max: 1
    if-no-changes: ignore
    allow-workflows: true
    protected-files: allowed
    allowed-files:
      - "samples/**"
      - "tests/**"
      - ".github/workflows/ci.yml"
    staged: ${{ vars.ASPIRE_BOT_MODE != 'active' }}
  create-issue:
    title-prefix: "[aspire-bot] CI failure: "
    labels: [aspire-bot, aspire-bot:ci-failure]
    max: 1
---

# Aspire Bot: CI doctor

A run of **Aspire Samples CI** on `main` failed. The run ID is
`${{ github.event.workflow_run.id || github.event.inputs.run_id }}`.

1. Read the failed jobs' logs and identify the root cause. Classify it as one of:
   - **flaky/infra**: network, runner, registry, or timeout noise with no code cause
   - **regression**: a recent commit broke a sample or test
   - **external drift**: an upstream image, package, SDK, or service changed
2. Deduplicate. Check `/tmp/gh-aw/cache-memory/ci-doctor.json` and open issues labeled
   `aspire-bot:ci-failure` for the same root cause. If one exists, do nothing else (call `noop`).
3. Act:
   - **flaky/infra**: record it in cache memory. Open an issue only if the same signature has
     failed 3 or more times in 7 days.
   - **regression** or **external drift** with a small, confident fix: implement it, build the
     affected sample to validate, and open one pull request that links the failed run.
   - Otherwise: open one issue with the failing job, the key log excerpt (at most 30 lines), the
     suspected cause, and suggested next steps.
4. Update `/tmp/gh-aw/cache-memory/ci-doctor.json` with the signature, date, and action taken.
