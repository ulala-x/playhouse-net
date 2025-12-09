# PlayHouse-NET Test Suite

Comprehensive test suite for the PlayHouse-NET framework following the testing strategy defined in `doc/specs/10-testing-spec.md`.

## Test Structure

### Test Projects
- `PlayHouse.Tests.Unit` - Unit tests for isolated components
- `PlayHouse.Tests.Integration` - Integration tests for component interactions
- `PlayHouse.Tests.E2E` - End-to-end scenario tests

### Test Philosophy
- **Integration Test First (70%)** - Focus on API usage patterns and real scenarios
- **Unit Test (20%)** - Edge cases, boundary conditions, timing issues
- **E2E Test (10%)** - Full system validation

### Test Guidelines
- **Given-When-Then** structure for all tests
- **Fake objects** preferred over mocks
- **State verification** over implementation verification
- **Korean DisplayName** with clear action and result descriptions

## Implemented Tests

### Unit Tests (PlayHouse.Tests.Unit)

#### Core/Stage Tests
- **AtomicBooleanTests.cs** - Atomic compare-and-swap operations, concurrency safety
  - Basic operations: initialization, Set, CompareAndSet
  - Concurrency: multi-threaded CompareAndSet, concurrent Set/CompareAndSet
  - Theory-based: various state combinations

#### Core/Timer Tests
- **TimerIdGeneratorTests.cs** - Unique ID generation, thread safety
  - Sequential ID generation
  - Concurrency: parallel generation, mixed operations
  - Large-scale: 10,000+ ID generation

#### Core/Session Tests
- **SessionIdGeneratorTests.cs** - Session ID uniqueness and performance
  - Sequential generation
  - High concurrency stress tests (100 threads)
  - Performance validation

### Integration Tests (PlayHouse.Tests.Integration)

#### TestHelpers
- **FakeStage.cs** - Test double for IStage with call tracking
- **FakeActor.cs** - Test double for IActor with lifecycle tracking
- **FakeStageSender.cs** - Test double for IStageSender with operation recording
- **FakeActorSender.cs** - Test double for IActorSender with message tracking

#### Core Tests
- **StageLifecycleTests.cs** - Stage creation, actor join/leave, lifecycle validation
  - Basic Operations: OnCreate, OnPostCreate, OnJoinRoom, OnLeaveRoom
  - Response Validation: reply packets, error codes
  - Input Validation: multiple actors, sequential joins
  - Edge Cases: connection changes, reconnection
  - Usage Examples: complete lifecycle scenarios

- **ActorLifecycleTests.cs** - Actor creation, authentication, destruction
  - Basic Operations: OnCreate, OnDestroy, OnAuthenticate
  - Response Validation: IsConnected state, sender identifiers
  - Input Validation: callbacks, null handling
  - Edge Cases: Reset, DisposeAsync
  - Usage Examples: full lifecycle, disconnect/reconnect, multi-actor management

- **MessageDispatchTests.cs** - Message routing and patterns
  - Basic Operations: OnDispatch, SendAsync, Reply
  - Response Validation: Request vs Fire-and-Forget messages
  - Input Validation: multiple actors, same actor multiple messages
  - Edge Cases: callbacks, broadcast, filtered broadcast
  - Usage Examples: Request-Reply, Fire-and-Forget, Chat broadcast, Filtered broadcast

- **TimerSystemTests.cs** - Timer registration and lifecycle
  - Basic Operations: AddRepeatTimer, AddCountTimer, CancelTimer, HasTimer
  - Response Validation: unique timer IDs, callback storage
  - Input Validation: various intervals and counts
  - Edge Cases: multiple cancellations, Reset
  - Usage Examples: Game tick, Countdown, Buff duration, Multi-timer management

## Running Tests

### All Tests
```bash
dotnet test
```

### Specific Test Project
```bash
dotnet test tests/PlayHouse.Tests.Unit
dotnet test tests/PlayHouse.Tests.Integration
dotnet test tests/PlayHouse.Tests.E2E
```

### Specific Test Category
```bash
dotnet test --filter "DisplayName~Stage"
dotnet test --filter "DisplayName~Actor"
dotnet test --filter "DisplayName~Timer"
```

### With Coverage
```bash
dotnet test --collect:"XPlat Code Coverage"
```

## Test Coverage Goals

| Area | Target | Current |
|------|--------|---------|
| Overall | 80% | TBD |
| Core Engine (Stage/Actor) | 90% | TBD |
| HTTP API | 85% | TBD |
| Timer System | 90% | TBD |

## Adding New Tests

1. **Choose appropriate test level**
   - Integration: API usage, component interactions
   - Unit: Edge cases, boundary conditions
   - E2E: Full scenario validation

2. **Follow naming convention**
   - Method: `{Target}_{Condition}_{ExpectedResult}`
   - DisplayName: "{Action} {Result}" in Korean

3. **Use Given-When-Then structure**
   ```csharp
   // Given (전제조건)
   var setup = ...;

   // When (행동)
   var result = ...;

   // Then (결과)
   result.Should().Be(expected);
   ```

4. **Prefer Fake objects**
   - Use TestHelpers fakes for domain objects
   - Only mock external dependencies

5. **Verify state, not implementation**
   - Check final state and outputs
   - Avoid verifying method calls

## Test Best Practices

### Do ✅
- Use FluentAssertions for readable assertions
- Write descriptive DisplayNames in Korean
- Create meaningful test data with clear intent
- Test edge cases and boundary conditions
- Keep tests independent and isolated

### Don't ❌
- Use magic numbers without explanation
- Create test interdependencies
- Sleep/delay in tests (use events/callbacks)
- Test implementation details
- Mix multiple concerns in one test

## Dependencies

- **xUnit** 2.9.x - Test framework
- **FluentAssertions** 6.12.x - Fluent assertion library
- **NSubstitute** 5.1.x - Mocking framework
- **Microsoft.NET.Test.Sdk** 17.x - Test SDK
- **coverlet.collector** 6.0.x - Code coverage

## References

- [10-testing-spec.md](../doc/specs/10-testing-spec.md) - Detailed testing specification
- [architecture-guide.md](../doc/architecture-guide.md) - Architecture principles
- [03-stage-actor-model.md](../doc/specs/03-stage-actor-model.md) - Stage/Actor model details
