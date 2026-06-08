import { defineConfig } from 'vite'

export default defineConfig({
  server: {
    // Allow the reverse-proxy host names Aspire forwards in run mode so the
    // YARP -> Vite dev-server hand-off isn't rejected by Vite's host check.
    allowedHosts: ['host.docker.internal', '.dev.internal']
  }
})