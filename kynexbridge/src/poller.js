import { getConnector } from './connectors/index.js';
import { saveCursors } from './config.js';

// Forward a batch of punches for one device to the KynexOne ingest webhook.
async function forward(apiBaseUrl, device, punches) {
  const res = await fetch(`${apiBaseUrl.replace(/\/$/, '')}/api/attendance/ingest`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', 'X-Device-Key': device.deviceKey },
    body: JSON.stringify({ punches, autoProcess: true }),
  });
  if (!res.ok) {
    const text = await res.text().catch(() => '');
    throw new Error(`ingest HTTP ${res.status} ${text}`);
  }
  return res.json();
}

export async function pollOnce(cfg, cursors) {
  for (const device of cfg.devices) {
    const tag = `[${device.name}]`;
    if (!device.deviceKey || device.deviceKey.includes('REPLACE')) {
      console.warn(`${tag} skipped — deviceKey not set.`);
      continue;
    }
    try {
      const connector = getConnector(device.connector);
      const since = cursors[device.name];
      const punches = await connector.pull(device, since);
      if (!punches.length) { console.log(`${tag} no new punches.`); continue; }

      const result = await forward(cfg.apiBaseUrl, device, punches);
      const latest = punches.reduce((m, p) => (p.punchTimestampUtc > m ? p.punchTimestampUtc : m), since || '');
      cursors[device.name] = latest;
      saveCursors(cfg.cursorFile, cursors);
      console.log(`${tag} forwarded ${punches.length} → accepted=${result.accepted} dup=${result.duplicates} unmatched=${result.unmatched} processed=${result.processed}`);
    } catch (e) {
      console.error(`${tag} ERROR: ${e.message}`);
    }
  }
}
