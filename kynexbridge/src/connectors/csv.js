// CSV-folder connector — for file-only / legacy devices that export logs.
// Watches an inbox directory, parses each CSV into punches, then archives the file.
import { readdirSync, readFileSync, renameSync, mkdirSync, existsSync } from 'node:fs';
import { join } from 'node:path';

export async function pull(device) {
  const cfg = device.csv || {};
  const inbox = cfg.inboxDir || './inbox';
  const archive = cfg.archiveDir || './archive';
  const cols = cfg.columns || { employeeCode: 0, timestamp: 1, direction: 2 };
  if (!existsSync(inbox)) { mkdirSync(inbox, { recursive: true }); return []; }
  if (!existsSync(archive)) mkdirSync(archive, { recursive: true });

  const files = readdirSync(inbox).filter((f) => f.toLowerCase().endsWith('.csv'));
  const punches = [];
  for (const file of files) {
    const full = join(inbox, file);
    const lines = readFileSync(full, 'utf8').split(/\r?\n/).filter(Boolean);
    const rows = cfg.hasHeader ? lines.slice(1) : lines;
    for (const line of rows) {
      const parts = line.split(',').map((s) => s.trim());
      const code = parts[cols.employeeCode];
      const rawTs = parts[cols.timestamp];
      if (!code || !rawTs) continue;
      const ts = new Date(rawTs);
      if (isNaN(ts.getTime())) continue;
      punches.push({
        employeeCode: code,
        punchTimestampUtc: ts.toISOString(),
        punchDirection: parts[cols.direction] || 'Unknown',
        verificationMethod: 'File import',
      });
    }
    // archive processed file so it isn't re-read
    renameSync(full, join(archive, `${Date.now()}_${file}`));
  }
  return punches;
}
