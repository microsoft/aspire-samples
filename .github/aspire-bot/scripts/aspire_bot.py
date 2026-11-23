#!/usr/bin/env python3
"""Aspire Bot control plane.

This script is the deterministic (non-AI) authority of the Aspire Bot loop:

* resolves the kill switch (``ASPIRE_BOT_MODE``: active | paused | stopped),
* classifies pull requests into risk tiers from their changed files,
* decides whether bot and Dependabot pull requests may merge, and merges them
  atomically: the squash merge is pinned to the exact head SHA that was
  classified, reviewed, and passed CI Gate, so a later push can't ride along,
* removes the persistent ``auto-merge`` label from bot and Dependabot pull requests,
* keeps the "Aspire Bot status" issue current.

No language model output can trigger a merge. The agents only contribute a
SHA-bound review verdict that this script verifies.
"""

from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import urllib.parse
from collections import Counter
from dataclasses import dataclass, field
from datetime import datetime, timezone
from pathlib import Path

POLICY_PATH = Path(__file__).resolve().parents[1] / "policy.json"
STATUS_TITLE = "Aspire Bot status"
REVIEWED_SHA_PATTERN = re.compile(r"(?i)[*_`]*aspire-bot-reviewed-sha:\s*`?([0-9a-f]{40})`?[*_`]*")
MODES = ("active", "paused", "stopped")


def load_policy(path: Path = POLICY_PATH) -> dict:
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def resolve_mode(raw: str | None) -> str:
    """Return the effective mode. Anything other than active/paused fails closed to stopped."""
    value = (raw or "").strip().lower()
    return value if value in MODES else "stopped"


def glob_to_regex(pattern: str) -> re.Pattern:
    """Translate a path glob (``**``, ``*``, ``?``) into an anchored regex."""
    out = []
    i = 0
    while i < len(pattern):
        if pattern.startswith("**/", i):
            out.append("(?:.*/)?")
            i += 3
        elif pattern.startswith("**", i):
            out.append(".*")
            i += 2
        elif pattern[i] == "*":
            out.append("[^/]*")
            i += 1
        elif pattern[i] == "?":
            out.append("[^/]")
            i += 1
        else:
            out.append(re.escape(pattern[i]))
            i += 1
    return re.compile("^" + "".join(out) + "$")


def match_rule(path: str, rules: list[dict]) -> dict | None:
    for rule in rules:
        if glob_to_regex(rule["glob"]).match(path):
            return rule
    return None


def patch_is_version_only(patch: str | None, patterns: list[str]) -> bool:
    """True when every added line equals a removed line once version text is masked."""
    if not patch:
        return False
    compiled = [re.compile(p) for p in patterns]

    def mask(line: str) -> str:
        for regex in compiled:
            line = regex.sub("<version>", line)
        return line

    added: Counter = Counter()
    removed: Counter = Counter()
    for line in patch.splitlines():
        if line.startswith("@@") or line.startswith("\\"):
            continue
        if line.startswith("+"):
            added[mask(line[1:])] += 1
        elif line.startswith("-"):
            removed[mask(line[1:])] += 1
    return bool(added or removed) and added == removed


def patch_sources_are_trusted(patch: str | None, forbidden: list[str]) -> bool:
    """True when no added lockfile line matches a forbidden (untrusted source) pattern."""
    if not patch:
        return False
    compiled = [re.compile(p) for p in forbidden]
    return not any(
        regex.search(line[1:])
        for line in patch.splitlines() if line.startswith("+") and not line.startswith("+++")
        for regex in compiled
    )


@dataclass
class Classification:
    tier: str
    reasons: list[str] = field(default_factory=list)


