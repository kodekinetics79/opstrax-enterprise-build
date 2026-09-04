from pathlib import Path
import re

path = Path("backend-dotnet/Services/Batch6SchemaService.cs")
source = path.read_text()

replacements = [
    (
        "drive_time_remaining_minutes INT NOT NULL DEFAULT 660,",
        "drive_time_remaining_minutes INT NULL,",
    ),
    (
        "shift_time_remaining_minutes INT NOT NULL DEFAULT 840,",
        "shift_time_remaining_minutes INT NULL,",
    ),
    (
        "cycle_time_remaining_minutes INT NOT NULL DEFAULT 4200,",
        "cycle_time_remaining_minutes INT NULL,",
    ),
    (
        "status VARCHAR(80) NOT NULL DEFAULT 'OK',\n            hos_warning",
        "status VARCHAR(80) NOT NULL DEFAULT 'Unavailable',\n            hos_warning",
    ),
]

for old, new in replacements:
    if new in source:
        continue
    count = source.count(old)
    if count != 1:
        raise SystemExit(f"expected exactly one Batch6 HOS fragment {old!r}; found {count}")
    source = source.replace(old, new, 1)

column_anchor = '''        new("hos_clocks",               "company_id",            "BIGINT NULL"),
        new("hos_clocks",               "branch_id",             "BIGINT NULL"),
'''
column_truth = '''        new("hos_clocks",               "company_id",            "BIGINT NULL"),
        new("hos_clocks",               "branch_id",             "BIGINT NULL"),
        new("hos_clocks",               "clock_source",          "VARCHAR(80) NULL"),
        new("hos_clocks",               "source_event_id",       "VARCHAR(160) NULL"),
        new("hos_clocks",               "source_observed_at",    "TIMESTAMPTZ NULL"),
        new("hos_clocks",               "source_authority",      "VARCHAR(32) NOT NULL DEFAULT 'LegacyUnverified'"),
        new("hos_clocks",               "source_quality",        "VARCHAR(32) NULL"),
'''
if 'new("hos_clocks",               "source_authority",' not in source:
    if source.count(column_anchor) != 1:
        raise SystemExit("unique hos_clocks ColumnDefinition anchor not found")
    source = source.replace(column_anchor, column_truth, 1)

create_start = source.find('@"CREATE TABLE IF NOT EXISTS hos_clocks (')
create_end = source.find('@"CREATE TABLE IF NOT EXISTS hos_certifications (', create_start)
if create_start == -1 or create_end == -1:
    raise SystemExit("hos_clocks CREATE TABLE block not found")
create_block = source[create_start:create_end]

table_anchor = '''            status VARCHAR(80) NOT NULL DEFAULT 'Unavailable',
            hos_warning VARCHAR(200) NULL,
            updated_at TIMESTAMPTZ NULL
'''
table_truth = '''            status VARCHAR(80) NOT NULL DEFAULT 'Unavailable',
            hos_warning VARCHAR(200) NULL,
            clock_source VARCHAR(80) NULL,
            source_event_id VARCHAR(160) NULL,
            source_observed_at TIMESTAMPTZ NULL,
            source_authority VARCHAR(32) NOT NULL DEFAULT 'LegacyUnverified',
            source_quality VARCHAR(32) NULL,
            updated_at TIMESTAMPTZ NULL
'''
if "source_authority VARCHAR(32) NOT NULL DEFAULT 'LegacyUnverified'" not in create_block:
    if create_block.count(table_anchor) != 1:
        raise SystemExit("unique hos_clocks CREATE TABLE source-truth anchor not found")
    create_block = create_block.replace(table_anchor, table_truth, 1)
    source = source[:create_start] + create_block + source[create_end:]

seed_start = source.find('@"INSERT INTO hos_clocks (id,driver_id,country_code,profile_id,cycle_type,drive_time_remaining_minutes,shift_time_remaining_minutes,cycle_time_remaining_minutes,status,hos_warning)')
if seed_start == -1:
    raise SystemExit("hos_clocks demo seed block not found")
seed_end_marker = 'ON CONFLICT DO NOTHING"'
seed_end = source.find(seed_end_marker, seed_start)
if seed_end == -1:
    raise SystemExit("hos_clocks demo seed block terminator not found")
seed_end += len(seed_end_marker)
seed_block = source[seed_start:seed_end]
row_pattern = re.compile(
    r"\((\d+),(\d+),'([^']+)',(\d+),'([^']+)',(?:\d+|NULL),(?:\d+|NULL),(?:\d+|NULL),'[^']+',(?:NULL|'[^']*')\)"
)

def fail_closed_seed(match: re.Match[str]) -> str:
    row_id, driver_id, country, profile_id, cycle = match.groups()
    return (
        f"({row_id},{driver_id},'{country}',{profile_id},'{cycle}',"
        "NULL,NULL,NULL,'Unavailable','Authoritative HOS source required')"
    )

repaired_seed, replaced_rows = row_pattern.subn(fail_closed_seed, seed_block)
if replaced_rows not in (0, 10):
    raise SystemExit(f"expected 10 HOS demo rows or already-repaired rows; transformed {replaced_rows}")
if replaced_rows == 0:
    expected = "NULL,NULL,NULL,'Unavailable','Authoritative HOS source required'"
    if seed_block.count(expected) != 10:
        raise SystemExit("HOS demo seed block is neither original nor fully fail-closed")
else:
    source = source[:seed_start] + repaired_seed + source[seed_end:]

path.write_text(source)
