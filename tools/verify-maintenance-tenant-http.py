#!/usr/bin/env python3
"""Exercise real HTTP authentication and maintenance routes on a disposable local DB.

Requires OPSTRAX_MAINT_HTTP_OWNER_URI, OPSTRAX_MAINT_HTTP_APP_URI and
OPSTRAX_MAINT_HTTP_API_URL. Never use a production database. Seeds synthetic
companies/users/vehicles and records; the disposable database is removed by its runner.
No bearer tokens, password hashes or database credentials are printed or exported.
"""
import base64
import concurrent.futures
import hashlib
import json
import os
from pathlib import Path
import secrets
import subprocess
from urllib.parse import urlparse
from urllib.request import Request, urlopen
from urllib.error import HTTPError


def local_uri(name, database=False):
    value = os.environ[name]
    parsed = urlparse(value)
    if parsed.hostname not in ("127.0.0.1", "localhost"):
        raise RuntimeError(f"{name} must target loopback, never production")
    if database and not parsed.path.lstrip("/").startswith(("opstrax_maintenance_http", "opstrax_prod_rehearsal_")):
        raise RuntimeError("Only explicitly named disposable maintenance HTTP databases are allowed")
    return value


OWNER = local_uri("OPSTRAX_MAINT_HTTP_OWNER_URI", True)
APP = local_uri("OPSTRAX_MAINT_HTTP_APP_URI", True)
API = local_uri("OPSTRAX_MAINT_HTTP_API_URL").rstrip("/")
checks = []


def sql(statement, uri=OWNER):
    # The URI contains credentials: environment only, never command arguments/output.
    parsed = urlparse(uri)
    env = dict(os.environ, PGHOST=parsed.hostname, PGPORT=str(parsed.port or 5432),
               PGUSER=parsed.username, PGPASSWORD=parsed.password,
               PGDATABASE=parsed.path.lstrip("/"), PGSSLMODE="disable")
    p = subprocess.run(["psql", "-X", "-q", "-A", "-t", "-v", "ON_ERROR_STOP=1"],
                       input=statement, text=True, capture_output=True, env=env)
    if p.returncode:
        raise RuntimeError("Disposable fixture SQL failed: " + p.stderr)
    return p.stdout.strip()


def q(value):
    return "'" + str(value).replace("'", "''") + "'"


def insert(table, columns, values):
    return int(sql(f"INSERT INTO {table} ({columns}) VALUES ({values}) RETURNING id;"))


def request(path, token=None, body=None):
    headers = {"Accept": "application/json"}
    if token:
        headers["Authorization"] = "Bearer " + token
    data = None
    if body is not None:
        headers["Content-Type"] = "application/json"
        data = json.dumps(body).encode()
    req = Request(API + path, data=data, headers=headers)
    try:
        with urlopen(req, timeout=45) as response:
            return response.status, json.load(response)
    except HTTPError as error:
        return error.code, json.loads(error.read())


def check(name, condition):
    if not condition:
        raise AssertionError(name)
    checks.append(name)
    print("PASS: " + name, flush=True)


def login(company, user):
    status, response = request("/api/auth/login", body={
        "companyCode": company["code"], "email": user["email"], "password": password})
    check(f"Real login succeeds: {user['label']}", status == 200 and response["success"])
    check(f"Login binds correct company: {user['label']}",
          int(response["data"]["company"]["id"]) == company["id"])
    return response["data"]["token"]


def rows(token, path="/api/maintenance"):
    status, response = request(path, token)
    if status != 200 or not response.get("success"):
        raise AssertionError(f"Maintenance list did not succeed: HTTP {status}")
    return response["data"]


def row_ids(token, path="/api/maintenance"):
    return {int(row["id"]) for row in rows(token, path)}


suffix = secrets.token_hex(6)
password = "HttpVerification!" + secrets.token_hex(16)
salt = secrets.token_bytes(16)
hash_value = "PBKDF2$100000$" + base64.b64encode(salt).decode() + "$" + base64.b64encode(
    hashlib.pbkdf2_hmac("sha256", password.encode(), salt, 100000, 32)).decode()

status, readiness = request("/health/ready")
contract = readiness.get("checks", {}).get("fleet_production_contract", {})
check("Real API runs in Production with valid restricted-role RLS contract",
      status == 200 and readiness.get("environment") == "Production" and
      contract.get("role_restricted") is True and contract.get("rls_violations") == 0 and
      contract.get("tenant_coverage_violations") == 0 and contract.get("tenant_ticket_migration_applied") is True)

# Prove the oracle is a real restricted identity, not an owner masquerading as RLS.
identity = json.loads(sql("SELECT json_build_object('role',current_user,'super',rolsuper,"
                          "'bypass',rolbypassrls) FROM pg_roles WHERE rolname=current_user;", APP))
check("Database app identity is opstrax_app, not superuser/BYPASSRLS",
      identity == {"role": "opstrax_app", "super": False, "bypass": False})
policy = json.loads(sql("SELECT json_build_object('enabled',relrowsecurity,'forced',relforcerowsecurity) "
                       "FROM pg_class WHERE oid='public.maintenance_items'::regclass;"))
check("Maintenance table has ENABLE and FORCE RLS", policy == {"enabled": True, "forced": True})

companies = []
for label in ("A", "B"):
    code = f"HTTP-RLS-{label}-{suffix}"
    cid = insert("companies", "company_code,name,industry,status,country,currency,entitlement_policy_mode",
                 f"{q(code)},{q('Synthetic HTTP isolation '+label)},'Test','Active','US','USD','package_allowlist'")
    sql(f"INSERT INTO tenant_entitlements(company_id,module_key,enabled) VALUES({cid},'maintenance',true);")
    companies.append({"id": cid, "code": code})
