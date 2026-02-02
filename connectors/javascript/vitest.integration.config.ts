import { defineConfig } from 'vitest/config';

export default defineConfig({
  test: {
    name: 'integration',
    include: ['tests/integration/**/*.test.ts'],
    setupFiles: ['tests/integration/setup.ts'],
    testTimeout: 60000,
    hookTimeout: 60000,
    teardownTimeout: 10000,
    globals: false,
    environment: 'node',
    reporters: ['verbose'],
    minThreads: 1,
    maxThreads: 1,
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
