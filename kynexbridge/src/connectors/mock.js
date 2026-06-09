// Mock connector — generates configurable test punches so the end-to-end flow
// (agent → /api/attendance/ingest → attendance_records) can be verified without hardware.
export async function pull(device, sinceIso) {
  const punches = (device.mock?.punches || []).map((p) => {
    const ts = new Date(Date.now() + (p.offsetMinutes || 0) * 60_000);
    return {
      employeeCode: p.employeeCode,
      punchTimestampUtc: ts.toISOString(),
      punchDirection: p.direction || 'In',
      verificationMethod: 'Mock',
      confidenceScore: 1.0,
    };
  });
  // Only emit punches newer than the cursor (so repeated polls don't duplicate).
  const since = sinceIso ? new Date(sinceIso).getTime() : 0;
  return punches.filter((p) => new Date(p.punchTimestampUtc).getTime() > since);
}
