import { readdir, readFile } from "node:fs/promises";
import { gzipSync } from "node:zlib";

const assetDirectory = new URL("../dist/assets/", import.meta.url);
const rawLimitBytes = 400 * 1024;
const gzipLimitBytes = 125 * 1024;

const javascriptAssets = (await readdir(assetDirectory))
  .filter((name) => name.endsWith(".js"));

if (javascriptAssets.length === 0) {
  throw new Error("Bundle budget could not be verified: dist/assets contains no JavaScript chunks.");
}

const measurements = await Promise.all(javascriptAssets.map(async (name) => {
  const contents = await readFile(new URL(name, assetDirectory));
  return {
    name,
    rawBytes: contents.byteLength,
    gzipBytes: gzipSync(contents).byteLength,
  };
}));

const violations = measurements.filter(({ rawBytes, gzipBytes }) =>
  rawBytes > rawLimitBytes || gzipBytes > gzipLimitBytes);
const largestRaw = measurements.reduce((largest, current) =>
  current.rawBytes > largest.rawBytes ? current : largest);
const largestGzip = measurements.reduce((largest, current) =>
  current.gzipBytes > largest.gzipBytes ? current : largest);
const kibibytes = (bytes) => `${(bytes / 1024).toFixed(2)} KiB`;

console.log(
  `Bundle budget: ${javascriptAssets.length} chunks; `
  + `largest raw ${largestRaw.name} ${kibibytes(largestRaw.rawBytes)}; `
  + `largest gzip ${largestGzip.name} ${kibibytes(largestGzip.gzipBytes)}.`,
);

if (violations.length > 0) {
  const details = violations
    .map(({ name, rawBytes, gzipBytes }) =>
      `- ${name}: ${kibibytes(rawBytes)} raw, ${kibibytes(gzipBytes)} gzip`)
    .join("\n");
  throw new Error(
    `JavaScript bundle budget exceeded (limits: ${kibibytes(rawLimitBytes)} raw, `
    + `${kibibytes(gzipLimitBytes)} gzip):\n${details}`,
  );
}
