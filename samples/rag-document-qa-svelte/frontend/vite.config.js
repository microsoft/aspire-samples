import { defineConfig } from 'vite'
import { svelte } from '@sveltejs/vite-plugin-svelte'

export default defineConfig({
  plugins: [svelte()],
  server: {
    host: true,
    proxy: {
      '/upload': {
        target: process.env.API_HTTP,
        changeOrigin: true
      },
      '/ask': {
        target: process.env.API_HTTP,
        changeOrigin: true
      },
      '/documents': {
        target: process.env.API_HTTP,
        changeOrigin: true
      }
    }
  }
})
