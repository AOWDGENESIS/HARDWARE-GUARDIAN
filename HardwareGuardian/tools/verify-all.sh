#!/usr/bin/env bash
# Runs every check that is possible without a .NET SDK.
#
# What this cannot do: compile the code, run the tests, or touch real hardware.
# What it does: catch the classes of mistakes this project kept producing by hand
# (wrong member names, missing localisation keys, missing project references, broken syntax).
#
# Usage:  bash tools/verify-all.sh
set -u
cd "$(dirname "$0")/.."
status=0

run() {
  echo
  echo "=== $1 ==="
  shift
  if ! "$@"; then
    status=1
  fi
}

run "Syntax (tree-sitter, C# grammar)" python3 tools/verify-syntax.py
run "Contracts (members, types, interface implementation)" python3 tools/check-contracts.py
run "Localisation (keys used vs. keys defined)" python3 tools/check-localization.py
run "XAML (well formed, resource keys, DataTypes, code-behind)" python3 tools/check-xaml.py
run "Projects (references, central package versions)" python3 tools/check-projects.py
run "Solution file is up to date" python3 tools/generate-solution.py --check

echo
if [ "$status" -eq 0 ]; then
  echo "all available checks passed"
  echo "NOT verified: compilation, unit tests, real hardware behaviour - that needs a Windows machine with the .NET 10 SDK."
else
  echo "at least one check reported findings (see above)"
fi
exit "$status"
