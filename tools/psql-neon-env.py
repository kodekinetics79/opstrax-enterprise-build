#!/usr/bin/env python3
"""Exec psql from NEON_PG_URI without placing the credential in argv."""

from __future__ import annotations

import os
import sys
from typing import NoReturn
from urllib.parse import parse_qsl, unquote, urlsplit


def fail(message: str) -> NoReturn:
    print(f"ERROR: {message}", file=sys.stderr)
    raise SystemExit(2)


uri = os.environ.get("NEON_PG_URI", "")
if not uri:
    fail("NEON_PG_URI is required")

parsed = urlsplit(uri)
if parsed.scheme not in {"postgres", "postgresql"}:
    fail("NEON_PG_URI must use the postgres or postgresql scheme")
if parsed.fragment or not parsed.hostname or not parsed.path.lstrip("/"):
    fail("NEON_PG_URI must contain a host and database and no fragment")
if parsed.username is None or parsed.password is None:
    fail("NEON_PG_URI must contain an explicit user and password")

database = unquote(parsed.path.lstrip("/"))
username = unquote(parsed.username)
password = unquote(parsed.password)
port = parsed.port or 5432

query_variables = {
    "application_name": "PGAPPNAME",
    "channel_binding": "PGCHANNELBINDING",
    "connect_timeout": "PGCONNECT_TIMEOUT",
    "gssencmode": "PGGSSENCMODE",
    "keepalives": "PGKEEPALIVES",
    "keepalives_count": "PGKEEPALIVESCOUNT",
    "keepalives_idle": "PGKEEPALIVESIDLE",
    "keepalives_interval": "PGKEEPALIVESINTERVAL",
    "options": "PGOPTIONS",
    "sslcert": "PGSSLCERT",
    "sslkey": "PGSSLKEY",
    "sslmode": "PGSSLMODE",
    "sslpassword": "PGSSLPASSWORD",
    "sslrootcert": "PGSSLROOTCERT",
    "target_session_attrs": "PGTARGETSESSIONATTRS",
}
query_options: dict[str, str] = {}
for key, value in parse_qsl(parsed.query, keep_blank_values=True, strict_parsing=True):
    variable = query_variables.get(key)
    if variable is None:
        fail(f"unsupported NEON_PG_URI parameter: {key}")
    if key in query_options:
        fail(f"duplicate NEON_PG_URI parameter: {key}")
    query_options[key] = value

# Neon credentials and release DDL must never fall back to libpq's permissive
# `sslmode=prefer` default. Validate this before even returning the safe target.
if parsed.hostname.lower().endswith(".neon.tech"):
    if query_options.get("sslmode") not in {"require", "verify-full"}:
        fail("Neon connections require sslmode=require or sslmode=verify-full")
    if query_options.get("channel_binding") != "require":
        fail("Neon connections require channel_binding=require")

if sys.argv[1:] == ["--print-safe-target"]:
    print(f"{parsed.hostname}\t{port}\t{database}\t{username}")
    raise SystemExit(0)

env = {key: value for key, value in os.environ.items() if not key.startswith("PG") and key != "NEON_PG_URI"}
env["PGHOST"] = parsed.hostname
env["PGDATABASE"] = database
env["PGUSER"] = username
env["PGPASSWORD"] = password
env["PGPORT"] = str(port)

for key, value in query_options.items():
    variable = query_variables[key]
    env[variable] = value

os.execvpe("psql", ["psql", "-X", *sys.argv[1:]], env)
