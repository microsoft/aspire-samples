"""Tests for the Aspire Bot control plane. Run: python3 -m unittest discover -s .github/aspire-bot/scripts"""

import json
import unittest

from aspire_bot import (
    Classification, classify, count_fix_attempts, decide, glob_to_regex, latest_verdict, load_policy,
    merge_blockers, merges_enabled, resolve_mode, trip_breaker,
)

POLICY = load_policy()
BOT = "aspire-bot[bot]"
SHA = "a" * 40
OTHER_SHA = "b" * 40

CSPROJ_BUMP = """@@ -10,7 +10,7 @@
   <ItemGroup>
-    <PackageReference Include="Aspire.Hosting.Redis" Version="17.0.0" />
+    <PackageReference Include="Aspire.Hosting.Redis" Version="17.0.1" />
   </ItemGroup>"""

CSPROJ_NEW_PACKAGE = """@@ -10,6 +10,7 @@
   <ItemGroup>
     <PackageReference Include="Aspire.Hosting.Redis" Version="17.0.0" />
+    <PackageReference Include="Evil.Package" Version="1.0.0" />
   </ItemGroup>"""

CSPROJ_RENAME_PACKAGE = """@@ -10,7 +10,7 @@
-    <PackageReference Include="Aspire.Hosting.Redis" Version="17.0.0" />
+    <PackageReference Include="Aspire.Hosting.Rediss" Version="17.0.1" />"""

CI_PIN_BUMP = """@@ -50,7 +50,7 @@
-    - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
+    - uses: actions/checkout@0123456789abcdef0123456789abcdef01234567 # v7.0.2
@@ -20,1 +20,1 @@
-  ASPIRE_CLI_VERSION: '17.0.0'
+  ASPIRE_CLI_VERSION: '17.0.1'"""

CI_STEP_REMOVED = """@@ -100,5 +100,0 @@
-    - name: Test
-      run: dotnet test"""

CI_ACTION_SWAP = """@@ -50,1 +50,1 @@
-    - uses: actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1
+    - uses: evil/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1 # v7.0.1"""

CI_STEP_MOVED = """@@ -10,3 +10,2 @@ jobs:
   build:
-      - run: dotnet test
       - run: echo build
@@ -40,2 +39,3 @@ jobs:
   deploy:
+      - run: dotnet test
       - run: echo deploy"""

CI_PINS_REORDERED = """@@ -20,2 +20,2 @@
-  ASPIRE_CLI_VERSION: '17.0.0'
-  DOTNET_VERSION: '10.0.100'
+  DOTNET_VERSION: '10.0.100'
+  ASPIRE_CLI_VERSION: '17.0.0'"""


NPM_BUMP = """@@ -5,1 +5,1 @@
-    "react": "^19.2.7",
+    "react": "^19.2.8","""

NPM_SOURCE_SWAP = """@@ -5,1 +5,1 @@
-    "react": "19.2.8",
+    "react": "1attacker/evil#0123456789abcdef0123456789abcdef01234567","""

YARN_REGISTRY = """@@ -9,1 +9,1 @@
-  resolved "https://registry.yarnpkg.com/react/-/react-19.2.7.tgz"
+  resolved "https://registry.yarnpkg.com/react/-/react-19.2.8.tgz\""""

YARN_UNTRUSTED = """@@ -9,1 +9,1 @@
-  resolved "https://registry.yarnpkg.com/react/-/react-19.2.7.tgz"
+  resolved "https://evil.example.com/react-19.2.8.tgz\""""

NPM_MIRROR = "https://ms-feed-25.pkgs.visualstudio.com/1es-public/_packaging/npm-public/npm/registry/"
REACT_OLD = {"version": "19.2.7", "resolved": "https://registry.npmjs.org/react/-/react-19.2.7.tgz"}
REACT_NEW = {"version": "19.2.8", "resolved": "https://registry.npmjs.org/react/-/react-19.2.8.tgz"}
LODASH_MIRROR = {"version": "4.17.21", "resolved": NPM_MIRROR + "lodash/-/lodash-4.17.21.tgz"}


def npm_lock(**packages):
    return json.dumps({"name": "app", "lockfileVersion": 3, "packages": {"": {"name": "app"}, **packages}}, indent=2)


def reader(base, head):
    return lambda path, side: base if side == "base" else head


def f(name, patch=None, status="modified", previous=None):
    entry = {"filename": name, "status": status, "patch": patch}
    if previous:
        entry["previous_filename"] = previous
    return entry


def pr(author=BOT, labels=(), draft=False, base="main", sha=SHA):
    return {"user": {"login": author}, "labels": [{"name": n} for n in labels], "draft": draft,
            "base": {"ref": base}, "head": {"sha": sha}, "number": 1}


