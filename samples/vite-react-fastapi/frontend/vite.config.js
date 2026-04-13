import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig({
  plugins: [react()],
  server: {
    allowedHosts: ['host.docker.internal'],
    host: true,
    proxy: {
      '/api': {
        target: process.env.API_HTTPS || process.env.API_HTTP,
        changeOrigin: true,
        rewrite: (path) => path.replace(/^\/api/, '')
      }
    }
  }
});
