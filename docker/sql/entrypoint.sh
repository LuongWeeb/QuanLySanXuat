#!/bin/bash
set -euo pipefail

MSSQL_SA_PASSWORD="$({ cat /run/secrets/mssql_sa_password || exit 1; printf x; })"
MSSQL_SA_PASSWORD="${MSSQL_SA_PASSWORD%x}"
if [[ "$MSSQL_SA_PASSWORD" == *$'\r\n' ]]; then
    MSSQL_SA_PASSWORD="${MSSQL_SA_PASSWORD%$'\r\n'}"
elif [[ "$MSSQL_SA_PASSWORD" == *$'\n' ]]; then
    MSSQL_SA_PASSWORD="${MSSQL_SA_PASSWORD%$'\n'}"
fi

if [[ -z "$MSSQL_SA_PASSWORD" ]]; then
    echo "The mssql_sa_password secret must not be empty after normalization." >&2
    exit 1
fi

export MSSQL_SA_PASSWORD
exec /opt/mssql/bin/sqlservr
