import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { test } from "node:test";

// This suite requires installed dependencies; keep it outside tools/launch's zero-install glob.
// Express 4/body-parser still constrain qs to the vulnerable 6.15 minor.
// Keep the override until both upstream ranges include the patched release.
// https://github.com/advisories/GHSA-x5fp-wj9c-mxmx
// https://github.com/advisories/GHSA-4mjr-xmp4-gh2g
const packages = new Map([
  ["backend", new URL("../../backend/package.json", import.meta.url)],
  ["services/node-events", new URL("../../services/node-events/package.json", import.meta.url)],
]);
const targets = process.argv.slice(2);
if (targets.length === 0) targets.push(...packages.keys());

for (const target of targets) {
  assert.ok(packages.has(target), `Unknown query-parser test target: ${target}`);
  const require = createRequire(packages.get(target));
  const expressRequire = createRequire(require.resolve("express"));
  const bodyParserRequire = createRequire(expressRequire.resolve("body-parser"));
  const qs = expressRequire("qs");

  test(`${target}: Express and body-parser both resolve the patched parser`, () => {
    for (const consumer of [expressRequire, bodyParserRequire]) {
      assert.equal(consumer("qs/package.json").version, "6.16.0");
      assert.equal(consumer.resolve("qs"), expressRequire.resolve("qs"));
    }
  });

  test(`${target}: ordinary nested queries and arrays retain their shape`, () => {
    const value = { fleet: { name: "Test Fleet" }, vehicle: ["truck", "van"] };
    assert.deepEqual(qs.parse(qs.stringify(value)), value);
    const express = require("express");
    const app = express();
    assert.deepEqual(app.get("query parser fn")("fleet[name]=Test%20Fleet&vehicle[]=truck&vehicle[]=van"), value);
  });

  for (const key of ["items", "items[]"]) {
    test(`${target}: ${key} cannot bypass the comma array limit`, () => {
      const options = { comma: true, arrayLimit: 3, throwOnLimitExceeded: true };
      assert.doesNotThrow(() => qs.parse(`${key}=a,b,c`, options));
      assert.throws(() => qs.parse(`${key}=a,b,c,d`, options), RangeError);
    });
  }

  for (const options of [{ plainObjects: true }, { allowPrototypes: true }]) {
    test(`${target}: untrusted constructor/isBuffer round-trip is safe (${JSON.stringify(options)})`, () => {
      const input = qs.parse("filter[constructor][isBuffer]=not-a-function", options);
      assert.equal(input.filter.constructor.isBuffer, "not-a-function");
      assert.equal(qs.stringify(input), "filter%5Bconstructor%5D%5BisBuffer%5D=not-a-function");
    });
  }

  test(`${target}: extended form parsing remains compatible`, async () => {
    const express = require("express");
    const app = express();
    app.use(express.urlencoded({ extended: true, limit: "1kb" }));
    app.post("/parser-smoke", (req, res) => res.json(req.body));
    const server = app.listen(0, "127.0.0.1");
    try {
      await new Promise((resolve, reject) => {
        server.once("listening", resolve);
        server.once("error", reject);
      });
      const response = await fetch(`http://127.0.0.1:${server.address().port}/parser-smoke`, {
        method: "POST",
        headers: { "content-type": "application/x-www-form-urlencoded" },
        body: "fleet[name]=Test%20Fleet&vehicle[]=truck&vehicle[]=van",
        signal: AbortSignal.timeout(5000),
      });
      assert.equal(response.status, 200);
      assert.deepEqual(await response.json(), { fleet: { name: "Test Fleet" }, vehicle: ["truck", "van"] });
    } finally {
      // Preserve a listener/permission failure instead of masking it during cleanup.
      if (server.listening) {
        server.closeAllConnections();
        await new Promise((resolve, reject) => server.close(error => error ? reject(error) : resolve()));
      }
    }
  });
}
