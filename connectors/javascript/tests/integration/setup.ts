/**
 * Global test setup for integration tests
 */

import { beforeAll, afterAll } from 'vitest';
import { TestServerClient } from './helpers/TestServerClient.js';

const testServer = new TestServerClient();

/**
 * Check if test server is running before starting tests
 */
beforeAll(async () => {
    console.log('Checking test server health...');
    console.log(`  Host: ${testServer.host}`);
    console.log(`  HTTP Port: ${testServer.httpPort}`);
    console.log(`  WS Port: ${testServer.wsPort}`);

    const isHealthy = await testServer.checkHealth();

    if (!isHealthy) {
        throw new Error(
            'Test server is not responding. Please start the test server using docker-compose:\n' +
            '  cd connectors/javascript\n' +
            '  docker-compose -f docker-compose.test.yml up -d\n' +
            'Or run the test script:\n' +
            '  ./run-tests.sh'
        );
    }

    console.log('Test server is healthy!');
});

/**
 * Cleanup after all tests
 */
afterAll(async () => {
    console.log('Integration tests completed');
});