def review(body, commit=SHA, login=BOT, when="2026-01-01T00:00:00Z"):
    return {"user": {"login": login}, "commit_id": commit, "body": body, "submitted_at": when}


LOW = Classification("low")
HIGH = Classification("high", ["x"])


class ModeTests(unittest.TestCase):
    def test_fail_closed(self):
        self.assertEqual(resolve_mode(None), "stopped")
        self.assertEqual(resolve_mode(""), "stopped")
        self.assertEqual(resolve_mode("go-wild"), "stopped")
        self.assertEqual(resolve_mode(" Active "), "active")
        self.assertEqual(resolve_mode("paused"), "paused")

    def test_merge_switch_needs_enabled(self):
        self.assertTrue(merges_enabled(" Enabled "))
        for raw in (None, "", "true", "on", "active"):
            with self.subTest(raw=raw):
                self.assertFalse(merges_enabled(raw))


class GlobTests(unittest.TestCase):
    def test_globs(self):
        self.assertTrue(glob_to_regex("**/*.md").match("README.md"))
        self.assertTrue(glob_to_regex("**/*.md").match("samples/a/README.md"))
        self.assertFalse(glob_to_regex(".github/workflows/*.lock.yml").match(".github/workflows/x/y.lock.yml"))
        self.assertTrue(glob_to_regex(".github/workflows/aspire-bot-*").match(".github/workflows/aspire-bot-retro.md"))


class ClassifyTests(unittest.TestCase):
    def tier(self, *files):
        return classify(list(files), POLICY).tier

    def test_version_bumps_are_low(self):
        self.assertEqual(self.tier(f("samples/a/A.csproj", CSPROJ_BUMP), f("README.md", "+x")), "low")
        self.assertEqual(self.tier(f(".github/workflows/ci.yml", CI_PIN_BUMP)), "low")
        self.assertEqual(self.tier(f("samples/js/package.json", NPM_BUMP)), "low")
        self.assertEqual(self.tier(f("samples/js/yarn.lock", YARN_REGISTRY)), "low")

    def test_dependency_source_swaps_are_high(self):
        self.assertEqual(self.tier(f("samples/js/package.json", NPM_SOURCE_SWAP)), "high")
        self.assertEqual(self.tier(f("samples/js/yarn.lock", YARN_UNTRUSTED)), "high")
        self.assertEqual(self.tier(f("samples/js/yarn.lock", '+  resolved "git+ssh://x"')), "high")
        self.assertEqual(self.tier(f("samples/js/yarn.lock", None)), "high")

    def test_non_version_changes_are_high(self):
        self.assertEqual(self.tier(f("samples/a/A.csproj", CSPROJ_NEW_PACKAGE)), "high")
        self.assertEqual(self.tier(f("samples/a/A.csproj", CSPROJ_RENAME_PACKAGE)), "high")
        self.assertEqual(self.tier(f(".github/workflows/ci.yml", CI_STEP_REMOVED)), "high")
        self.assertEqual(self.tier(f(".github/workflows/ci.yml", CI_ACTION_SWAP)), "high")
        self.assertEqual(self.tier(f("samples/a/Program.cs", "+x")), "high")
        self.assertEqual(self.tier(f("samples/a/A.csproj", None)), "high")
        self.assertEqual(self.tier(f("samples/a/A.csproj", CSPROJ_BUMP, status="added")), "high")
        self.assertEqual(self.tier(), "high")

    def test_moved_or_reordered_lines_are_not_version_only(self):
        self.assertEqual(self.tier(f(".github/workflows/ci.yml", CI_STEP_MOVED)), "high")
        self.assertEqual(self.tier(f(".github/workflows/ci.yml", CI_PINS_REORDERED)), "high")

    def test_self_control_files_are_high_even_if_markdown(self):
        for path in [".github/workflows/aspire-bot-improver.md", ".github/aspire-bot/policy.json",
                     ".github/policies/auto-merge.yml", ".github/workflows/aspire-bot-triage.lock.yml",
                     ".github/workflows/shared/aspire-bot.md", ".github/agents/agentic-workflows.md",
                     ".github/dependabot.yml", "build/build.sh"]:
            with self.subTest(path=path):
                self.assertEqual(self.tier(f(path, "+x")), "high")

    def test_rename_into_low_path_is_still_checked(self):
        self.assertEqual(self.tier(f("docs/x.md", "+x", status="renamed", previous="samples/a/Program.cs")), "high")


