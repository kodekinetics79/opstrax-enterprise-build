// ZKTeco connector (pull) — talks to ZKTeco fingerprint/face devices over TCP/IP (port 4370).
//
// Uses the optional `node-zklib` dependency. Install it where the agent runs:
//     npm install node-zklib
// (kept optional so the agent runs with mock/csv connectors out of the box).
//
// This is the integration point for the on-prem biometric fleet. The structure is real;
// field names from the device SDK are mapped to the KynexOne ingest punch shape.
export async function pull(device, sinceIso) {
  let ZKLib;
  try {
    ZKLib = (await import('node-zklib')).default;
  } catch {
    console.warn(`[zkteco:${device.name}] node-zklib not installed — run "npm install node-zklib" on the agent host. Skipping.`);
    return [];
  }

  const cfg = device.zkteco || {};
  const zk = new ZKLib(cfg.ip, cfg.port || 4370, cfg.timeout || 10000, 4000);
  const since = sinceIso ? new Date(sinceIso).getTime() : 0;
  try {
    await zk.createSocket();
    const logs = await zk.getAttendances(); // { data: [{ deviceUserId, recordTime, ... }] }
    const punches = (logs?.data || [])
      .map((r) => ({
        employeeCode: String(r.deviceUserId),           // map device enrollment id → EmployeeCode
        punchTimestampUtc: new Date(r.recordTime).toISOString(),
        punchDirection: 'Unknown',                        // ZK devices often don't flag IN/OUT; pairing is done server-side
        verificationMethod: 'Biometric',
      }))
      .filter((p) => new Date(p.punchTimestampUtc).getTime() > since);
    return punches;
  } catch (e) {
    console.error(`[zkteco:${device.name}] pull failed: ${e.message}`);
    return [];
  } finally {
    try { await zk.disconnect(); } catch { /* ignore */ }
  }
}
