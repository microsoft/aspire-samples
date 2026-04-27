import { defineConfig } from 'vite'

export default defineConfig({
  server: {
    allowedHosts: ['host.docker.internal']
  }
})