class NpmLockfileTests(unittest.TestCase):
    PATH = "samples/js/package-lock.json"

    def tier(self, base, head, status="modified"):
        return classify([f(self.PATH, "+x", status=status)], POLICY, reader(base, head)).tier

    def test_registry_bump_is_low_beside_unchanged_mirror_entries(self):
        base = npm_lock(**{"node_modules/react": REACT_OLD, "node_modules/lodash": LODASH_MIRROR})
        head = npm_lock(**{"node_modules/react": REACT_NEW, "node_modules/lodash": LODASH_MIRROR})
        self.assertEqual(self.tier(base, head), "low")
        self.assertEqual(self.tier(None, npm_lock(**{"node_modules/react": REACT_NEW}), status="added"), "low")

    def test_added_or_changed_packages_must_resolve_from_the_registry(self):
        base = npm_lock(**{"node_modules/react": REACT_OLD})
        for entry in ({"version": "19.2.8", "resolved": "https://evil.example.com/react-19.2.8.tgz"},
                      {"version": "19.2.8", "resolved": NPM_MIRROR + "react/-/react-19.2.8.tgz"},
                      {"version": "github:attacker/react#0123456"},
                      {"resolved": "../evil", "link": True}):
            with self.subTest(entry=entry):
                self.assertEqual(self.tier(base, npm_lock(**{"node_modules/react": entry})), "high")

    def test_formatting_cannot_hide_a_source(self):
        base = npm_lock(**{"node_modules/react": REACT_OLD})
        head = base.replace(f'"resolved": "{REACT_OLD["resolved"]}"',
                            '"resolved"\n        :   "https://evil.example.com/react-19.2.8.tgz"')
        self.assertEqual(self.tier(base, head), "high")

    def test_unreadable_lockfiles_are_high(self):
        lock = npm_lock(**{"node_modules/react": REACT_NEW})
        self.assertEqual(self.tier(lock, "{not json"), "high")
        self.assertEqual(self.tier(lock, None, status="removed"), "high")
        self.assertEqual(classify([f(self.PATH, "+x")], POLICY).tier, "high")


class VerdictTests(unittest.TestCase):
    def test_bound_to_head_sha(self):
        body = f"aspire-bot-verdict: approve\naspire-bot-reviewed-sha: {SHA}"
        self.assertEqual(latest_verdict([review(body)], BOT, SHA, POLICY), "approve")
        self.assertIsNone(latest_verdict([review(body, commit=OTHER_SHA)], BOT, SHA, POLICY))
        stale = f"aspire-bot-verdict: approve\naspire-bot-reviewed-sha: {OTHER_SHA}"
        self.assertIsNone(latest_verdict([review(stale)], BOT, SHA, POLICY))

    def test_only_bot_reviews_count(self):
        body = f"aspire-bot-verdict: approve\naspire-bot-reviewed-sha: {SHA}"
        self.assertIsNone(latest_verdict([review(body, login="someone")], BOT, SHA, POLICY))

    def test_footer_must_be_unambiguous(self):
        quoted = (f"> aspire-bot-verdict: approve\n> aspire-bot-reviewed-sha: {SHA}\n\nProblems found.\n"
                  f"aspire-bot-verdict: request-changes\naspire-bot-reviewed-sha: {SHA}")
        self.assertIsNone(latest_verdict([review(quoted)], BOT, SHA, POLICY))
        not_last = f"aspire-bot-verdict: approve\naspire-bot-reviewed-sha: {SHA}\nMore text"
        self.assertIsNone(latest_verdict([review(not_last)], BOT, SHA, POLICY))
        quoted_only = f"> aspire-bot-verdict: approve\n> aspire-bot-reviewed-sha: {SHA}"
        self.assertIsNone(latest_verdict([review(quoted_only)], BOT, SHA, POLICY))

    def test_newest_wins(self):
        old = review(f"aspire-bot-verdict: approve\naspire-bot-reviewed-sha: {SHA}", when="2026-01-01T00:00:00Z")
        new = review(f"**aspire-bot-verdict: request-changes**\naspire-bot-reviewed-sha: `{SHA}`", when="2026-01-02T00:00:00Z")
        self.assertEqual(latest_verdict([old, new], BOT, SHA, POLICY), "request-changes")


