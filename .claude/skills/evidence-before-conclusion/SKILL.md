---
name: evidence-before-conclusion
description: Stop yourself shipping a confident causal story built from partial evidence. Use this whenever you are about to state a root cause, explain why something broke or regressed, claim something has or hasn't shipped, or say "the problem is X". Reach for it BEFORE you write the explanation.
---

# Evidence before conclusion

The failure this prevents: a plausible causal story assembled from two or three real observations, presented as a finding, and wrong. It is expensive because it is confident.

## The rule

**Every causal claim is either quoted or labelled.** Quote the file, the log line, the command output, the commit. If you cannot quote it, write *"Hypothesis:"* in front of it and say what would confirm or kill it.

## Five checks that cost seconds

**1. Is this artefact the current one?** Re-read the source at the start of every pass; never carry a snapshot across turns. A config staged an hour ago may have been changed by a ticket since.

**2. Am I looking at the right copy?** Diagnose against the worker clone `C:\Dev\MAx CCs\cc-web`, never `C:\Dev\MaxVsTheWorlds`, which runs days behind.

**3. Is a status being used as evidence?** A Jira status records intent, not reality. A ticket at "On Staging" may have zero commits on its branch — `git log --grep=<KEY>` is the evidence. A green CI run proves only that the checks which ran passed; check whether the one you care about was parked, skipped, or cancelled.

**4. Does the timeline work?** If something "used to work", a regression cannot be explained by a mechanism that was always there.

**5. Have I checked the obvious contradiction?** Before filing a defect against a script, read the script. One widely-repeated claim — that a batch gate reported green while tests failed because Unity was wrapped in `start "" /min /wait` — was carried as a top open item by two reviews and was false: `START /WAIT` sets `ERRORLEVEL` correctly.

## Signals you are about to do it again

- Writing "the problem is" without having opened a file this turn.
- Explaining *why* before confirming *what*.
- Offering a second theory after the first was wrong. That is the moment to read, not to guess again.
- Stopping at a tool limitation and handing the work back. Check for another route — what you need is often one directory away.
