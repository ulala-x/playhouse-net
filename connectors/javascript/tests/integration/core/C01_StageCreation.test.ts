/**
 * C-01: Stage Creation Tests
 *
 * Tests for creating stages on the test server via HTTP API.
 */

import { describe, test, expect } from 'vitest';
import { TestServerClient } from '../helpers/TestServerClient.js';

describe('C-01: Stage Creation', () => {
    const testServer = new TestServerClient();

    test('C-01-01: Can create a stage with TestStage type', async () => {
        // When: Create a stage with TestStage type
        const stageInfo = await testServer.createTestStage();

        // Then: Stage information should be returned correctly
        expect(stageInfo).toBeDefined();
        expect(stageInfo.success).toBe(true);
        expect(stageInfo.stageId).toBeGreaterThan(0);
        expect(stageInfo.stageType).toBe('TestStage');
        expect(stageInfo.replyPayloadId).toBeDefined();
    });

    test('C-01-02: Can create a stage with custom payload', async () => {
        // When: Create a stage with max players specified
        const stageInfo = await testServer.createStage('TestStage', 10);

        // Then: Stage should be created successfully
        expect(stageInfo).toBeDefined();
        expect(stageInfo.stageId).toBeGreaterThan(0);
        expect(stageInfo.stageType).toBe('TestStage');
    });

    test('C-01-03: Can create multiple stages with unique IDs', async () => {
        // When: Create 3 stages
        const stage1 = await testServer.createTestStage();
        const stage2 = await testServer.createTestStage();
        const stage3 = await testServer.createTestStage();

        // Then: Each stage should have a unique ID
        expect(stage1.stageId).not.toBe(stage2.stageId);
        expect(stage2.stageId).not.toBe(stage3.stageId);
        expect(stage1.stageId).not.toBe(stage3.stageId);

        // All stages should have the same type
        expect(stage1.stageType).toBe('TestStage');
        expect(stage2.stageType).toBe('TestStage');
        expect(stage3.stageType).toBe('TestStage');
    });
});
