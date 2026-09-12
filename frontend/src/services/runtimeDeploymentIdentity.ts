const exactGitSha = /^[0-9a-f]{40}$/i;
const dotnetInformationalVersionWithSha = /^\d+\.\d+\.\d+(?:-[0-9a-z.-]+)?\+([0-9a-f]{40})$/i;

/**
 * Resolve the exact commit carried by either a deployment provider's raw SHA or
 * .NET's standard assembly informational version (`1.0.0+<sha>`). Any other
 * label stays unverifiable so the runtime gate continues to fail closed.
 */
export function exactDeploymentSha(value: unknown): string {
  const candidate = String(value ?? "").trim().toLowerCase();
  if (exactGitSha.test(candidate)) return candidate;
  return dotnetInformationalVersionWithSha.exec(candidate)?.[1] ?? "";
}

