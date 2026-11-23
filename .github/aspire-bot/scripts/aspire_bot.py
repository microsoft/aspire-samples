#!/usr/bin/env python3
"""Aspire Bot control plane.

This script is the deterministic (non-AI) authority of the Aspire Bot loop:

* honors every kill switch: the ``aspire-bot:stop`` label on any open issue or pull request,
  ``ASPIRE_BOT_MODE`` (active | paused | stopped), and the ``ASPIRE_BOT_MERGE`` merge switch,
* trips the circuit breaker (adds ``aspire-bot:stop`` to the status issue) when CI fails on the
  default branch after an Aspire Bot merge, or when too many automated pull requests merge in a day,
* classifies pull requests into risk tiers from their changed files,
* decides whether bot and Dependabot pull requests may merge. The ``merge`` command runs in a
  separate job that re-reads the kill switches, re-validates each pull request immediately before
  merging, and pins the squash merge to the exact head SHA that was classified, reviewed, and
  passed CI Gate, so a later push can't ride along,
* disarms GitHub auto-merge (the ``auto-merge`` label and the setting) on bot and Dependabot
  pull requests,
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
from dataclasses import dataclass, field
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Callable

POLICY_PATH = Path(__file__).resolve().parents[1] / "policy.json"
STATUS_TITLE = "Aspire Bot status"
REVIEWED_SHA_PATTERN = re.compile(r"(?i)[*_`]*aspire-bot-reviewed-sha:\s*`?([0-9a-f]{40})`?[*_`]*")
MODES = ("active", "paused", "stopped")
BREAKER_MARKER = "aspire-bot-breaker:"
NPM_PROTOCOL = re.compile(r"^[A-Za-z][A-Za-z0-9+.-]*:")

# read_file(path, side) returns a file's content at the merge base ("base") or head commit ("head").
FileReader = Callable[[str, str], "str | None"]


def load_policy(path: Path = POLICY_PATH) -> dict:
    with open(path, encoding="utf-8") as handle:
        return json.load(handle)


def resolve_mode(raw: str | None) -> str:
    """Return the effective mode. Anything other than active/paused fails closed to stopped."""
    value = (raw or "").strip().lower()
    return value if value in MODES else "stopped"


def merges_enabled(raw: str | None) -> bool:
    """The merge switch is a second key: only ``enabled`` allows merges. Anything else fails closed."""
    return (raw or "").strip().lower() == "enabled"


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
    """True when every change block swaps lines for the same lines, in order, once versions are masked.

    Lines are compared within each contiguous block of removed and added lines, so a line that
    moves elsewhere in the file, or lines that are reordered, are not version-only.
    """
    if not patch:
        return False
    compiled = [re.compile(p) for p in patterns]

    def mask(line: str) -> str:
        for regex in compiled:
            line = regex.sub("<version>", line)
        return line

    blocks: list[tuple[list[str], list[str]]] = []
    removed: list[str] = []
    added: list[str] = []
    for line in [*patch.splitlines(), " "]:
        if line.startswith("\\"):
            continue
        if line.startswith("+"):
            added.append(mask(line[1:]))
        elif line.startswith("-"):
            removed.append(mask(line[1:]))
        elif removed or added:
            # A context line or hunk header ends the block.
            blocks.append((removed, added))
            removed, added = [], []
    return bool(blocks) and all(old == new for old, new in blocks)


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


def npm_lock_entries(text: str) -> dict[str, dict] | None:
    """Flatten an npm lockfile (lockfileVersion 1 to 3) into ``{key: entry}``, or None if it isn't valid."""
    try:
        data = json.loads(text)
    except ValueError:
        return None
    if not isinstance(data, dict):
        return None
    entries: dict[str, dict] = {}
    packages = data.get("packages")
    if isinstance(packages, dict):
        for key, entry in packages.items():
            if isinstance(entry, dict):
                entries[f"packages/{key}"] = entry

    def walk(dependencies: object, prefix: str) -> None:
        if isinstance(dependencies, dict):
            for name, entry in dependencies.items():
                if isinstance(entry, dict):
                    key = f"{prefix}/{name}"
                    entries[f"dependencies{key}"] = entry
                    walk(entry.get("dependencies"), key)

    walk(data.get("dependencies"), "")
    return entries


