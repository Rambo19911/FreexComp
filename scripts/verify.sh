#!/usr/bin/env sh
# Full verify: clean build + full test run. Fails when the build fails, when `dotnet test` exits non-zero
# (including assembly-fixture failures that the summary line does not show), when any test is skipped, or when the
# output contains a catastrophic / unhandled failure line.
# Usage: sh scripts/verify.sh [repo-root]   (default: the current directory)
root="${1:-.}"
cd "$root" || exit 2
log="$(mktemp)"

dotnet build FreexComp.sln --no-incremental > "$log" 2>&1
build=$?
grep -E "Warning\(s\)|Error\(s\)" "$log"
if [ "$build" -ne 0 ]; then echo "VERIFY FAILED: build exit $build"; rm -f "$log"; exit 1; fi

dotnet test FreexComp.sln -m:4 --no-build > "$log" 2>&1
tests=$?
grep -E "Passed!|Failed!|aborted|crash|Catastrophic failure|Unhandled exception|Cleanup Failure|^  Failed " "$log"

status=0
[ "$tests" -ne 0 ] && { echo "VERIFY FAILED: dotnet test exit $tests"; status=1; }
grep -qE "Catastrophic failure|Unhandled exception|Cleanup Failure" "$log" && { echo "VERIFY FAILED: catastrophic/unhandled output"; status=1; }
grep -E "Skipped: +[1-9]" "$log" > /dev/null && { echo "VERIFY FAILED: skipped tests"; status=1; }
[ "$status" -eq 0 ] && echo "VERIFY OK"
rm -f "$log"
exit $status
