#include "../base_integration_test.hpp"
#include <gtest/gtest.h>

using namespace playhouse;
using namespace playhouse::test;

/// C-01: Stage Creation Tests
/// Verifies that test stages can be created via the HTTP API
class C01_StageCreationTest : public BaseIntegrationTest {
protected:
    // This test doesn't need connector initialization
    void SetUp() override {
        // Skip connector initialization for HTTP-only tests
    }

    void TearDown() override {
        // No cleanup needed
    }
};

TEST_F(C01_StageCreationTest, CreateStage_WithTestStageType_ReturnsValidStageInfo) {
    // When: Create a TestStage type stage
    auto stage_info = GetTestServer().CreateTestStage();

    // Then: Stage information should be valid
    EXPECT_TRUE(stage_info.success) << "Stage creation should succeed";
    EXPECT_GT(stage_info.stage_id, 0) << "Stage ID should be positive";
    EXPECT_EQ(stage_info.stage_type, "TestStage") << "Stage type should match requested type";
    EXPECT_FALSE(stage_info.reply_payload_id.empty()) << "Reply payload should not be empty";
}

TEST_F(C01_StageCreationTest, CreateStage_WithCustomPayload_ReturnsValidStageInfo) {
    // When: Create a stage with maximum players specified
    auto stage_info = GetTestServer().CreateStage("TestStage", 10);

    // Then: Stage should be created successfully
    EXPECT_TRUE(stage_info.success);
    EXPECT_GT(stage_info.stage_id, 0);
    EXPECT_EQ(stage_info.stage_type, "TestStage");
}

TEST_F(C01_StageCreationTest, CreateStage_MultipleTimes_ReturnsUniqueStageIds) {
    // When: Create 3 stages
    auto stage1 = GetTestServer().CreateTestStage();
    auto stage2 = GetTestServer().CreateTestStage();
    auto stage3 = GetTestServer().CreateTestStage();

    // Then: Each stage should have a unique ID
    EXPECT_NE(stage1.stage_id, stage2.stage_id) << "First and second stage IDs should differ";
    EXPECT_NE(stage2.stage_id, stage3.stage_id) << "Second and third stage IDs should differ";
    EXPECT_NE(stage1.stage_id, stage3.stage_id) << "First and third stage IDs should differ";

    // All stages should have the same type
    EXPECT_EQ(stage1.stage_type, "TestStage");
    EXPECT_EQ(stage2.stage_type, "TestStage");
    EXPECT_EQ(stage3.stage_type, "TestStage");
}
