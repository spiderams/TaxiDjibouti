import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";

function getAspireServiceEndpoint(serviceName: string) {
  const servicePrefix = `services__${serviceName}__`;
  const endpointKey = Object.keys(process.env).find(
    (key) =>
      key.startsWith(servicePrefix) &&
      (key.includes("__https__") || key.includes("__http__")),
  );

  return endpointKey ? process.env[endpointKey] : undefined;
}

const apiProxyTarget =
  process.env.VITE_API_PROXY_TARGET ??
  getAspireServiceEndpoint("api") ??
  "https://localhost:7129";

const port = Number(process.env.PORT ?? 5173);
console.log(`Vite dev server will run on port ${port}`);
export default defineConfig({
  plugins: [react()],
  server: {
    host: "0.0.0.0",
    port,
    strictPort: Boolean(process.env.PORT),
    proxy: {
      "/api": {
        target: apiProxyTarget,
        changeOrigin: true,
        secure: false,
      },
      "/hubs": {
        target: apiProxyTarget,
        changeOrigin: true,
        secure: false,
        ws: true,
      },
    },
  },
});