def classify(files: list[dict], policy: dict) -> Classification:
    """Classify a pull request from its changed files (GitHub ``pulls/N/files`` shape)."""
    rules = policy["riskTiers"]["rules"]
    max_files = policy["limits"]["maxLowRiskFiles"]
    reasons: list[str] = []
    if not files:
        return Classification("high", ["no changed files reported"])
    if len(files) > max_files:
        reasons.append(f"{len(files)} files changed (low-risk limit is {max_files})")

    for entry in files:
        paths = [entry["filename"]]
        if entry.get("previous_filename"):
            paths.append(entry["previous_filename"])
        for path in paths:
            rule = match_rule(path, rules)
            if rule is None:
                reasons.append(f"`{path}`: not covered by a low-risk rule")
            elif rule["tier"] == "high":
                reasons.append(f"`{path}`: {rule['reason']}")
            elif rule["tier"] == "version-only":
                if entry.get("status") != "modified":
                    reasons.append(f"`{path}`: {entry.get('status')} (only modifications can be version-only)")
                elif not patch_is_version_only(entry.get("patch"), rule.get("patterns", [])):
                    reasons.append(f"`{path}`: changes more than versions")
            elif rule["tier"] == "lockfile":
                if not patch_sources_are_trusted(entry.get("patch"), rule.get("forbidden", [])):
                    reasons.append(f"`{path}`: lockfile diff unavailable or references an untrusted source")
            elif rule["tier"] != "any":
                reasons.append(f"`{path}`: unknown tier `{rule['tier']}`")

    return Classification("high" if reasons else "low", reasons)


def parse_verdict_footer(body: str, policy: dict) -> tuple[str, str] | None:
    """Parse the verdict footer: the last two non-empty lines, each marker appearing exactly once.

    Any extra marker anywhere in the body (for example, quoted pull request text) makes the
    review ambiguous, and it is ignored.
    """
    lowered = body.lower()
    if lowered.count("aspire-bot-verdict") != 1 or lowered.count("aspire-bot-reviewed-sha") != 1:
        return None
    lines = [line for line in body.strip().splitlines() if line.strip()]
    if len(lines) < 2:
        return None
    verdict = re.compile(policy["verdict"]["pattern"]).fullmatch(lines[-2].strip())
    reviewed = REVIEWED_SHA_PATTERN.fullmatch(lines[-1].strip())
    if not verdict or not reviewed:
        return None
    return verdict.group(1).lower(), reviewed.group(1)


def latest_verdict(reviews: list[dict], bot_login: str, head_sha: str, policy: dict) -> str | None:
    """Return the verdict of the newest bot review on ``head_sha`` if it is bound to that SHA."""
    candidates = [
        r for r in reviews
        if (r.get("user") or {}).get("login", "").lower() == bot_login.lower()
        and r.get("commit_id") == head_sha
    ]
    if not candidates:
        return None
    newest = max(candidates, key=lambda r: r.get("submitted_at") or "")
    parsed = parse_verdict_footer(newest.get("body") or "", policy)
    if parsed and parsed[1] == head_sha:
        return parsed[0]
    return None


@dataclass
class Decision:
    kind: str
    merge: bool | None
    reasons: list[str]
    add_labels: list[str] = field(default_factory=list)
    remove_labels: list[str] = field(default_factory=list)
    needs_human: bool = False


def author_kind(login: str, bot_login: str, policy: dict) -> str:
    if login.lower() == bot_login.lower():
        return "bot"
    if login.lower() == policy["trustedAuthors"]["dependabot"].lower():
        return "dependabot"
    return "human"


