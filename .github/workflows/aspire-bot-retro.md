---
name: Aspire Bot Retro
description: Weekly retrospective of the Aspire Bot loop, with a report and one self-improvement pull request for human review.
on:
  schedule: weekly
  workflow_dispatch:
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
tools:
  github:
    toolsets: [default, actions]
  agentic-workflows: true
  edit:
  bash: ["*"]
  cache-memory: true
safe-outputs:
  create-issue:
    title-prefix: "[aspire-bot] Weekly retro: "
    labels: [aspire-bot, aspire-bot:retro]
    close-older-issues: true
    max: 1
  create-pull-request:
    title-prefix: "[aspire-bot] Retro: "
    labels: [aspire-bot, needs-human-review]
    draft: false
    max: 1
    if-no-changes: ignore
    allow-workflows: true
    protected-files: allowed
    allowed-files:
      - ".github/workflows/aspire-bot-*.md"
      - ".github/workflows/aspire-bot-*.lock.yml"
      - ".github/workflows/shared/aspire-bot.md"
    staged: ${{ vars.ASPIRE_BOT_MODE != 'active' }}
---

# Aspire Bot: weekly retrospective

You are the only Aspire Bot workflow allowed to propose changes to Aspire Bot itself, and
only through a pull request that a human must review. The control plane always classifies these
pull requests as high risk.

1. **Measure the last 7 days.** Use the agentic-workflows `logs` and `audit` tools and GitHub data.
   Collect the following:
   - Runs per Aspire Bot workflow and their outcomes: success, failure, noop, and skipped.
   - Pull requests opened, merged, and closed without merge, plus the merge rate by workflow.
   - Pull requests labeled `needs-human-review` and why they needed a human.
   - Pull requests that hit the fix-attempt limit.
   - Review verdicts that were later contradicted, such as an approved PR that failed CI or got
     reverted.
   - Recurring CI failures and open `aspire-bot:ci-failure` issues.
   - Token and AI cost per workflow, if the logs report it.
2. **Compare with last week.** Read `/tmp/gh-aw/cache-memory/retro.json`, then write this week's
   metrics to it.
3. **Report.** Open one issue titled with this week's date. It replaces last week's issue.
   Include a metrics table, the top 3 problems with evidence (run or PR links), and the top 3
   proposed improvements.
4. **Improve.** If one improvement is a clear, low-ambiguity prompt or configuration change to
   an Aspire Bot workflow:
   - Edit the `.md` source.
   - Run the agentic-workflows `compile` tool so the matching `.lock.yml` is regenerated.
   - Open one pull request that explains the evidence and the expected effect.
   - Never weaken safety controls. Don't remove `staged` mode gates, the emergency stop step, or the
     `environment`; don't widen `allowed-files`, add write permissions, change the top-level `if:`,
     or edit `policy.json`, the control plane, or `.github/policies`.

If there was no activity this week, still post the report and call `noop` for the pull request.
