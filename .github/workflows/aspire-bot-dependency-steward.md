---
name: Aspire Bot Dependency Steward
description: Turns Dependabot and code scanning alerts, and failing Dependabot pull requests, into small fix pull requests.
on:
  schedule: daily on weekdays
  workflow_dispatch:
  skip-if-match:
    query: "is:pr is:open label:aspire-bot"
    max: 3
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
  (vars.ASPIRE_BOT_MODE == 'active' || vars.ASPIRE_BOT_MODE == 'paused')
# The App credentials are secrets of this environment, which only the default branch can use.
environment:
  name: aspire-bot
  deployment: false
permissions:
  contents: read
  issues: read
  pull-requests: read
  actions: read
  security-events: read
  vulnerability-alerts: read
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
    toolsets: [default, actions, dependabot, code_security]
  edit:
  bash: ["*"]
  cache-memory: true
steps:
  - name: Set up .NET
    uses: actions/setup-dotnet@0f4a354dd5547a28c70b7853dbd95d5b67886e73 # v6.1.0
    with:
      global-json-file: ./build/global.json
  - name: Set up Node.js
    uses: actions/setup-node@949feb2413d6458794dcd2491c4babbbce0c15c1 # v7.1.0
    with:
      node-version: "24"
safe-outputs:
  create-pull-request:
    title-prefix: "[aspire-bot] "
    labels: [aspire-bot, dependencies]
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
  add-comment:
    target: "*"
    max: 3
    staged: ${{ vars.ASPIRE_BOT_MODE != 'active' }}
---

# Aspire Bot: dependency steward

Keep the samples' dependencies secure and current, in this priority order:

1. **Open Dependabot pull requests with failing CI.** Find open PRs authored by
   `dependabot[bot]` whose `CI Gate` check failed. Diagnose the failure from the job logs.
   If a small companion change makes it pass (for example, an API rename in sample code, a
   matching version bump in a sibling project, or a regenerated lockfile), open one pull request
   with that change and comment on the Dependabot PR linking to it. If you cannot fix it, comment
   once with your diagnosis and stop.
2. **Dependabot alerts** (open, highest severity first) that Dependabot has not already opened a PR
   for. Bump the vulnerable package to the minimum patched version in every affected manifest
   (`*.csproj`, `Directory.Packages.props`, `package.json` and its lockfile, `go.mod`/`go.sum`,
   `requirements.txt`/`pyproject.toml`, `pom.xml`).
3. **Code scanning alerts** (open, error or warning) in sample code that have a clear, local fix.

Rules:

- Open **at most one** pull request per run, focused on one package or alert.
- Check `/tmp/gh-aw/cache-memory/dependency-steward.json` first and skip items you already
  attempted in the last 7 days. Record what you attempted (item ID, date, outcome) before
  finishing.
- Search for an existing open pull request for the same package or alert before opening one.
- Validate by building the affected sample (`dotnet build` on its AppHost, `npm ci && npm run build`
  for JavaScript apps). Report the exact commands and results in the PR's Validation section.
- Version-only changes are low risk under `policy.json`. Avoid code changes unless they are
  required, and call them out in the Risk section when you make them.
- If nothing is actionable, call `noop`.