def npm_entry_is_trusted(entry: dict, registries: list[str]) -> bool:
    if entry.get("link"):
        return False
    resolved = entry.get("resolved")
    if resolved is not None and not (isinstance(resolved, str) and resolved.startswith(tuple(registries))):
        return False
    version = entry.get("version")
    return not (isinstance(version, str) and NPM_PROTOCOL.match(version))


def npm_lockfile_is_trusted(base: str | None, head: str | None, registries: list[str]) -> bool:
    """True when every package entry that the change adds or modifies resolves from a trusted registry.

    Both sides are parsed as JSON, so whitespace or line breaks in the diff can't hide a source.
    Entries that are unchanged from the merge base aren't judged again.
    """
    if head is None or not registries:
        return False
    head_entries = npm_lock_entries(head)
    base_entries = npm_lock_entries(base) if base is not None else {}
    if head_entries is None or base_entries is None:
        return False
    return all(base_entries.get(key) == entry or npm_entry_is_trusted(entry, registries)
               for key, entry in head_entries.items())


@dataclass
class Classification:
    tier: str
    reasons: list[str] = field(default_factory=list)


def classify(files: list[dict], policy: dict, read_file: FileReader | None = None) -> Classification:
    """Classify a pull request from its changed files (GitHub ``pulls/N/files`` shape).

    ``read_file`` supplies file content for rules that judge the resulting file rather than the
    diff (npm lockfiles). Without it, those files are high risk.
    """
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
            elif rule["tier"] == "npm-lockfile":
                if path != entry["filename"]:
                    continue  # A rename is judged once, from the old path at the base to the new path at the head.
                status = entry.get("status")
                head = read_file(path, "head") if read_file and status != "removed" else None
                base = read_file(entry.get("previous_filename") or path, "base") if read_file and status != "added" else None
                if not npm_lockfile_is_trusted(base, head, rule.get("registries", [])):
                    reasons.append(f"`{path}`: lockfile adds or changes a package from an untrusted source, or can't be read")
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
    disable_auto_merge: bool = False


def author_kind(login: str, bot_login: str, policy: dict) -> str:
    if login.lower() == bot_login.lower():
        return "bot"
    if login.lower() == policy["trustedAuthors"]["dependabot"].lower():
        return "dependabot"
    return "human"


def decide(pr: dict, mode: str, classification: Classification | None, verdict: str | None,
           fix_attempts: int, bot_login: str, policy: dict, default_branch: str = "main",
           blockers: list[str] | tuple[str, ...] = ()) -> Decision:
    """Decide one pull request. ``blockers`` are repository-wide reasons not to merge (kill switches)."""
    labels = policy["labels"]
    current = {label["name"] for label in pr.get("labels", [])}
    kind = author_kind(pr["user"]["login"], bot_login, policy)
    auto_merge = labels["autoMerge"]["name"]

    if kind == "human":
        return Decision(kind, None, ["human-authored pull requests are never merged or relabeled by Aspire Bot"])

    reasons: list[str] = list(blockers)
    needs_human = False
    if mode != "active":
        reasons.append(f"mode is `{mode}`")
    if pr.get("draft"):
        reasons.append("draft")
    if pr["base"]["ref"] != default_branch:
        reasons.append(f"targets `{pr['base']['ref']}`")
    if labels["hold"]["name"] in current:
        reasons.append(f"`{labels['hold']['name']}` label")
    if labels["needsHuman"]["name"] in current:
        reasons.append(f"`{labels['needsHuman']['name']}` label (a maintainer must remove it)")
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
    # The persistent label and GitHub auto-merge would authorize future pushes too; merges are
    # SHA-pinned by the merge command instead.
    if auto_merge in current:
        decision.remove_labels.append(auto_merge)
    decision.disable_auto_merge = pr.get("auto_merge") is not None

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
            capture_output=True, text=True, encoding="utf-8", check=False,
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

    def text(self, path: str) -> str | None:
        """Raw file content from the contents API, or None when the file doesn't exist at that ref."""
        result = subprocess.run(["gh", "api", "-H", "Accept: application/vnd.github.raw", path],
                                capture_output=True, text=True, encoding="utf-8", check=False)
        if result.returncode == 0:
            return result.stdout
        if "HTTP 404" in result.stderr:
            return None
        raise RuntimeError(f"gh api {path} failed: {result.stderr.strip()}")

    def graphql(self, query: str, **variables: str) -> None:
        if self.dry_run:
            print(f"[dry-run] graphql {query.split('(')[0].strip()} {json.dumps(variables)}")
            return
        args = ["graphql", "-f", f"query={query}"]
        for name, value in variables.items():
            args += ["-f", f"{name}={value}"]
        self._run(args)


