import assert from "node:assert/strict";
import { existsSync, readFileSync } from "node:fs";
import { createRequire } from "node:module";
import { runInNewContext } from "node:vm";
import test from "node:test";
import ts from "typescript";
import { renderToStaticMarkup } from "react-dom/server";
import * as icons from "lucide-react";

const require = createRequire(import.meta.url);
const utilityUrl = new URL("../src/utils/telemetryMeasurements.ts", import.meta.url);
const measurements = existsSync(utilityUrl) ? await import(utilityUrl.href) : {};

// Execute the actual production functions, including JSX, without mounting the app,
// contacting an API or claiming rendered-browser coverage. No copied implementation.
function loadDeclarations(path, names, bindings = {}) {
  const filename = new URL(path, import.meta.url);
  const source = readFileSync(filename, "utf8");
  const tree = ts.createSourceFile(filename.pathname, source, ts.ScriptTarget.Latest, true, ts.ScriptKind.TSX);
  const selected = tree.statements.filter((node) => {
    if (ts.isFunctionDeclaration(node)) return names.includes(node.name?.text);
    return ts.isVariableStatement(node) && node.declarationList.declarations.some((d) => names.includes(d.name.getText(tree)));
  });
  const present = names.filter((name) => selected.some((node) =>
    ts.isFunctionDeclaration(node) ? node.name?.text === name : node.declarationList.declarations.some((d) => d.name.getText(tree) === name)));
  const code = ts.transpileModule(`${selected.map((node) => node.getText(tree)).join("\n")}\nexport { ${present.join(",")} };`, {
    compilerOptions: { module: ts.ModuleKind.CommonJS, target: ts.ScriptTarget.ES2022, jsx: ts.JsxEmit.ReactJSX },
  }).outputText;
  const exports = {};
  runInNewContext(code, { exports, require, ...measurements, ...bindings }, { filename: filename.pathname });
  return exports;
}

const { toPosition } = loadDeclarations("../src/hooks/useLiveTelemetry.ts", ["toPosition"]);
const { statusBucket } = loadDeclarations("../src/pages/LiveMapPage.tsx", ["hasValidPosition", "statusBucket"]);
const { makeVehicleIcon } = loadDeclarations("../src/components/LiveMap.tsx", ["isMovingState", "markerColor", "makeVehicleIcon"], {
  L: { divIcon: (options) => options }, markerSourceRing: () => "", freshnessColor: () => "#999999",
});
const { ReplayTrail, headingLabel } = loadDeclarations("../src/pages/VehiclesPage.tsx", ["g", "num", "ReplayTrail", "headingLabel"]);
const fresh = { lat: 34.05, lng: -118.24, secondsSincePing: 10, freshness: "live" };

test("live hook preserves missing speed and heading as explicit null", () => {
  const position = toPosition({ ...fresh, speedMph: null, heading: null });
  assert.equal(position.speedMph, null);
  assert.equal(position.heading, null);
});

test("live hook preserves real zero speed and north heading", () => {
  const position = toPosition({ ...fresh, speedMph: 0, heading: 0 });
  assert.equal(position.speedMph, 0);
  assert.equal(position.heading, 0);
});

test("fresh GPS without speed has unknown motion rather than idle", () => {
  assert.equal(statusBucket({ ...fresh, speedMph: null, status: "Available" }), "Unknown");
});

test("operational on-route status does not prove motion without speed", () => {
  assert.equal(statusBucket({ ...fresh, speedMph: null, status: "On Route" }), "Unknown");
});

test("moving GPS with absent heading has no north-pointing arrow", () => {
  const icon = makeVehicleIcon("live", "vendor", "low", "", 40, null, false, "Online", "Online");
  assert.doesNotMatch(icon.html, /border-bottom:13px/, "a directional arrow requires a measured heading");
});

