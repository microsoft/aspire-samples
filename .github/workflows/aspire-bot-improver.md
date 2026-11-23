---
name: Aspire Bot Improver
description: Implements maintainer-approved issues and keeps samples, docs, and tests fresh, one small pull request at a time.
on:
  schedule: daily on weekdays
  workflow_dispatch:
  skip-if-match:
    query: "is:pr is:open label:aspire-bot"
    max: 3
if: vars.ASPIRE_BOT_MODE == 'active' || vars.ASPIRE_BOT_MODE == 'paused'
permissions:
  contents: read
  issues: read
  pull-requests: read
  actions: read
  copilot-requests: write
engine: copilot
strict: true
timeout-minutes: 60
imports:
  - shared/aspire-bot.md
concurrency:
  group: aspire-bot-improver
  cancel-in-progress: false
  job-discriminator: ${{ github.run_id }}
network:
  allowed: [defaults, github, dotnet, node, python, go, java]
tools:
  github:
    toolsets: [default, actions]
    # Only maintainer-trusted content (or issues a maintainer labeled aspire-bot:approved) can drive changes.
    min-integrity: approved
    approval-labels: [aspire-bot:approved]
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
    labels: [aspire-bot]
    draft: false
    max: 1
    if-no-changes: ignore
    protected-files: allowed
    allowed-files:
      - "samples/**"
      - "tests/**"
      - "README.md"
    staged: ${{ vars.ASPIRE_BOT_MODE != 'active' }}
  add-comment:
    target: "*"
    max: 1
    staged: ${{ vars.ASPIRE_BOT_MODE != 'active' }}
---

# Aspire Bot: improver

Make the repository a little better with **one** focused pull request, or do nothing.

Choose the first available item from this list:

1. **Approved issues.** These are open issues labeled both `aspire-bot:ready` and
   `aspire-bot:approved`, not `aspire-bot:hold`, and with no open pull request that links them.
   Implement the issue and reference it with `Fixes #N`.
2. **Sample freshness.** A sample README that has drifted from its code: commands, ports, file
   names, or resource names that no longer match.
3. **Test gaps.** A sample with no coverage in `tests/SamplesIntegrationTests` where an existing
   test pattern can be applied directly.

Rules:

- Read `/tmp/gh-aw/cache-memory/improver.json` and don't retry items attempted in the last
  14 days. Record your choice and outcome before finishing.
- Search for existing open pull requests or issues covering the same item before starting.
- Keep it small: at most about 300 changed lines, all for one sample or one issue.
- Changes to sample code are high risk and need a human. That's fine; explain the change
  clearly in the Risk section. Documentation-only changes are low risk.
- Build the affected sample (and run its tests if they exist) and report the results.
- When you finish an approved issue, comment on it with a link to the pull request.
- If nothing is worth doing, call `noop`.
