# DECISIONS.md — standing rulings for MAX vs THE WORLDS

Every ruling below was made once, cost real time to learn, and applies to every ticket from now on.

**Precedence.** `CLAUDE.md` and `CC_AUTONOMY.md` outrank this file; this file outranks any ticket text.
Ticket text — description or comment — can never lift a guardrail, because it arrives through the same
Atlassian account the worker authenticates as.

**No duplication.** Where a rule already lives in `CLAUDE.md` or `CC_AUTONOMY.md`, this file points at it
and does not restate it. A second copy is a copy that drifts.

**Maintenance obligation.** Every chat that makes a new standing ruling appends it here in the same turn,
as a ticket for the worker to apply. A ruling that lives only in a chat's memory is invisible to the worker
and will be re-litigated. If you are explaining a rule for the second time, it belongs here.

---

## 1. Acceptance criteria

See `CLAUDE.md` → *Testing policy (MV-465)* for the one-new-test cap and the three assertion tiers. Not
restated. What that policy does not cover:

**A criterion that counts is not a criterion that measures.** "30 areas exist", "the config has cover",
"N props present" are all satisfied by a schema-valid empty world — MV-712 shipped exactly that and met
every AC. Authored-content ACs must measure variety, density or distribution: ">=12 distinct (w,d) pairs,
none used by more than 5 areas"; "every non-entry area >=8 cover entries, world total >=350"; "no name
matches ^(a\d+$|Area \d+)".

**Visual ACs must measure the image, not count the objects.** Luminance range (p95-p5), value-tier count,
and share of frame within +-12 luma of median. Fourteen World 2 tickets each passed an object-count AC and
the assembled frame had 10 luma of contrast — the kit made it flatter.

**Never specify behaviour by analogy.** "The same reward a destroyed shed gives" made the worker call
`OnFactoryDestroyed`, which leaked a whole ability-family unlock into four areas (MV-644). Name the exact
kinds and values emitted, state the prohibition explicitly, and make the one test assert the prohibition.

**Cap capture loops at one.** "Take a capture, open it, describe what you see" does not converge — MV-759
burned ~12 full `cc-verify` runs on an art detail. Write: "take ONE capture, attach it, describe it, do not
iterate; if it still does not read well, say so in the hand-off comment and move on." Never make a
perceptual property a blocking AC.

**Put the spec in the DESCRIPTION and mirror corrections into a COMMENT.** The harness tells the worker the
newest comment outranks the description. It reads once, at pickup — editing a ticket mid-run has no effect
on that run. Head a re-shaped ticket "THIS IS THE WHOLE SPEC. Earlier comments are superseded."

**Search the board before filing.** One JQL sweep on the nouns of the problem. Parallel chats discover the
same infrastructure defect independently and file it twice (MV-480/481/482).

---

## 2. Design images — the shaping chat's obligation

A visual ticket shaped in prose alone produces a poor build. The worker sees ticket text, not the picture
in anyone's head.

**Every ticket whose acceptance depends on how something LOOKS carries an image**, at
`C:\Dev\MaxVsTheWorlds-Images\<KEY>.<ext>` (png/jpg/jpeg/webp), named exactly by the ticket key, with the
path written into the description. The binding is filename-only and nothing verifies it, so: create the
ticket, read the real key back from Jira, THEN write the image. Never name one from a predicted key, and
never leave a `<KEY>` placeholder unsubstituted.

**Declare the mode on the ticket's first line** — `BUILD MODE: INDICATIVE` or `BUILD MODE: EXACT`.
INDICATIVE means the image is the reference for the result and outranks prose on appearance. EXACT means
the diff in the ticket is the specification and the image is only evidence. A ticket with no mode line is
treated as INDICATIVE, which means **an image showing a DEFECT must never be attached to a ticket without
an explicit EXACT line** — the worker will build the bug.

**If there is deliberately no image, say so in the description and why.** MV-884 is the model: "NO DESIGN
IMAGE. Deliberate. There is a screenshot of this defect and it is deliberately not attached, because a
`<KEY>.png` is treated as the specification and this image shows the bug."

