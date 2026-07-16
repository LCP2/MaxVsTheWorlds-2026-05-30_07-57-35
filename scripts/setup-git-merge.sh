#!/usr/bin/env sh
# Per-clone git hygiene for YT-103. Run once on every CC start (idempotent):
#
#     sh scripts/setup-git-merge.sh
#
# The real fix is committed in .gitattributes: Unity YAML files (.unity/.prefab/
# .asset) carry NO merge driver, so git uses its built-in 3-way text merge and
# writes standard conflict markers instead of launching UnityYAMLMerge's GUI.
#
# This script is belt-and-suspenders: it strips any stale `merge.unityyamlmerge`
# driver left in this clone's .git/config (older setups pointed it at UnityYAMLMerge
# with broken $BASE/$REMOTE placeholders, which is what popped the blocking dialog).
# With the attribute unset nothing invokes it anyway, but removing it means a future
# re-add of the attribute can't resurrect the GUI.
set -eu

cd "$(git rev-parse --show-toplevel)"

for key in driver name recursive; do
  git config --unset "merge.unityyamlmerge.$key" 2>/dev/null || true
done
# Drop the now-empty section header if present (ignore failure — it may not exist).
git config --remove-section merge.unityyamlmerge 2>/dev/null || true

echo "[setup-git-merge] Stale UnityYAMLMerge driver removed from this clone."
echo "[setup-git-merge] .unity/.prefab/.asset now use git's 3-way text merge (conflict markers, no GUI)."
echo "[setup-git-merge] verify: git check-attr merge -- ProjectSettings/ProjectSettings.asset  # expect 'merge: unspecified'"