class DecideTests(unittest.TestCase):
    def decide(self, p, mode="active", c=LOW, verdict="approve", attempts=0):
        return decide(p, mode, c, verdict, attempts, BOT, POLICY)

    def test_eligible_bot_pr_merges_and_never_gets_persistent_label(self):
        d = self.decide(pr())
        self.assertTrue(d.merge)
        self.assertNotIn("auto-merge", d.add_labels)
        self.assertIn("auto-merge", self.decide(pr(labels=["auto-merge"])).remove_labels)

    def test_dependabot_does_not_need_verdict_but_honors_request_changes(self):
        self.assertTrue(self.decide(pr(author="dependabot[bot]"), verdict=None).merge)
        d = self.decide(pr(author="dependabot[bot]", labels=["auto-merge"]), verdict="request-changes")
        self.assertFalse(d.merge)
        self.assertIn("auto-merge", d.remove_labels)

    def test_disarms_when_not_active(self):
        for mode in ("paused", "stopped"):
            d = self.decide(pr(labels=["auto-merge"]), mode=mode, c=None if mode == "stopped" else LOW)
            self.assertFalse(d.merge)
            self.assertEqual(d.remove_labels[:1], ["auto-merge"])
        self.assertEqual(self.decide(pr(labels=["auto-merge"]), mode="stopped", c=None).add_labels, [])

    def test_blockers(self):
        self.assertFalse(self.decide(pr(), c=HIGH).merge)
        self.assertIn("needs-human-review", self.decide(pr(), c=HIGH).add_labels)
        self.assertFalse(self.decide(pr(), verdict=None).merge)
        self.assertFalse(self.decide(pr(labels=["aspire-bot:hold"])).merge)
        self.assertFalse(self.decide(pr(labels=["needs-human-review"])).merge)
        self.assertFalse(self.decide(pr(draft=True)).merge)
        self.assertFalse(self.decide(pr(base="release")).merge)
        exhausted = self.decide(pr(), verdict="request-changes", attempts=3)
        self.assertTrue(exhausted.needs_human)

    def test_repository_kill_switches_block_merges(self):
        d = decide(pr(), "active", LOW, "approve", 0, BOT, POLICY, blockers=["`ASPIRE_BOT_MERGE` is not `enabled`"])
        self.assertFalse(d.merge)
        self.assertIn("`ASPIRE_BOT_MERGE` is not `enabled`", d.reasons)

    def test_github_auto_merge_is_disarmed_on_bot_prs_only(self):
        armed = dict(pr(), auto_merge={"merge_method": "squash"})
        self.assertTrue(self.decide(armed, mode="stopped", c=None).disable_auto_merge)
        self.assertFalse(self.decide(pr()).disable_auto_merge)
        self.assertFalse(self.decide(dict(armed, user={"login": "octocat"})).disable_auto_merge)

    def test_humans_are_untouched(self):
        d = self.decide(pr(author="octocat", labels=["auto-merge"]), mode="stopped")
        self.assertIsNone(d.merge)
        self.assertEqual((d.add_labels, d.remove_labels), ([], []))


class MergeRevalidationTests(unittest.TestCase):
    def test_fresh_copy_must_still_be_eligible(self):
        fresh = dict(pr(), state="open")
        self.assertEqual(merge_blockers(fresh, SHA, POLICY, "main"), [])
        self.assertTrue(merge_blockers(dict(fresh, head={"sha": OTHER_SHA}), SHA, POLICY, "main"))
        self.assertTrue(merge_blockers(dict(fresh, state="closed"), SHA, POLICY, "main"))
        self.assertTrue(merge_blockers(dict(pr(draft=True), state="open"), SHA, POLICY, "main"))
        for label in ("aspire-bot:hold", "needs-human-review", "aspire-bot:stop"):
            with self.subTest(label=label):
                self.assertTrue(merge_blockers(dict(pr(labels=[label]), state="open"), SHA, POLICY, "main"))


class FixAttemptTests(unittest.TestCase):
    def test_every_fixer_run_counts_not_only_pushes(self):
        commits = [{"commit": {"message": "Fix build [aspire-bot fix]"}}]
        attempt = {"user": {"login": BOT}, "body": f"Couldn't fix it.\naspire-bot-fix-attempt: {SHA}"}
        self.assertEqual(count_fix_attempts(commits, [attempt] * 3, BOT, POLICY), 3)
        self.assertEqual(count_fix_attempts(commits, [], BOT, POLICY), 1)
        self.assertEqual(count_fix_attempts([], [dict(attempt, user={"login": "someone"})], BOT, POLICY), 0)


class FakeGitHub:
    repo = "o/r"

    def __init__(self, comments=()):
        self.comments = list(comments)
        self.writes = []

    def get(self, path, paginate=False):
        return self.comments if "/comments" in path else [{"number": 7}]

    def write(self, method, path, payload=None):
        self.writes.append((method, path, payload))


class BreakerTests(unittest.TestCase):
    def test_trips_once_per_cause(self):
        gh = FakeGitHub()
        self.assertTrue(trip_breaker(gh, POLICY, "ci:abc", "CI failed on main."))
        self.assertIn(("POST", "repos/o/r/issues/7/labels", {"labels": ["aspire-bot:stop"]}), gh.writes)
        cleared = FakeGitHub(comments=[gh.writes[-1][2]])
        self.assertFalse(trip_breaker(cleared, POLICY, "ci:abc", "CI failed on main."))
        self.assertEqual(cleared.writes, [])


if __name__ == "__main__":
    unittest.main()