A, B = companies


def branch(company, code):
    return insert("branches", "company_id,branch_code,name,status,country_code",
                  f"{company['id']},{q(code+'-'+suffix)},{q(code)},'Active','US'")


branch_a1, branch_a2, branch_b = branch(A, "A1"), branch(A, "A2"), branch(B, "B1")


def user(company, label, allowed=True, branch_id=None):
    permissions = ["maintenance:view"] if allowed else ["fleet:view"]
    role = insert("roles", "company_id,name,permissions_json,is_system",
                  f"{company['id']},{q('HTTP '+label+' '+suffix)},{q(json.dumps(permissions))}::jsonb,false")
    email = f"{label.lower()}-{suffix}@example.invalid"
    uid = insert("users", "company_id,branch_id,role_id,full_name,email,role_name,password_hash,permissions_json,status",
                 f"{company['id']},{branch_id if branch_id else 'NULL'},{role},{q('Synthetic '+label)},"
                 f"{q(email)},{q('HTTP '+label+' '+suffix)},{q(hash_value)},{q(json.dumps(permissions))}::jsonb,'Active'")
    return {"id": uid, "email": email, "label": label}


user_a = user(A, "A-viewer")
user_b = user(B, "B-viewer")
user_branch = user(A, "A1-viewer", branch_id=branch_a1)
user_denied = user(A, "A-denied", allowed=False)


def maintenance(company, branch_id, marker):
    code = marker + "-" + suffix
    vehicle = insert("vehicles", "company_id,branch_id,vehicle_code,type,status,vin_exception_type,alternate_identifier",
                     f"{company['id']},{branch_id},{q(code)},'Truck','Available','legacy-fleet-identifier',{q(code)}")
    return insert("maintenance_items", "company_id,vehicle_id,title,category,service_type,status,priority,due_date,estimated_cost,risk_score,data_origin,verification_status",
                  f"{company['id']},{vehicle},{q(code)},'Preventive Maintenance','Oil Change','Open','High',"
                  "CURRENT_DATE+7,123.45,40,'user_workflow','recorded_by_authenticated_actor'")


record_a1 = maintenance(A, branch_a1, "HTTP-A1")
record_a2 = maintenance(A, branch_a2, "HTTP-A2")
record_b = maintenance(B, branch_b, "HTTP-B1")
token_a, token_b = login(A, user_a), login(B, user_b)
token_branch, token_denied = login(A, user_branch), login(A, user_denied)

for path in ("/api/maintenance", f"/api/maintenance/{record_a1}"):
    status, response = request(path)
    check(f"No token denied with 401: {path}", status == 401 and not response.get("success"))
    status, response = request(path, "not-a-valid-session")
    check(f"Invalid token denied with 401: {path}", status == 401 and not response.get("success"))
    status, response = request(path, token_denied)
    check(f"Authenticated user without maintenance grant denied with 403: {path}",
          status == 403 and not response.get("success"))

check("Company A list contains exactly its two records", row_ids(token_a) == {record_a1, record_a2})
check("Company B list contains exactly its own record", row_ids(token_b) == {record_b})
check("Company A branch A1 excludes branch A2 and company B", row_ids(token_branch) == {record_a1})
for token, own, foreign, label in ((token_a, record_a1, record_b, "A"), (token_b, record_b, record_a1, "B")):
    status, response = request(f"/api/maintenance/{own}", token)
    check(f"Company {label} can read its own detail", status == 200 and int(response["data"]["record"]["id"]) == own)
    status, response = request(f"/api/maintenance/{foreign}", token)
    check(f"Company {label} foreign-company detail returns 404 without data",
          status == 404 and response.get("data") is None)
    foreign_cid = B["id"] if label == "A" else A["id"]
    check(f"Company {label} cannot override tenant using query parameters",
          row_ids(token, f"/api/maintenance?companyId={foreign_cid}&company_id={foreign_cid}&allUsers=1") ==
          ({record_a1, record_a2} if label == "A" else {record_b}))
status, response = request(f"/api/maintenance/{record_a2}", token_branch)
check("Other-branch detail returns 404 without data", status == 404 and response.get("data") is None)


def concurrent_request(index):
    token, expected = (token_a, {record_a1, record_a2}) if index % 2 == 0 else (token_b, {record_b})
    assert row_ids(token) == expected, "Cross-company visibility during concurrent pooled HTTP requests"


with concurrent.futures.ThreadPoolExecutor(max_workers=8) as executor:
    list(executor.map(concurrent_request, range(60)))
check("60 interleaved concurrent authenticated HTTP reads retain company isolation", True)
check("Unscoped restricted-role query returns zero rows after HTTP pool reuse",
      sql("SELECT count(*) FROM maintenance_items;", APP) == "0")

evidence = {"status": "passed", "transport": "real HTTP with real login/session middleware",
            "release": readiness.get("version"), "timestamp": readiness.get("timestamp"),
            "database": "disposable local PostgreSQL; restricted opstrax_app with signed tenant tickets",
            "checks": checks, "concurrent_reads": 60,
            "production_data_changed": False}
if output := os.environ.get("OPSTRAX_MAINT_HTTP_REPORT"):
    Path(output).write_text(json.dumps(evidence, indent=2) + "\n")
print(f"Maintenance HTTP isolation verification passed: {len(checks)} checks and 60 concurrent reads.")