def file_reader(gh: GitHub, pr: dict) -> FileReader:
    """Read files at the pull request's merge base or head commit. The merge base is looked up once."""
    merge_base: list[str] = []

    def read(path: str, side: str) -> str | None:
        if side == "head":
            ref = pr["head"]["sha"]
        else:
            if not merge_base:
                compare = gh.get(f"repos/{gh.repo}/compare/{pr['base']['sha']}...{pr['head']['sha']}")
                merge_base.append(compare["merge_base_commit"]["sha"])
            ref = merge_base[0]
        return gh.text(f"repos/{gh.repo}/contents/{urllib.parse.quote(path)}?ref={ref}")

    return read


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


def merge_pinned(gh: GitHub, number: int, sha: str) -> str:
    """Squash-merge only if the head is still ``sha`` (GitHub returns 409 otherwise)."""
    try:
        gh.write("PUT", f"repos/{gh.repo}/pulls/{number}/merge", {"sha": sha, "merge_method": "squash"})
        return "merged"
    except RuntimeError as error:
        hint = ""
        if "review" in str(error).lower():
            hint = " If the main ruleset requires an approval, add the Aspire Bot App to its bypass list (see the README setup)."
        print(f"::warning::Merge of #{number} at {sha} was refused: {error}{hint}")
        return "merge refused"


def count_fix_attempts(commits: list[dict], comments: list[dict], bot_login: str, policy: dict) -> int:
    """Every fixer run leaves a marked comment, and every push a marked commit. Use the larger count."""
    limits = policy["limits"]
    pushes = sum(1 for c in commits if limits["fixCommitMarker"] in ((c.get("commit") or {}).get("message") or ""))
    runs = sum(1 for c in comments
               if ((c.get("user") or {}).get("login") or "").lower() == bot_login.lower()
               and limits["fixAttemptMarker"] in (c.get("body") or ""))
    return max(pushes, runs)


def open_stop_items(gh: GitHub, policy: dict) -> list[int]:
    """Open issues and pull requests that carry the emergency stop label."""
    label = urllib.parse.quote(policy["labels"]["stop"]["name"])
    return [item["number"] for item in gh.get(f"repos/{gh.repo}/issues?state=open&labels={label}&per_page=10") or []]


def stop_reason(numbers: list[int], policy: dict) -> str:
    return f"emergency stop: `{policy['labels']['stop']['name']}` is on {', '.join(f'#{n}' for n in numbers)}"


def merge_blockers(pr: dict, sha: str, policy: dict, default_branch: str) -> list[str]:
    """Reasons a pull request must not merge, from a copy read immediately before merging."""
    labels = policy["labels"]
    current = {label["name"] for label in pr.get("labels", [])}
    reasons = []
    if pr.get("state") != "open":
        reasons.append("no longer open")
    if pr.get("draft"):
        reasons.append("draft")
    if pr["base"]["ref"] != default_branch:
        reasons.append(f"targets `{pr['base']['ref']}`")
    if pr["head"]["sha"] != sha:
        reasons.append("new commits since it was validated")
    reasons += [f"`{labels[key]['name']}` label" for key in ("hold", "needsHuman", "stop") if labels[key]["name"] in current]
    return reasons


def disable_auto_merge(gh: GitHub, pr: dict) -> None:
    try:
        gh.graphql("mutation($id: ID!) { disablePullRequestAutoMerge(input: {pullRequestId: $id}) { clientMutationId } }",
                   id=pr["node_id"])
    except RuntimeError as error:
        print(f"::warning::Couldn't disable auto-merge on #{pr['number']}: {error}")


def main_ci_status(gh: GitHub, policy: dict, bot_login: str, branch: str) -> tuple[list[str], tuple[str, str] | None]:
    """Block merges while the newest CI run on ``branch`` failed, and trip the breaker if Aspire Bot merged that commit."""
    data = gh.get(f"repos/{gh.repo}/actions/workflows/{policy['ciWorkflow']}/runs"
                  f"?branch={urllib.parse.quote(branch)}&event=push&status=completed&per_page=1")
    runs = (data or {}).get("workflow_runs") or []
    if not runs or runs[0].get("conclusion") not in ("failure", "timed_out"):
        return [], None
    run = runs[0]
    blockers = [f"CI is failing on `{branch}` ({run.get('html_url')})"]
    for pull in gh.get(f"repos/{gh.repo}/commits/{run['head_sha']}/pulls") or []:
        merged_by = ((gh.get(f"repos/{gh.repo}/pulls/{pull['number']}") or {}).get("merged_by") or {}).get("login") or ""
        if merged_by.lower() == bot_login.lower():
            return blockers, (f"ci:{run['head_sha']}",
                              f"CI failed on `{branch}` after Aspire Bot merged #{pull['number']} ({run.get('html_url')}).")
    return blockers, None


