import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';

export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    allowedHosts: ['host.docker.internal'],
    host: true,
    proxy: {
      '/tasks': {
        target: process.env.API_HTTPS || process.env.API_HTTP,
        changeOrigin: true
      },
      '/health': {
        target: process.env.API_HTTPS || process.env.API_HTTP,
        changeOrigin: true
      }
    }
  }
});
