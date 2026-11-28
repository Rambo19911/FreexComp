#!/usr/bin/env sh
# Full verify: clean build + full test run. Fails when the build fails, when `dotnet test` exits non-zero
# (including assembly-fixture failures that the summary line does not show), when any test is skipped, or when the
# output contains a catastrophic / unhandled failure line.
# Usage: sh scripts/verify.sh [repo-root]   (default: the current directory)
# On failure the full log is kept and its path printed (VERIFY LOG: ...).
root="${1:-.}"
cd "$root" || exit 2
log="$(mktemp)"

dotnet build FreexComp.sln --no-incremental > "$log" 2>&1
build=$?
grep -E "Warning\(s\)|Error\(s\)" "$log"
if [ "$build" -ne 0 ]; then echo "VERIFY FAILED: build exit $build"; echo "VERIFY LOG: $log"; exit 1; fi

# A single test that runs longer than 5 minutes is a hang: its test host is stopped (no dump) and the run reports it.
# Two parts: everything but the performance tests with four test hosts at once, then the performance tests of all
# projects one host at a time, so their budgets are measured on a quiet machine (a project without performance tests
# prints no summary line in the second part).
echo "== tests (parallel, Category!=Performance) ==" > "$log"
dotnet test FreexComp.sln -m:4 --no-build --filter "Category!=Performance" --blame-hang-timeout 5m --blame-hang-dump-type none >> "$log" 2>&1
tests=$?
echo "== performance tests (serial, Category=Performance) ==" >> "$log"
dotnet test FreexComp.sln -m:1 --no-build --filter "Category=Performance" --blame-hang-timeout 5m --blame-hang-dump-type none >> "$log" 2>&1
perf=$?
[ "$tests" -eq 0 ] && tests=$perf
# GUI programs a stopped test host left behind (the full verify runs only when no agent runs tests).
taskkill //IM AccessibilityProbe.exe //F >/dev/null 2>&1
grep -E "^== |Passed!|Failed!|aborted|crash|Catastrophic failure|Unhandled exception|Cleanup Failure|^  Failed " "$log"

status=0
[ "$tests" -ne 0 ] && { echo "VERIFY FAILED: dotnet test exit $tests"; status=1; }
grep -qE "Catastrophic failure|Unhandled exception|Cleanup Failure" "$log" && { echo "VERIFY FAILED: catastrophic/unhandled output"; status=1; }
grep -E "Skipped: +[1-9]" "$log" > /dev/null && { echo "VERIFY FAILED: skipped tests"; status=1; }
if [ "$status" -eq 0 ]; then echo "VERIFY OK"; rm -f "$log"; else echo "VERIFY LOG: $log"; fi
exit $status
