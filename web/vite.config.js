import { fileURLToPath } from 'node:url';
import { defineConfig } from 'vite';

// 构建产物输出到 ASP.NET Core 的 wwwroot，由后端直接托管
const outDir = fileURLToPath(new URL('../src/wwwroot', import.meta.url));

export default defineConfig({
  base: '/',
  server: {
    port: 5173,
    proxy: {
      '/api': 'http://localhost:8080',
    },
  },
  build: {
    outDir,
    emptyOutDir: true,
  },
});
