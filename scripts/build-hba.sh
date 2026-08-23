#!/usr/bin/env sh
set -eu

ROOT_DIR="$(CDPATH= cd -- "$(dirname -- "$0")/.." && pwd)"

cd "$ROOT_DIR"

# MSBuild 17.14 / .NET SDK 9.0.315 can stall while resolving project-reference
# target frameworks for this solution when the default parallel build is used.
# A single MSBuild worker keeps the build deterministic.
exec dotnet build HBA.sln --no-restore -v:minimal /nr:false /m:1 "$@"