def decide(pr: dict, mode: str, classification: Classification | None, verdict: str | None,
           fix_attempts: int, bot_login: str, policy: dict, default_branch: str = "main") -> Decision:
    labels = policy["labels"]
    current = {label["name"] for label in pr.get("labels", [])}
    kind = author_kind(pr["user"]["login"], bot_login, policy)
    auto_merge = labels["autoMerge"]["name"]

    if kind == "human":
        return Decision(kind, None, ["human-authored pull requests are never merged or relabeled by Aspire Bot"])

    reasons: list[str] = []
    needs_human = False
    if mode != "active":
        reasons.append(f"mode is `{mode}`")
    if pr.get("draft"):
        reasons.append("draft")
    if pr["base"]["ref"] != default_branch:
        reasons.append(f"targets `{pr['base']['ref']}`")
    if labels["hold"]["name"] in current:
        reasons.append(f"`{labels['hold']['name']}` label")
    if classification is not None and classification.tier != "low":
        reasons.append("high risk tier")
        needs_human = True
    if kind == "bot":
        if verdict != policy["verdict"]["approve"]:
            reasons.append(f"no reviewer approval on the head commit (verdict: {verdict or 'none'})")
        max_attempts = policy["limits"]["maxFixAttemptsPerPullRequest"]
        if fix_attempts >= max_attempts and verdict != policy["verdict"]["approve"]:
            reasons.append(f"{fix_attempts} fix attempts reached the limit of {max_attempts}")
            needs_human = True
    elif verdict == policy["verdict"]["requestChanges"]:
        reasons.append("reviewer requested changes on the head commit")
        needs_human = True

    decision = Decision(kind, not reasons, reasons or ["eligible"], needs_human=needs_human and mode != "stopped")
    # The persistent label would authorize future pushes too; merges are SHA-pinned by reconcile().
    if auto_merge in current:
        decision.remove_labels.append(auto_merge)

    if mode != "stopped" and classification is not None:
        want, drop = (labels["tierLow"]["name"], labels["tierHigh"]["name"])
        if classification.tier != "low":
            want, drop = drop, want
        if want not in current:
            decision.add_labels.append(want)
        if drop in current:
            decision.remove_labels.append(drop)
        if decision.needs_human and labels["needsHuman"]["name"] not in current:
            decision.add_labels.append(labels["needsHuman"]["name"])
    return decision


class GitHub:
    """Minimal ``gh api`` wrapper. Mutations are printed instead of executed in dry-run mode."""

    def __init__(self, repo: str, dry_run: bool):
        self.repo = repo
        self.dry_run = dry_run

    def _run(self, args: list[str], payload: dict | None = None) -> str:
        result = subprocess.run(
            ["gh", "api", *args] + (["--input", "-"] if payload is not None else []),
            input=json.dumps(payload) if payload is not None else None,
            capture_output=True, text=True, check=False,
        )
        if result.returncode != 0:
            raise RuntimeError(f"gh api {' '.join(args)} failed: {result.stderr.strip()}")
        return result.stdout

    def get(self, path: str, paginate: bool = False):
        if paginate:
            pages = json.loads(self._run(["--paginate", "--slurp", path]) or "[]")
            return [item for page in pages for item in page]
        return json.loads(self._run([path]) or "null")

    def write(self, method: str, path: str, payload: dict | None = None):
        if self.dry_run:
            print(f"[dry-run] {method} {path} {json.dumps(payload) if payload else ''}")
            return None
        output = self._run(["-X", method, path], payload)
        return json.loads(output) if output.strip() else None


def ensure_labels(gh: GitHub, policy: dict) -> None:
    existing = {label["name"].lower() for label in gh.get(f"repos/{gh.repo}/labels?per_page=100", paginate=True)}
    for label in policy["labels"].values():
        if label["name"].lower() not in existing:
            gh.write("POST", f"repos/{gh.repo}/labels",
                     {"name": label["name"], "color": label["color"], "description": label["description"]})


def required_check_state(gh: GitHub, sha: str, name: str) -> str:
    """Return ``success``, ``failure``, or ``pending`` for the newest run of a check on ``sha``."""
    data = gh.get(f"repos/{gh.repo}/commits/{sha}/check-runs?check_name={urllib.parse.quote(name)}&filter=latest")
    runs = (data or {}).get("check_runs") or []
    if not runs:
        return "pending"
    newest = max(runs, key=lambda r: r.get("started_at") or "")
    if newest.get("status") != "completed":
        return "pending"
    return "success" if newest.get("conclusion") == "success" else "failure"


