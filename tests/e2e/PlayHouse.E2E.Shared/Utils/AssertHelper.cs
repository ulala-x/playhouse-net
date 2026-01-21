#nullable enable

namespace PlayHouse.E2E.Shared.Utils;

/// <summary>
/// E2E 검증 프로그램을 위한 간단한 Assert 헬퍼.
/// 실패 시 예외를 발생시킵니다.
/// </summary>
public class AssertHelper
{
    /// <summary>
    /// 두 값이 같은지 검증합니다.
    /// </summary>
    public void Equals<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            var errorMessage = message ?? $"Expected: {expected}, Actual: {actual}";
            throw new AssertionFailedException(errorMessage);
        }
    }

    /// <summary>
    /// 두 값이 다른지 검증합니다.
    /// </summary>
    public void NotEquals<T>(T notExpected, T actual, string? message = null)
    {
        if (EqualityComparer<T>.Default.Equals(notExpected, actual))
        {
            var errorMessage = message ?? $"Not Expected: {notExpected}, Actual: {actual}";
            throw new AssertionFailedException(errorMessage);
        }
    }

    /// <summary>
    /// 조건이 참인지 검증합니다.
    /// </summary>
    public void IsTrue(bool condition, string? message = null)
    {
        if (!condition)
        {
            var errorMessage = message ?? "Expected: true, Actual: false";
            throw new AssertionFailedException(errorMessage);
        }
    }

    /// <summary>
    /// 조건이 거짓인지 검증합니다.
    /// </summary>
    public void IsFalse(bool condition, string? message = null)
    {
        if (condition)
        {
            var errorMessage = message ?? "Expected: false, Actual: true";
            throw new AssertionFailedException(errorMessage);
        }
    }

    /// <summary>
    /// 값이 null이 아닌지 검증합니다.
    /// </summary>
    public void NotNull<T>(T? value, string? message = null)
    {
        if (value == null)
        {
            var errorMessage = message ?? "Expected: not null, Actual: null";
            throw new AssertionFailedException(errorMessage);
        }
    }

    /// <summary>
    /// 문자열이 null이거나 비어있지 않은지 검증합니다.
    /// </summary>
    public void NotNullOrEmpty(string? value, string? message = null)
    {
        if (string.IsNullOrEmpty(value))
        {
            var errorMessage = message ?? "Expected: not null or empty, Actual: " + (value == null ? "null" : "empty");
            throw new AssertionFailedException(errorMessage);
        }
    }

    /// <summary>
    /// 두 값이 같은지 검증합니다. (Equals와 동일, 호환성을 위해 제공)
    /// </summary>
    public void AreEqual<T>(T expected, T actual, string? message = null)
    {
        Equals(expected, actual, message);
    }

    /// <summary>
    /// 값이 null인지 검증합니다.
    /// </summary>
    public void IsNull<T>(T? value, string? message = null)
    {
        if (value != null)
        {
            var errorMessage = message ?? $"Expected: null, Actual: {value}";
            throw new AssertionFailedException(errorMessage);
        }
    }

    /// <summary>
    /// 컬렉션에 특정 값이 포함되어 있는지 검증합니다.
    /// </summary>
    public void Contains<T>(IEnumerable<T> collection, T item, string? message = null)
    {
        if (!collection.Contains(item))
        {
            var errorMessage = message ?? $"Collection does not contain: {item}";
            throw new AssertionFailedException(errorMessage);
        }
    }

    /// <summary>
    /// 컬렉션이 비어있지 않은지 검증합니다.
    /// </summary>
    public void NotEmpty<T>(IEnumerable<T> collection, string? message = null)
    {
        if (!collection.Any())
        {
            var errorMessage = message ?? "Expected: not empty, Actual: empty";
            throw new AssertionFailedException(errorMessage);
        }
    }

    /// <summary>
    /// 컬렉션이 비어있는지 검증합니다.
    /// </summary>
    public void Empty<T>(IEnumerable<T> collection, string? message = null)
    {
        if (collection.Any())
        {
            var errorMessage = message ?? $"Expected: empty, Actual: {collection.Count()} items";
            throw new AssertionFailedException(errorMessage);
        }
    }

    /// <summary>
    /// 값이 특정 값보다 큰지 검증합니다.
    /// </summary>
    public void GreaterThan<T>(T value, T threshold, string? message = null) where T : IComparable<T>
    {
        if (value.CompareTo(threshold) <= 0)
        {
            var errorMessage = message ?? $"Expected: > {threshold}, Actual: {value}";
            throw new AssertionFailedException(errorMessage);
        }
    }

    /// <summary>
    /// 값이 특정 값보다 작은지 검증합니다.
    /// </summary>
    public void LessThan<T>(T value, T threshold, string? message = null) where T : IComparable<T>
    {
        if (value.CompareTo(threshold) >= 0)
        {
            var errorMessage = message ?? $"Expected: < {threshold}, Actual: {value}";
            throw new AssertionFailedException(errorMessage);
        }
    }

    /// <summary>
    /// 값이 특정 값보다 크거나 같은지 검증합니다.
    /// </summary>
    public void GreaterThanOrEqual<T>(T value, T threshold, string? message = null) where T : IComparable<T>
    {
        if (value.CompareTo(threshold) < 0)
        {
            var errorMessage = message ?? $"Expected: >= {threshold}, Actual: {value}";
            throw new AssertionFailedException(errorMessage);
        }
    }

    /// <summary>
    /// 값이 특정 값보다 작거나 같은지 검증합니다.
    /// </summary>
    public void LessThanOrEqual<T>(T value, T threshold, string? message = null) where T : IComparable<T>
    {
        if (value.CompareTo(threshold) > 0)
        {
            var errorMessage = message ?? $"Expected: <= {threshold}, Actual: {value}";
            throw new AssertionFailedException(errorMessage);
        }
    }

    /// <summary>
    /// 문자열이 특정 문자열을 포함하는지 검증합니다.
    /// </summary>
    public void StringContains(string text, string substring, string? message = null)
    {
        if (!text.Contains(substring))
        {
            var errorMessage = message ?? $"String does not contain: '{substring}' in '{text}'";
            throw new AssertionFailedException(errorMessage);
        }
    }

    /// <summary>
    /// 문자열이 특정 문자열로 시작하는지 검증합니다.
    /// </summary>
    public void StringStartsWith(string text, string prefix, string? message = null)
    {
        if (!text.StartsWith(prefix))
        {
            var errorMessage = message ?? $"String does not start with: '{prefix}' in '{text}'";
            throw new AssertionFailedException(errorMessage);
        }
    }

    /// <summary>
    /// 문자열이 특정 문자열로 끝나는지 검증합니다.
    /// </summary>
    public void StringEndsWith(string text, string suffix, string? message = null)
    {
        if (!text.EndsWith(suffix))
        {
            var errorMessage = message ?? $"String does not end with: '{suffix}' in '{text}'";
            throw new AssertionFailedException(errorMessage);
        }
    }

    /// <summary>
    /// 예외가 발생하는지 검증합니다.
    /// </summary>
    public void Throws<TException>(Action action, string? message = null) where TException : Exception
    {
        try
        {
            action();
            var errorMessage = message ?? $"Expected exception: {typeof(TException).Name}, but no exception was thrown";
            throw new AssertionFailedException(errorMessage);
        }
        catch (TException)
        {
            // 예상한 예외가 발생했으므로 성공
        }
    }

    /// <summary>
    /// 비동기 예외가 발생하는지 검증합니다.
    /// </summary>
    public async Task ThrowsAsync<TException>(Func<Task> action, string? message = null) where TException : Exception
    {
        try
        {
            await action();
            var errorMessage = message ?? $"Expected exception: {typeof(TException).Name}, but no exception was thrown";
            throw new AssertionFailedException(errorMessage);
        }
        catch (TException)
        {
            // 예상한 예외가 발생했으므로 성공
        }
    }
}

/// <summary>
/// Assertion 실패 시 발생하는 예외.
/// </summary>
public class AssertionFailedException : Exception
{
    public AssertionFailedException(string message) : base(message)
    {
    }
}
