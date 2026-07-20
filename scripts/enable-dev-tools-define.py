#!/usr/bin/env python3
"""
Compile the dev tuning panel INTO an iOS build (YT-118).

Adds the MAXWORLDS_DEV_TOOLS scripting define to the iOS player settings, in the
working tree only, immediately before CI runs the Unity build. The committed
ProjectSettings deliberately does NOT carry the define, so the safe default — and
anything built for the App Store — ships without the panel. A beta build opts in by
running this; nothing opts in by forgetting.

Why a script and not a line of sed in the workflow: the failure that matters here is
the SILENT one. A sed that matches nothing exits 0, the build succeeds, TestFlight
gets a build with no sliders in it, and the only symptom is Lee not finding a button.
So this verifies its own work and exits non-zero if the define is not actually there
when it finishes.

Why not an -executeMethod build script: CI builds through GameCI's own builder, so a
custom -executeMethod never fires (YT-104), and GameCI does not forward environment
variables into the Unity container either (YT-117). Editing the settings file the
build is about to read is the one channel that does not depend on either.

Usage:  python3 scripts/enable-dev-tools-define.py [--define NAME] [--platform KEY]
"""

import argparse
import pathlib
import re
import sys

SETTINGS = pathlib.Path("ProjectSettings/ProjectSettings.asset")
HEADER = "  scriptingDefineSymbols:"


def add_define(text: str, platform: str, define: str) -> str:
    empty = f"{HEADER} {{}}"
    if empty in text:
        return text.replace(empty, f"{HEADER}\n    {platform}: {define}", 1)

    if HEADER not in text:
        raise SystemExit(
            "ProjectSettings.asset has no scriptingDefineSymbols key at all — the file "
            "is not the shape this script was written for. Refusing to guess."
        )

    lines = text.splitlines(keepends=True)
    out, i = [], 0
    while i < len(lines):
        line = lines[i]
        out.append(line)
        i += 1
        if line.rstrip("\n") != HEADER:
            continue

        # Walk this block's entries, looking for the platform already present.
        handled = False
        while i < len(lines) and re.match(r"^    \S", lines[i]):
            entry = lines[i]
            key, _, value = entry.partition(":")
            if key.strip() == platform:
                current = value.strip()
                if define not in current.split(";"):
                    joined = f"{current};{define}" if current else define
                    entry = f"    {platform}: {joined}\n"
                handled = True
            out.append(entry)
            i += 1

        if not handled:
            out.append(f"    {platform}: {define}\n")

    return "".join(out)


def defined_for(text: str, platform: str, define: str) -> bool:
    """Read the file back the way Unity will: is the define listed under the platform?"""
    block = re.search(r"^  scriptingDefineSymbols:\n((?:    \S.*\n)*)", text, re.MULTILINE)
    if not block:
        return False
    for line in block.group(1).splitlines():
        key, _, value = line.partition(":")
        if key.strip() == platform:
            return define in [d.strip() for d in value.strip().split(";")]
    return False


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("--define", default="MAXWORLDS_DEV_TOOLS")
    ap.add_argument("--platform", default="iPhone", help="Unity build-target key, e.g. iPhone")
    args = ap.parse_args()

    if not SETTINGS.exists():
        raise SystemExit(f"{SETTINGS} not found — run this from the repo root.")

    before = SETTINGS.read_text(encoding="utf-8")
    after = add_define(before, args.platform, args.define)
    SETTINGS.write_text(after, encoding="utf-8", newline="")

    # Verify against what is now on disk, not against what we think we wrote.
    if not defined_for(SETTINGS.read_text(encoding="utf-8"), args.platform, args.define):
        print(
            f"::error::Failed to add {args.define} for {args.platform}. The build would "
            f"have shipped WITHOUT the dev tuning panel and nothing else would have said so.",
            file=sys.stderr,
        )
        return 1

    print(f"{args.define} is set for {args.platform} — the dev tuning panel will be in this build.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