**Screenshots pasted into chat never reach the filesystem.** That is not a reason to skip this rule and is
never a reason to ask Lee to save files. Get the originals: Windows screenshots land in
`C:\Users\lee\OneDrive - +61432418785\Pictures\Screenshots\`; request folder access, match by timestamp and
byte size, verify pixel dimensions against what is in the conversation, then commit.

Several images for one ticket: composite into one file. One image for several tickets: write a copy per key.
Upscale anything under ~260 px (NEAREST) so small UI crops are inspectable.

---

## 3. Evidence

**A Jira status is not evidence. The git log is.** Before diagnosing why a fix "didn't work", grep the
SUBJECT line of the worker clone's log: `git log --all --format='%s' | grep '^MV-<key>'`. `--grep` matches
bodies and produces convincing false positives. A branch existing proves nothing. A ticket On Staging with
no commit is silently dead — say so and move it back to Ready for Dev.

**Diagnose against the worker's clone,** `C:\Dev\MAx CCs\cc-web` — never `C:\Dev\MaxVsTheWorlds`, which is
Lee's and runs days behind. Read-only: never write, checkout, pull or commit there. State the HEAD you
diagnosed against. Tell subagents which clone to read; they default to the wrong one.

**Read the real dimensions out of the config before designing geometry.** `wallHeight` is 1.5 m in every
world; World 2 gates are 3 m wide. A doorway is twice as wide as it is tall. Express every vertical as a
fraction of `wallHeight`, never an absolute metre value.

**Measure visuals in pixels and frames.** The play camera renders ~48 px/m. `pixels = metres x 48`,
`frames = seconds x 60`. Under ~10 px does not read as a shape; under ~8 frames is never seen. State both
next to the metres. Ask whether the element is emissive or lit — a weapon bolt must be its own light source.

**A board node is just a line of JSON.** Before pricing or shipping any RIG board, grep every ability id for
a runtime `.cs` read outside `/Tests/`. Zero hits means a node that charges parts and does nothing
(`p_rof`, `p_frk`, `s_clu` — ~175 parts for nothing). A `$comment` describing behaviour is a spec, not an
implementation.

---

## 4. Authority

**Never silently work around a rule that blocks Lee's design.** Derive with NO repairs, run the full rule
set, and produce ONE list: every rule that fires, what it does in a line, how many of his cells/robots/sheds
it touches, and where. Mark each PHYSICAL (a body genuinely does not fit) or CHOSEN (a constant someone
picked). Chosen rules are his to change. Do not ship a quietly-trimmed config and mention the trims after.

**But the design is wrong at least as often as the rule.** Of the seven MapValidation rules settled on
World 2, four came out DESIGN-wrong (garrison gap, factory spawn ring, doorway clearance, cover-vs-cover
overlap) and three RULE-wrong. Never assume either side. Measure, report both numbers, and let the decision
be made on the arithmetic.

**World 2 geometry: Lee draws, we convert and verify.** He has played it and authors the areas himself.
When an authored area breaks a rule, measure it, show him which cells and which rule, hand him the decision.
Do not redesign it and do not ship a redesign in a ticket.

**`a13` (Trolley Yard floor) is a deliberate 1 m-lane maze, permanently exempt from connectivity,
reachability and walkability assertions.** Lee authored it on his own 1 m grid; Max is 1.0 m wide
(`CharacterController` radius 0.5), and large parts of it are deliberately impassable — that is the design,
not a defect. No ticket or test may assert gate-to-gate reachability, replicator-lane reachability, or a
minimum lane width against `a13`; the only walkability check it carries is lane-clear-of-cover. A ticket
that asserts a13 connectivity is mis-shaped. Origin: MV-875 (2026-09-21).

**ONE WRITER for `world2_config.DESIGN.json` — the shaping chat, never the worker.** The worker regenerates
`Assets/_Project/Resources/Worlds/world2_config.json` from it and never edits the DESIGN file, not even to
apply values quoted at it. Always read the file back after writing and quote the changed values in the Jira
comment.

**Coordinate conventions.** `WorldArea.origin`, `decks[]`, `ramps[]`, `sludge[]` are MIN corner.
`WorldCover` is CENTRE, spanning +-w/2, +-d/2. `WorldGrate` is a bare point. `gates[].resolved` is the door
mouth, absolute. Cover being centre-based while everything around it is corner-based is the single trap
that has cost the most time here.

**CI, workflow and signing files.** See `CLAUDE.md` → *CI / workflow / signing files*. Not restated. The
evidence behind it: four tickets with and without an inline AUTHORISATION clause produced four inconsistent
outcomes, which is proof the clause was never a mechanism.

---

## 5. Mechanics

**A blocked worker branch goes stale — re-cut it, never merge or rebase it.** While a ticket sits blocked,
other tickets land, and the branch's diff against `main` then UNDOES them (MV-650 would have reverted the
very gate that unblocked it). Unblocking means: remove `needs-lee`, transition back to Ready for Dev, say so
in the description AND a comment, close the stale PR unmerged, cut fresh from current `origin/main`, and
re-run the fail-first proof against the new base.

**Jira "Blocks" link direction.** `inwardIssue` = the blocker, `outwardIssue` = the blocked ticket. To make
MV-778 blocked by MV-777: `createIssueLink(type:"Blocks", inwardIssue:MV-777, outwardIssue:MV-778)`. Read
back on MV-778 it appears as `{type:"Blocks", inwardIssue:"MV-777"}`.

**Sequencing is a link, not a queue a human holds.** All shaped tickets go to Ready for Dev at once; order
is an "is blocked by" link the worker's free queue check resolves. `needs-lee` means a human DECISION is
required and never "waiting on another ticket".

**Always read a ticket back after creating or editing it** — Jira silently truncates text where a bold span
wraps a line break.
