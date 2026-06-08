#!/usr/bin/env node
import { loadConfig, loadCursors } from './config.js';
import { pollOnce } from './poller.js';

const cfg = loadConfig();
const cursors = loadCursors(cfg.cursorFile);
const once = process.argv.includes('--once');

console.log(`KynexBridge starting · API ${cfg.apiBaseUrl} · ${cfg.devices.length} device(s) · interval ${cfg.pollIntervalSeconds}s${once ? ' · single run' : ''}`);

async function loop() {
  await pollOnce(cfg, cursors);
  if (once) { process.exit(0); }
}

await loop();
if (!once) {
  setInterval(loop, cfg.pollIntervalSeconds * 1000);
  process.on('SIGINT', () => { console.log('\nKynexBridge stopped.'); process.exit(0); });
}
