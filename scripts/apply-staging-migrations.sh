#!/usr/bin/env bash
# ==============================================================================
# CourtBook (PlaySpot) — Apply Existing EF Core Migrations to Staging Database
# ==============================================================================
# SAFETY RULES:
# 1. This script ONLY applies existing migrations from the repository.
# 2. It NEVER creates, modifies, or drops migrations.
# 3. It NEVER executes destructive database drops or schema resets.
# ==============================================================================

set -euo pipefail

CONNECTION_STRING="${1:-${ConnectionStrings__DefaultConnection:-}}"

if [ -z "$CONNECTION_STRING" ]; then
    echo "ERROR: Connection string is required."
    echo "Usage: $0 \"<STAGING_SQL_CONNECTION_STRING>\""
    echo "   Or: export ConnectionStrings__DefaultConnection=\"<STAGING_SQL_CONNECTION_STRING>\" && $0"
    exit 1
fi

echo "=============================================================================="
echo "CourtBook — Applying Existing Migrations to Staging Database"
echo "=============================================================================="

# Navigate to solution root (directory of this script / ..)
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$ROOT_DIR"

echo "Listing currently recorded migrations in repository..."
dotnet ef migrations list \
    --project src/CourtBook.Infrastructure \
    --startup-project src/CourtBook.API

echo ""
echo "Applying migrations to target database..."
dotnet ef database update \
    --project src/CourtBook.Infrastructure \
    --startup-project src/CourtBook.API \
    --connection "$CONNECTION_STRING"

echo ""
echo "SUCCESS: All existing migrations applied successfully to staging database."
echo "=============================================================================="
