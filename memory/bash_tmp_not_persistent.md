---
name: bash-tmp-not-persistent
description: /tmp files do not reliably persist across separate Bash tool invocations in this sandbox
metadata:
  type: feedback
---

Files written to `/tmp` in one Bash tool call are not reliably readable in a later, separate Bash
tool call — even moments later, even with no other activity in between. Confirmed during MV-498:
`git show HEAD:path > /tmp/x.cs` succeeded (verified with `wc -l` in the same call), then a
follow-up Bash call reading `/tmp/x.cs` got `FileNotFoundError`. Retried multiple times with
different filenames; same result every time. Read-only commands within a *single* call (echo,
cat, git show, wc -l chained on one shell) do see each other's output fine — the boundary is the
tool call, not time or command count.

**Why:** looks like each Bash tool invocation may get an isolated/ephemeral view of `/tmp` (or a
fresh mount), so cross-call scratch files silently vanish instead of erroring loudly at write time.

**How to apply:** for any workflow that needs a file to survive between separate Bash calls (e.g.
build a modified variant of a source file to probe/prove a test fails pre-fix, then restore),
either (a) do the whole read-modify-run-restore sequence inside ONE Bash call, or (b) use a
repo-local scratch path instead of `/tmp` (the working tree persists fine across calls — confirmed
via normal git status/diff across many calls this same session), or (c) skip the file shuffle
entirely and use targeted Edit-tool calls on the real file (make the single-line change, run
Unity/tests, edit it back) — this is what actually worked cleanly for proving MV-498's new test
failed pre-fix. Don't burn a run debugging "the file disappeared" — this is the sandbox, not a
bug in the git/python commands themselves.