test("operational on-route status alone does not create a moving marker", () => {
  const icon = makeVehicleIcon("live", "vendor", "low", "On Route", null, 90, false, "Online", "Online");
  assert.doesNotMatch(icon.html, /border-bottom:13px/);
});

test("replay unknown speed is a gap, not a zero-speed sample or fabricated peak", () => {
  const html = renderToStaticMarkup(ReplayTrail({ points: [{ speedMph: null }, {}] }));
  assert.doesNotMatch(html, /0 mph|peak 1 mph/);
  assert.match(html, /Speed unavailable/);
  assert.match(html, /peak unavailable/);
});

test("replay measured zero keeps a genuine zero peak", () => {
  const html = renderToStaticMarkup(ReplayTrail({ points: [{ speedMph: 0 }] }));
  assert.match(html, /title="0 mph"/);
  assert.match(html, /peak 0 mph/);
});

test("heading label already distinguishes absent heading from north", () => {
  assert.equal(headingLabel(null), "—");
  assert.equal(headingLabel(0), "N");
});

test("snapshot and SSE normalization preserve null to zero to null transitions", () => {
  for (const value of [null, 0, null]) {
    const position = toPosition({ ...fresh, speedMph: value, heading: value, eventTime: "2026-09-02T10:00:00Z" });
    assert.equal(position.speedMph, value);
    assert.equal(position.heading, value);
    assert.equal(position.lat, fresh.lat);
    assert.equal(position.lng, fresh.lng);
    assert.equal(position.eventTime, "2026-09-02T10:00:00Z");
  }
  const source = readFileSync(new URL("../src/hooks/useLiveTelemetry.ts", import.meta.url), "utf8");
  assert.equal((source.match(/setPositions\(rows\.map\(toPosition\)\)/g) ?? []).length, 2,
    "snapshot and SSE both use the tested normalizer; this is not a live transport test");
});

test("canonical null does not revive an older numeric alias", () => {
  assert.equal(toPosition({ ...fresh, speedMph: null, speed_mph: 42 }).speedMph, null);
  assert.equal(toPosition({ ...fresh, speed_mph: 0 }).speedMph, 0);
  assert.equal(statusBucket({ ...fresh, speedMph: null, speed_mph: 42 }), "Unknown");
});

test("partial measurement combinations remain independent", () => {
  const speedOnly = toPosition({ ...fresh, speedMph: 40 });
  const headingOnly = toPosition({ ...fresh, heading: 90 });
  assert.equal(speedOnly.speedMph, 40);
  assert.equal(speedOnly.heading, null);
  assert.equal(headingOnly.speedMph, null);
  assert.equal(headingOnly.heading, 90);
});

test("malformed or blank values remain unknown while numeric zero survives", () => {
  for (const missing of [null, undefined, "", "  ", false, true, [], {}, Number.NaN, Infinity, -1]) {
    assert.equal(measurements.optionalTelemetrySpeed(missing), null);
    assert.equal(measurements.optionalTelemetryHeading(missing), null);
  }
  assert.equal(measurements.optionalTelemetrySpeed("0"), 0);
  assert.equal(measurements.optionalTelemetryHeading("0"), 0);
  assert.equal(measurements.optionalTelemetryHeading(360), 0);
  assert.equal(measurements.optionalTelemetryHeading(361), null);
});

test("unknown motion does not enter moving or idle filter counts", () => {
  const buckets = [null, 0, 40].map((speedMph) => statusBucket({ ...fresh, speedMph }));
  assert.deepEqual(buckets, ["Unknown", "Idle", "Moving"]);
  assert.equal(statusBucket({ ...fresh, speedMph: null, isStale: true }), "Offline");
});

test("known moving north keeps its measured arrow; stale fixes do not", () => {
  const north = makeVehicleIcon("live", "vendor", "low", "", 40, 0, false, "Online", "Online");
  assert.match(north.html, /border-bottom:13px/);
  assert.match(north.html, /rotate\(0deg\)/);
  const stale = makeVehicleIcon("stale", "vendor", "low", "", 40, 0, false, "Online", "Online");
  assert.doesNotMatch(stale.html, /border-bottom:13px/);
});

