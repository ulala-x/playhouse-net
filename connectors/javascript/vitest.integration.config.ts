import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    name: 'integration',
    include: ['tests/integration/**/*.test.ts'],
    setupFiles: ['tests/integration/setup.ts'],
    testTimeout: 30000,
    hookTimeout: 30000,
    teardownTimeout: 10000,
    globals: false,
    environment: 'node',
    reporters: ['verbose'],
    coverage: {
      enabled: false
    },
    sequence: {
      shuffle: false
    }
  },
  resolve: {
    alias: {
      '@': '/src'
    }
  }
});