def merge_pinned(gh: GitHub, pr: dict) -> str:
    """Squash-merge only if the head is still the SHA that was validated (GitHub returns 409 otherwise)."""
    try:
        gh.write("PUT", f"repos/{gh.repo}/pulls/{pr['number']}/merge",
                 {"sha": pr["head"]["sha"], "merge_method": "squash"})
        return "merged"
    except RuntimeError as error:
        print(f"::warning::Merge of #{pr['number']} at {pr['head']['sha']} was refused: {error}")
        return "merge refused"


def count_fix_attempts(commits: list[dict], marker: str) -> int:
    return sum(1 for c in commits if marker in ((c.get("commit") or {}).get("message") or ""))


def render_status(mode: str, rows: list[dict], repo: str) -> str:
    lines = [
        f"# {STATUS_TITLE}",
        "",
        f"**Mode:** `{mode}` (set the `ASPIRE_BOT_MODE` repository variable to `active`, `paused`, or `stopped`).",
        "",
        "This issue is maintained by the Aspire Bot control plane. "
        "See [.github/aspire-bot/README.md](../blob/main/.github/aspire-bot/README.md) for how the loop works.",
        "",
        "## Tracked pull requests",
        "",
    ]
    if rows:
        lines += ["| PR | Author | Tier | Verdict | Fix attempts | Merge | Why |", "|---|---|---|---|---|---|---|"]
        for row in rows:
            lines.append(
                f"| #{row['number']} | {row['kind']} | {row['tier']} | {row['verdict'] or '-'} | "
                f"{row['attempts']} | {row['merge']} | {'; '.join(row['reasons'])} |"
            )
    else:
        lines.append("No open bot or Dependabot pull requests.")
    lines += ["", f"[Workflow runs](https://github.com/{repo}/actions?query=workflow%3A%22Aspire+Bot%22)"]
    return "\n".join(lines) + "\n"


def upsert_status_issue(gh: GitHub, policy: dict, body: str) -> None:
    label = policy["labels"]["status"]["name"]
    issues = gh.get(f"repos/{gh.repo}/issues?state=open&labels={urllib.parse.quote(label)}&per_page=10")
    issues = [i for i in issues or [] if "pull_request" not in i]
    if not issues:
        gh.write("POST", f"repos/{gh.repo}/issues", {"title": STATUS_TITLE, "body": body, "labels": [label]})
    elif (issues[0].get("body") or "").strip() != body.strip():
        gh.write("PATCH", f"repos/{gh.repo}/issues/{issues[0]['number']}", {"body": body})


def explain_needs_human(decision: Decision, classification: Classification | None) -> str:
    lines = ["Aspire Bot will not auto-merge this pull request. A maintainer needs to review it.", ""]
    for reason in decision.reasons:
        lines.append(f"- {reason}")
    if classification and classification.reasons:
        lines += ["", "<details><summary>Risk tier details</summary>", ""]
        lines += [f"- {reason}" for reason in classification.reasons[:50]]
        lines += ["", "</details>"]
    return "\n".join(lines)