test("mixed replay excludes gaps from measured peak and reports coverage", () => {
  const html = renderToStaticMarkup(ReplayTrail({ points: [{ speedMph: 40 }, { speedMph: null }, { speedMph: 0 }] }));
  assert.match(html, /title="40 mph"/);
  assert.match(html, /title="0 mph"/);
  assert.match(html, /Speed unavailable/);
  assert.match(html, /peak 40 mph/);
  assert.match(html, /2\/3 measured speeds/);
});

test("speed formatting and summaries retain unknown versus measured zero", () => {
  assert.equal(measurements.formatTelemetrySpeed(null, "km/h"), "Speed unavailable");
  assert.equal(measurements.formatTelemetrySpeed(0, "km/h"), "0 km/h");
  assert.deepEqual(measurements.telemetrySpeedSummary([null, 0, 40]), { peak: 40, knownCount: 2, missingCount: 1 });
  assert.deepEqual(measurements.telemetrySpeedSummary([null]), { peak: null, knownCount: 0, missingCount: 1 });
});

test("actual vehicle drawer renders absent instruments without zero or north", () => {
  const { VehicleDrawer } = loadDeclarations("../src/pages/VehiclesPage.tsx", [
    "g", "num", "riskTier", "vehicleDeviceStatus", "vehicleCameraStatus", "hasRecentHeartbeat", "freshness",
    "StatusPill", "RiskChip", "VehicleDrawer", "Instrument", "headingLabel", "ReplayTrail", "DrawerSection", "DrawerTable", "EmptyLine", "fmt", "SlaChip",
  ], { ...icons });
  const drawer = (speedMph, heading) => renderToStaticMarkup(VehicleDrawer({
    record: { id: 1, vehicleCode: "Synthetic partial GPS", lat: 34.05, lng: -118.24,
      lastSeenAt: new Date().toISOString(), speedMph, heading }, detail: {}, loading: false,
  }));
  const instrument = (html, label) => html.match(new RegExp(`>${label}</div><div[^>]*>([\\s\\S]*?)</div>`))?.[1];
  const unknown = drawer(null, null);
  assert.equal(instrument(unknown, "Speed"), "—");
  assert.equal(instrument(unknown, "Heading"), "—");
  assert.match(unknown, /34\.0500, -118\.2400/);
  const zero = drawer(0, 0);
  assert.match(instrument(zero, "Speed"), /^0<span[^>]*>mph<\/span>$/);
  assert.equal(instrument(zero, "Heading"), "N");
});

test("actual fleet tracking card preserves null and zero speed", () => {
  const { TrackingCard } = loadDeclarations("../src/pages/FleetWorkspacePage.tsx", [
    "num", "fmt0", "fmt1", "trackingProvenance", "TrackingCard", "BoardCard", "CardHead", "CardMeta", "CardChip", "statusTone",
  ], { ...icons });
  const card = (speedKph) => renderToStaticMarkup(TrackingCard({ point: {
    shipmentNumber: "Synthetic", vehicleNumber: "Test", locationLabel: "Test", geofenceName: "",
    status: "In Transit", speedKph, alertType: "", isLive: true, freshnessSeconds: 10,
  }, canOpen: false }));
  assert.match(card(null), /Speed unavailable/);
  assert.doesNotMatch(card(null), /0 km\/h/);
  assert.match(card(0), /0 km\/h/);
});

test("vehicle table retains a separate fresh GPS but unavailable-speed path", () => {
  const source = readFileSync(new URL("../src/pages/VehiclesPage.tsx", import.meta.url), "utf8");
  assert.match(source, /const speed = readSpeedMph\(row\)/);
  assert.match(source, /fresh && speed != null[\s\S]*Speed unavailable[\s\S]*No GPS/);
});
