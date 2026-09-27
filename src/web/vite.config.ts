import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'

// Dev: Vite on :5173 proxies /api to the .NET app on :5073.
// Build: output goes into the API's wwwroot so one process serves everything.
export default defineConfig({
  plugins: [react()],
  server: {
    proxy: { '/api': 'http://localhost:5073' },
  },
  build: {
    outDir: '../BuildingManager.Api/wwwroot',
    emptyOutDir: true,
  },
})