def automated_merges_since(gh: GitHub, branch: str, since: datetime, logins: set[str]) -> int:
    """Count commits on ``branch`` since ``since`` that Aspire Bot or Dependabot authored (their squash merges)."""
    stamp = since.strftime("%Y-%m-%dT%H:%M:%SZ")
    commits = gh.get(f"repos/{gh.repo}/commits?sha={urllib.parse.quote(branch)}&since={stamp}&per_page=100", paginate=True)
    return sum(1 for c in commits if ((c.get("author") or {}).get("login") or "").lower() in logins)


def trip_breaker(gh: GitHub, policy: dict, key: str, reason: str) -> bool:
    """Engage the emergency stop on the status issue, at most once per cause.

    A maintainer resumes the loop by removing the label. The same cause won't trip the breaker again.
    """
    marker = f"<!-- {BREAKER_MARKER} {key} -->"
    stop = policy["labels"]["stop"]["name"]
    issue = find_status_issue(gh, policy)
    if issue is not None:
        comments = gh.get(f"repos/{gh.repo}/issues/{issue['number']}/comments?per_page=100", paginate=True)
        if any(marker in (c.get("body") or "") for c in comments):
            return False
    else:
        issue = gh.write("POST", f"repos/{gh.repo}/issues", {
            "title": STATUS_TITLE, "body": "This issue is maintained by the Aspire Bot control plane.",
            "labels": [policy["labels"]["status"]["name"]]})
    print(f"::warning::Circuit breaker tripped: {reason}")
    if issue:
        gh.write("POST", f"repos/{gh.repo}/issues/{issue['number']}/labels", {"labels": [stop]})
        gh.write("POST", f"repos/{gh.repo}/issues/{issue['number']}/comments", {"body": (
            f"{marker}\n**Circuit breaker tripped:** {reason}\n\n"
            f"Aspire Bot is stopped. Investigate, then remove the `{stop}` label to resume. "
            "This cause won't trip the breaker again.")})
    return True


