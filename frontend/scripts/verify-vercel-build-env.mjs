const apiBaseUrl =
  process.env.VITE_API_BASE_URL ??
  process.env.VITE_PLATFORM_API_BASE_URL ??
  process.env.VITE_DOTNET_API_URL;

if (!apiBaseUrl) {
  console.error(
    "VITE_API_BASE_URL (or VITE_PLATFORM_API_BASE_URL / VITE_DOTNET_API_URL) is required for the Vercel production build",
  );
  process.exit(1);
}