def reconcile(args: argparse.Namespace) -> int:
    policy = load_policy()
    mode = resolve_mode(os.environ.get(policy["modeVariable"]))
    repo = os.environ["GITHUB_REPOSITORY"]
    bot_login = args.bot_login or os.environ.get("ASPIRE_BOT_LOGIN") or ""
    if not bot_login:
        print("::error::Bot login is unknown; pass --bot-login or set ASPIRE_BOT_LOGIN.")
        return 1
    gh = GitHub(repo, args.dry_run)
    default_branch = gh.get(f"repos/{repo}")["default_branch"]
    print(f"Mode: {mode} | bot: {bot_login} | dry-run: {args.dry_run}")

    if mode != "stopped":
        ensure_labels(gh, policy)

    if args.pr:
        prs = [gh.get(f"repos/{repo}/pulls/{args.pr}")]
        prs = [p for p in prs if p and p.get("state") == "open"]
    else:
        prs = gh.get(f"repos/{repo}/pulls?state=open&per_page=100", paginate=True)

    rows = []
    summary = [f"## Aspire Bot control plane (`{mode}`)", ""]
    for pr in prs:
        kind = author_kind(pr["user"]["login"], bot_login, policy)
        if kind == "human":
            continue
        number = pr["number"]
        classification = verdict = None
        attempts = 0
        if mode != "stopped":
            files = gh.get(f"repos/{repo}/pulls/{number}/files?per_page=100", paginate=True)
            classification = classify(files, policy)
            reviews = gh.get(f"repos/{repo}/pulls/{number}/reviews?per_page=100", paginate=True)
            verdict = latest_verdict(reviews, bot_login, pr["head"]["sha"], policy)
            if kind == "bot":
                commits = gh.get(f"repos/{repo}/pulls/{number}/commits?per_page=100", paginate=True)
                attempts = count_fix_attempts(commits, policy["limits"]["fixCommitMarker"])
        decision = decide(pr, mode, classification, verdict, attempts, bot_login, policy, default_branch)

        newly_needs_human = policy["labels"]["needsHuman"]["name"] in decision.add_labels
        for name in decision.remove_labels:
            gh.write("DELETE", f"repos/{repo}/issues/{number}/labels/{urllib.parse.quote(name, safe='')}")
        if decision.add_labels:
            gh.write("POST", f"repos/{repo}/issues/{number}/labels", {"labels": decision.add_labels})
        if newly_needs_human:
            gh.write("POST", f"repos/{repo}/issues/{number}/comments",
                     {"body": explain_needs_human(decision, classification)})

        merge = "-"
        if decision.merge:
            gate = required_check_state(gh, pr["head"]["sha"], policy["requiredCheck"])
            merge = merge_pinned(gh, pr) if gate == "success" else f"waiting ({policy['requiredCheck']}: {gate})"
        rows.append({
            "number": number, "kind": kind, "tier": classification.tier if classification else "-",
            "verdict": verdict, "attempts": attempts, "merge": merge, "reasons": decision.reasons,
        })
        summary.append(f"- #{number} ({kind}): tier={rows[-1]['tier']} verdict={verdict or '-'} "
                       f"merge={merge} +{decision.add_labels} -{decision.remove_labels} :: {'; '.join(decision.reasons)}")

    if mode != "stopped" and not args.pr:
        upsert_status_issue(gh, policy, render_status(mode, rows, repo))

    if not rows:
        summary.append("No open bot or Dependabot pull requests.")
    text = "\n".join(summary) + "\n"
    print(text)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as handle:
            handle.write(text)
    return 0


def classify_command(args: argparse.Namespace) -> int:
    """Classify a ``pulls/N/files`` JSON document (file path or ``-`` for stdin)."""
    policy = load_policy()
    data = json.load(sys.stdin if args.files == "-" else open(args.files, encoding="utf-8"))
    result = classify(data, policy)
    print(json.dumps({"tier": result.tier, "reasons": result.reasons}, indent=2))
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    rec = sub.add_parser("reconcile", help="Reconcile auto-merge labels and the status issue")
    rec.add_argument("--pr", type=int, help="Only reconcile this pull request")
    rec.add_argument("--bot-login", help="Login of the Aspire Bot app user (for example aspire-bot[bot])")
    rec.add_argument("--dry-run", action="store_true", help="Print mutations instead of executing them")
    rec.set_defaults(func=reconcile)
    cls = sub.add_parser("classify", help="Classify a pulls/N/files JSON document")
    cls.add_argument("files", help="Path to JSON, or - for stdin")
    cls.set_defaults(func=classify_command)
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
