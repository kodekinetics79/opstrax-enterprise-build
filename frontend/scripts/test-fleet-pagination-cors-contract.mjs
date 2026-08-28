import assert from "node:assert/strict";
import { readFileSync } from "node:fs";

const apiSource = readFileSync(new URL("../src/services/fleetDomainApi.ts", import.meta.url), "utf8");
const serverSource = readFileSync(new URL("../../backend-dotnet/Program.cs", import.meta.url), "utf8");

assert.match(
  apiSource,
  /response\.headers\?\.\["x-total-count"\]/,
  "paged fleet clients must read the authoritative server total",
);
assert.match(
  serverSource,
  /WithExposedHeaders\([\s\S]*"X-Total-Count"[\s\S]*\);/,
  "CORS must expose X-Total-Count or cross-origin pagination collapses to one page",
);

console.log("Fleet pagination CORS contract passed.");
