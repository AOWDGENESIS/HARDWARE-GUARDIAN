#!/usr/bin/env bash
# Runs every check that is possible without a .NET SDK.
#
# What this cannot do: compile the code, run the tests, or touch real hardware.
# What it does: catch the classes of mistakes this project kept producing by hand
# (wrong member names, missing localisation keys, missing project references, broken syntax,
# binding paths that would silently produce an empty control). The last step breaks the code on
# purpose in a temporary copy: a checker that never fails would otherwise look like proof.
#
# Usage:  bash tools/verify-all.sh
set -u
cd "$(dirname "$0")/.."
status=0
incomplete=0
skipped=0

run() {
  echo
  echo "=== $1 ==="
  shift
  "$@"
  code=$?
  if [ "$code" -eq 3 ]; then
    # The tool could not run (missing dependency). That is not a pass, so the summary below
    # says so and the exit code is 2 instead of 0.
    incomplete=$((incomplete + 1))
  elif [ "$code" -ne 0 ]; then
    status=1
  fi
}

run_optional() {
  # For checks that need something this environment may not have (a network, for example).
  # "Did not run" is reported, but it does not turn the offline verdict into "incomplete",
  # because it was never part of that verdict.
  echo
  echo "=== $1 (optional, needs network) ==="
  shift
  "$@"
  code=$?
  if [ "$code" -eq 3 ]; then
    skipped=$((skipped + 1))
  elif [ "$code" -ne 0 ]; then
    status=1
  fi
}

run "Syntax (tree-sitter, C# grammar)" python3 tools/verify-syntax.py
run "Contracts (members, types, interface implementation)" python3 tools/check-contracts.py
run "Localisation (keys used vs. keys defined)" python3 tools/check-localization.py
run "XAML (well formed, resource keys, DataTypes, code-behind)" python3 tools/check-xaml.py
run "Bindings (every {Binding} root against its data scope)" python3 tools/check-bindings.py
run "Projects (references, central package versions)" python3 tools/check-projects.py
run "Solution file is up to date" python3 tools/generate-solution.py --check
run "Localisation check itself (wrong placeholders must be reported)" python3 tools/check-localization.py --self-test
run "PowerShell scripts (the measuring instruments of the acceptance kit)" python3 tools/check-powershell.py
run "PowerShell check itself (a broken script must be reported)" python3 tools/check-powershell.py --self-test
run "Repository size (the diff has to stay reviewable)" python3 tools/check-repo-size.py
run "The size check itself (an oversized file must be reported)" python3 tools/check-repo-size.py --self-test
run "The checks themselves (deliberate defects must be reported)" python3 tools/check-mutation.py
run_optional "Manufacturer sources (official vendor pages answer over HTTPS)" python3 tools/check-source-urls.py --quiet

echo
if [ "$status" -ne 0 ]; then
  echo "at least one check reported findings (see above)"
  exit 1
fi

if [ "$skipped" -gt 0 ]; then
  echo "$skipped optional check(s) did not run (no network); they are not part of the offline verdict"
fi

if [ "$incomplete" -gt 0 ]; then
  echo "checks passed, but $incomplete check(s) did not run (missing dependency, see above) - this is NOT a complete verification"
  echo "NOT verified: compilation, unit tests, real hardware behaviour - that needs a Windows machine with the .NET 10 SDK."
  exit 2
fi

echo "all available checks passed"
echo "NOT verified: compilation, unit tests, real hardware behaviour - that needs a Windows machine with the .NET 10 SDK."
exit 0
