import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { dirname, resolve } from "node:path";
import test from "node:test";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const require = createRequire(import.meta.url);
const esbuild = require("esbuild");
const built = await esbuild.build({
  stdin: {
    contents: 'export { AuthProvider } from "@/hooks/useAuth";',
    loader: "ts",
    resolveDir: root,
  },
  bundle: true,
  platform: "node",
  format: "cjs",
  write: false,
  logLevel: "silent",
  alias: { "@": resolve(root, "src") },
  external: ["react", "react/jsx-runtime", "react-dom", "@tanstack/react-query"],
  plugins: [{
    name: "controlled-auth-api",
    setup(build) {
      build.onResolve({ filter: /services\/authApi$/ }, () => ({ path: "auth-test-api", external: true }));
    },
  }],
});

function deferred() {
  let resolvePromise;
  let rejectPromise;
  const promise = new Promise((resolve, reject) => {
    resolvePromise = resolve;
    rejectPromise = reject;
  });
  return { promise, resolve: resolvePromise, reject: rejectPromise };
}

async function settle() {
  for (let index = 0; index < 8; index += 1) await Promise.resolve();
}

function fixture(meResults) {
  const effects = [];
  const refs = [];
  const states = [];
  let refIndex = 0;
  let stateIndex = 0;
  let meCalls = 0;
  const storage = new Map();
  const stored = {
    session: {
      token: "stored-token",
      csrfToken: "stored-csrf",
      role: "Fleet Manager",
      permissions: ["dashboard:view"],
      user: { id: 12, email: "review@example.test", name: "Review User" },
      company: { id: 8, code: "REVIEW", name: "Review Tenant" },
    },
    expiresAt: Date.now() + 60_000,
  };
  storage.set("opstrax.session.v3", JSON.stringify(stored));
  const localStorage = {
    getItem: (key) => storage.get(key) ?? null,
    setItem: (key, value) => storage.set(key, value),
    removeItem: (key) => storage.delete(key),
  };

  const react = {
    createContext: () => ({}),
    useContext: () => null,
    useEffect: (effect) => { effects.push(effect); },
    useMemo: (factory) => factory(),
    useRef: (initial) => {
      const index = refIndex++;
      if (!refs[index]) refs[index] = { current: initial };
      return refs[index];
    },
    useState: (initial) => {
      const index = stateIndex++;
      if (states.length <= index) states[index] = typeof initial === "function" ? initial() : initial;
      return [states[index], (next) => {
        states[index] = typeof next === "function" ? next(states[index]) : next;
      }];
    },
  };
  const authApi = {
    me: () => {
      const result = meResults[meCalls];
      meCalls += 1;
      if (!result) throw new Error("Unexpected authApi.me call");
      return result.promise;
    },
  };
  const customRequire = (name) => {
    if (name === "react") return react;
    if (name === "react/jsx-runtime") return { jsx: (type, props) => ({ type, props }), jsxs: (type, props) => ({ type, props }) };
    if (name === "react-dom") return { flushSync: (action) => action() };
    if (name === "@tanstack/react-query") return { useQueryClient: () => ({ clear() {} }) };
    if (name === "auth-test-api") return { authApi };
    return require(name);
  };

  const module = { exports: {} };
  new Function("module", "exports", "require", "localStorage", built.outputFiles[0].text)(
    module,
    module.exports,
    customRequire,
    localStorage,
  );
  module.exports.AuthProvider({ children: { type: "main", props: {} } });
  return {
    effects,
    states,
    stored,
    meCalls: () => meCalls,
  };
}

test("stored-session revalidation settles after React StrictMode effect replay", async () => {
  const first = deferred();
  const replay = deferred();
  const view = fixture([first, replay]);

  assert.equal(view.states[1], true, "the shell starts gated while the stored session is revalidated");
  const cleanupFirstSetup = view.effects[0]();
  cleanupFirstSetup();
  view.effects[0]();
  assert.equal(view.meCalls(), 2, "StrictMode's replay must start a live replacement request");

  first.resolve({ ...view.stored.session, permissions: ["stale:first-call"] });
  await settle();
  assert.equal(view.states[1], true, "the cancelled first request cannot release the authorization gate");

  const current = { ...view.stored.session, role: "Company Admin", permissions: ["*"] };
  replay.resolve(current);
  await settle();
  assert.equal(view.states[0], current, "the replay request installs the server-current session");
  assert.equal(view.states[1], false, "the shell is released after the replay request completes");
});

test("ordinary mount still performs exactly one server permission revalidation", async () => {
  const request = deferred();
  const view = fixture([request]);
  view.effects[0]();
  assert.equal(view.meCalls(), 1);
  request.resolve({ ...view.stored.session, permissions: ["dashboard:view", "fleet:view"] });
  await settle();
  assert.equal(view.states[1], false);
});