def render_status(mode: str, merging: bool, rows: list[dict], repo: str, policy: dict) -> str:
    lines = [
        f"# {STATUS_TITLE}",
        "",
        f"**Mode:** `{mode}` (the `{policy['modeVariable']}` repository variable: `active`, `paused`, or `stopped`).",
        f"**Merges:** {'enabled' if merging else 'off'} (the `{policy['mergeVariable']}` repository variable must be `enabled`).",
        "",
        f"To stop everything at once, add the `{policy['labels']['stop']['name']}` label to any open issue or "
        "pull request, including this one.",
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


def find_status_issue(gh: GitHub, policy: dict) -> dict | None:
    label = urllib.parse.quote(policy["labels"]["status"]["name"])
    issues = gh.get(f"repos/{gh.repo}/issues?state=open&labels={label}&per_page=10")
    issues = [i for i in issues or [] if "pull_request" not in i]
    return issues[0] if issues else None


def upsert_status_issue(gh: GitHub, policy: dict, body: str) -> None:
    issue = find_status_issue(gh, policy)
    if issue is None:
        gh.write("POST", f"repos/{gh.repo}/issues",
                 {"title": STATUS_TITLE, "body": body, "labels": [policy["labels"]["status"]["name"]]})
    elif (issue.get("body") or "").strip() != body.strip():
        gh.write("PATCH", f"repos/{gh.repo}/issues/{issue['number']}", {"body": body})


def explain_needs_human(decision: Decision, classification: Classification | None) -> str:
    lines = ["Aspire Bot will not auto-merge this pull request. A maintainer needs to review it.", ""]
    for reason in decision.reasons:
        lines.append(f"- {reason}")
    if classification and classification.reasons:
        lines += ["", "<details><summary>Risk tier details</summary>", ""]
        lines += [f"- {reason}" for reason in classification.reasons[:50]]
        lines += ["", "</details>"]
    return "\n".join(lines)


def write_summary(text: str) -> None:
    print(text)
    if os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(os.environ["GITHUB_STEP_SUMMARY"], "a", encoding="utf-8") as handle:
            handle.write(text)


def reconcile(args: argparse.Namespace) -> int:
    policy = load_policy()
    mode = resolve_mode(os.environ.get(policy["modeVariable"]))
    merging = merges_enabled(os.environ.get(policy["mergeVariable"]))
    repo = os.environ["GITHUB_REPOSITORY"]
    bot_login = args.bot_login or os.environ.get("ASPIRE_BOT_LOGIN") or ""
    if not bot_login:
        print("::error::Bot login is unknown; pass --bot-login or set ASPIRE_BOT_LOGIN.")
        return 1
    gh = GitHub(repo, args.dry_run)
    default_branch = gh.get(f"repos/{repo}")["default_branch"]
    # Always create missing labels, so `aspire-bot:stop` exists for anyone who needs to pull it.
    ensure_labels(gh, policy)

    blockers: list[str] = []
    stop_items = open_stop_items(gh, policy)
    if stop_items:
        blockers.append(stop_reason(stop_items, policy))
        mode = "stopped"
    if not merging:
        blockers.append(f"`{policy['mergeVariable']}` is not `enabled`")
    if mode != "stopped":
        ci_blockers, trip = main_ci_status(gh, policy, bot_login, default_branch)
        blockers += ci_blockers
        if trip and trip_breaker(gh, policy, *trip):
            blockers.append(f"circuit breaker tripped: {trip[1]}")
            mode = "stopped"
    print(f"Mode: {mode} | merges: {'enabled' if merging else 'off'} | bot: {bot_login} | dry-run: {args.dry_run}")

    if args.pr:
        prs = [gh.get(f"repos/{repo}/pulls/{args.pr}")]
        prs = [p for p in prs if p and p.get("state") == "open"]
    else:
        prs = gh.get(f"repos/{repo}/pulls?state=open&per_page=100", paginate=True)

    rows = []
    candidates = []
    summary = [f"## Aspire Bot control plane (`{mode}`)", ""] + [f"- Not merging: {b}" for b in blockers]
    for pr in prs:
        kind = author_kind(pr["user"]["login"], bot_login, policy)
        if kind == "human":
            continue
        number = pr["number"]
        classification = verdict = None
        attempts = 0
        if mode != "stopped":
            files = gh.get(f"repos/{repo}/pulls/{number}/files?per_page=100", paginate=True)
            classification = classify(files, policy, file_reader(gh, pr))
            reviews = gh.get(f"repos/{repo}/pulls/{number}/reviews?per_page=100", paginate=True)
            verdict = latest_verdict(reviews, bot_login, pr["head"]["sha"], policy)
            if kind == "bot":
                commits = gh.get(f"repos/{repo}/pulls/{number}/commits?per_page=100", paginate=True)
                comments = gh.get(f"repos/{repo}/issues/{number}/comments?per_page=100", paginate=True)
                attempts = count_fix_attempts(commits, comments, bot_login, policy)
        decision = decide(pr, mode, classification, verdict, attempts, bot_login, policy, default_branch, blockers)

        if decision.disable_auto_merge:
            disable_auto_merge(gh, pr)
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
            if gate == "success":
                candidates.append({"number": number, "sha": pr["head"]["sha"]})
                merge = "ready to merge"
            else:
                merge = f"waiting ({policy['requiredCheck']}: {gate})"
        rows.append({
            "number": number, "kind": kind, "tier": classification.tier if classification else "-",
            "verdict": verdict, "attempts": attempts, "merge": merge, "reasons": decision.reasons,
        })
        summary.append(f"- #{number} ({kind}): tier={rows[-1]['tier']} verdict={verdict or '-'} "
                       f"merge={merge} +{decision.add_labels} -{decision.remove_labels} :: {'; '.join(decision.reasons)}")

    if mode != "stopped" and not args.pr:
        upsert_status_issue(gh, policy, render_status(mode, merging, rows, repo, policy))

    if not rows:
        summary.append("No open bot or Dependabot pull requests.")
    write_summary("\n".join(summary) + "\n")
    if os.environ.get("GITHUB_OUTPUT"):
        with open(os.environ["GITHUB_OUTPUT"], "a", encoding="utf-8") as handle:
            handle.write(f"candidates={json.dumps(candidates)}\n")
    return 0


def merge_command(args: argparse.Namespace) -> int:
    """Merge reconcile's candidates after re-reading every kill switch and each pull request.

    This runs in its own job: GitHub reads repository variables when a job starts, so the mode and
    merge switch here reflect changes made while reconcile was running.
    """
    policy = load_policy()
    repo = os.environ["GITHUB_REPOSITORY"]
    bot_login = args.bot_login or os.environ.get("ASPIRE_BOT_LOGIN") or ""
    candidates = json.loads(args.candidates or "[]")
    gh = GitHub(repo, args.dry_run)
    default_branch = gh.get(f"repos/{repo}")["default_branch"]
    blockers: list[str] = []
    mode = resolve_mode(os.environ.get(policy["modeVariable"]))
    if mode != "active":
        blockers.append(f"mode is `{mode}`")
    if not merges_enabled(os.environ.get(policy["mergeVariable"])):
        blockers.append(f"`{policy['mergeVariable']}` is not `enabled`")
    if not bot_login:
        blockers.append("the bot login is unknown")
    logins = {bot_login.lower(), policy["trustedAuthors"]["dependabot"].lower()}
    limit = policy["limits"]["maxAutoMergesPerDay"]

    lines = ["## Aspire Bot merges", ""]
    for candidate in candidates:
        number, sha = int(candidate["number"]), str(candidate["sha"])
        if not blockers:
            stop_items = open_stop_items(gh, policy)
            if stop_items:
                blockers.append(stop_reason(stop_items, policy))
        if not blockers:
            now = datetime.now(timezone.utc)
            merged = automated_merges_since(gh, default_branch, now - timedelta(days=1), logins)
            if merged >= limit:
                reason = (f"{merged} automated pull requests merged into `{default_branch}` in the last 24 hours "
                          f"(the limit is {limit}).")
                trip_breaker(gh, policy, f"merge-budget:{now:%Y-%m-%d}", reason)
                blockers.append(reason)
        if blockers:
            lines.append(f"- #{number}: not merged ({'; '.join(blockers)})")
            continue
        fresh = gh.get(f"repos/{repo}/pulls/{number}")
        reasons = merge_blockers(fresh, sha, policy, default_branch)
        if not reasons:
            gate = required_check_state(gh, sha, policy["requiredCheck"])
            if gate != "success":
                reasons.append(f"{policy['requiredCheck']} is {gate}")
        result = merge_pinned(gh, number, sha) if not reasons else f"skipped ({'; '.join(reasons)})"
        lines.append(f"- #{number} at `{sha[:7]}`: {result}")
    if not candidates:
        lines.append("No merge candidates.")
    write_summary("\n".join(lines) + "\n")
    return 0


def classify_command(args: argparse.Namespace) -> int:
    """Classify a ``pulls/N/files`` JSON document (file path or ``-`` for stdin).

    npm lockfiles are judged from their content, which isn't available here, so they report high risk.
    """
    policy = load_policy()
    data = json.load(sys.stdin if args.files == "-" else open(args.files, encoding="utf-8"))
    result = classify(data, policy)
    print(json.dumps({"tier": result.tier, "reasons": result.reasons}, indent=2))
    return 0


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    sub = parser.add_subparsers(dest="command", required=True)
    rec = sub.add_parser("reconcile", help="Reconcile labels, the status issue, and merge candidates")
    rec.add_argument("--pr", type=int, help="Only reconcile this pull request")
    rec.add_argument("--bot-login", help="Login of the Aspire Bot app user (for example aspire-bot[bot])")
    rec.add_argument("--dry-run", action="store_true", help="Print mutations instead of executing them")
    rec.set_defaults(func=reconcile)
    mrg = sub.add_parser("merge", help="Merge reconcile's candidates after re-checking every kill switch")
    mrg.add_argument("--candidates", default="[]", help="JSON list of {number, sha} objects from reconcile")
    mrg.add_argument("--bot-login", help="Login of the Aspire Bot app user (for example aspire-bot[bot])")
    mrg.add_argument("--dry-run", action="store_true", help="Print merges instead of executing them")
    mrg.set_defaults(func=merge_command)
    cls = sub.add_parser("classify", help="Classify a pulls/N/files JSON document")
    cls.add_argument("files", help="Path to JSON, or - for stdin")
    cls.set_defaults(func=classify_command)
    args = parser.parse_args(argv)
    return args.func(args)


if __name__ == "__main__":
    sys.exit(main())
