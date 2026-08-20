---
name: generate-then-verify
description: Keep generation and verification as separate processes so a generator cannot certify its own output. Use whenever you write a script that produces a config, layout, dataset or level, whenever you are about to trust a script's own console output as proof it worked, and whenever you chain generation stages together.
---

# Generate, then verify — separately

A generator's own prints are not evidence. Every real bug in a recent level-generation session — thirteen identical rooms, six overlapping areas, a shed 0.1 m out of tolerance — was caught by an independent checker reading the output fresh from disk, and none by the generator's own reporting.

## The pattern

1. **Generator** writes the artefact to disk. Nothing downstream trusts its prints.
2. **Verifier** is a separate script, run afterwards, that re-reads the artefact **from disk** and asserts every constraint. It does not import the generator or receive its return value.
3. Only a green verifier is permission to proceed.

A generator and its own assertions share the same misunderstanding.

## Pipeline hazards

**Never pipe a generator through `head`, `tail` or a pager.** `python3 world2.py | head -6` kills the script with SIGPIPE before it writes its JSON; three downstream stages then ran on a stale file. Run the script, then read the file.

**Check the artefact was written this run.** Delete the output first, generate, then fail if it did not reappear. A stale file satisfies almost any check — the same class of bug as a gate that greps a log it did not clear.

**Fail on the exit code correctly.** `if errorlevel 1` tests `>= 1` and silently passes negative exit codes, so a hard crash reads as success. Capture the code and compare to zero.

**Report every violation, not the first.** A validator that returns on first failure forces one round-trip per problem.

**Validate everything, not just the category you thought of.** If only `cover` entities are checked, geometry nobody authored can appear in a passing config.

## What to assert

Assert the **resolved or rendered** value, never the authored one. A test reading the constant in the source passes while the engine draws something else — `fontSize` 36 asserted against a resize cap of 32 is the canonical case. Visual assertions belong in the screenshot-conformance harness, not a headless unit test that cannot see pixels.

## Choosing a metric

Beware optimising a proxy. Maximising walkable route length produced a blob, then a maze — route length says whether a room can be skipped, not whether it is a good fight. Constrain the proxy to a band, then select on the distribution you care about.

## Constraints belong in a file

If your verifier hard-codes the rules, they die with the session. Put them in a checked-in constraints file both the generator and the verifier read.
