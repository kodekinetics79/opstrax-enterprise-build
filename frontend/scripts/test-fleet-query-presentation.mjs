import assert from "node:assert/strict";
import {
  fleetQueryFingerprint,
  resolveFleetQueryPresentation,
} from "../src/utils/fleetQueryPresentation.ts";

const page20 = fleetQueryFingerprint({
  page: 20, pageSize: 50, search: "", status: "All", sort: "vehicle", order: "asc",
});
const searchedPage1 = fleetQueryFingerprint({
  page: 1, pageSize: 50, search: "WESTHUB", status: "All", sort: "vehicle", order: "asc",
});

assert.equal(resolveFleetQueryPresentation({
  rawSearch: "WESTHUB", appliedSearch: "", requestFingerprint: page20,
  responseFingerprint: page20, hasData: true, isFetching: false,
}), "settling", "page-20 rows must disappear on the first raw search render");

assert.equal(resolveFleetQueryPresentation({
  rawSearch: "WESTHUB", appliedSearch: "WESTHUB", requestFingerprint: searchedPage1,
  responseFingerprint: undefined, hasData: false, isFetching: true,
}), "loading", "the debounced page-1 request has an honest loading state");

assert.equal(resolveFleetQueryPresentation({
  rawSearch: "WESTHUB", appliedSearch: "WESTHUB", requestFingerprint: searchedPage1,
  responseFingerprint: searchedPage1, hasData: true, isFetching: false,
}), "rows", "rows become visible only after the matching search resolves");

const offlineSort = fleetQueryFingerprint({
  page: 1, pageSize: 50, search: "WESTHUB", status: "Offline", sort: "status", order: "desc",
});
assert.equal(resolveFleetQueryPresentation({
  rawSearch: "WESTHUB", appliedSearch: "WESTHUB", requestFingerprint: offlineSort,
  responseFingerprint: searchedPage1, hasData: true, isFetching: true,
}), "settling", "a response from the prior filter/sort identity cannot render");

console.log("Fleet query presentation behavior contract passed.");
