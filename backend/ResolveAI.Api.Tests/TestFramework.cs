using System.Runtime.CompilerServices;

namespace ResolveAI.Api.Tests;

public static class Assert
{
    public static void True(bool condition, string? message = null, [CallerArgumentExpression("condition")] string? expr = null)
    {
        if (!condition)
            throw new AssertionException(message ?? $"Expected true, but got false for expression: {expr}");
    }

    public static void False(bool condition, string? message = null, [CallerArgumentExpression("condition")] string? expr = null)
    {
        if (condition)
            throw new AssertionException(message ?? $"Expected false, but got true for expression: {expr}");
    }

    public static void Equal<T>(T expected, T actual, string? message = null)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new AssertionException(message ?? $"Expected: {expected}, Actual: {actual}");
    }

    public static void NotEqual<T>(T notExpected, T actual, string? message = null)
    {
        if (EqualityComparer<T>.Default.Equals(notExpected, actual))
            throw new AssertionException(message ?? $"Expected values to differ, but both were: {actual}");
    }

    public static void NotNull(object? value, string? message = null)
    {
        if (value is null)
            throw new AssertionException(message ?? "Expected value not to be null.");
    }

    public static void Null(object? value, string? message = null)
    {
        if (value is not null)
            throw new AssertionException(message ?? $"Expected null, but got: {value}");
    }

    public static void Contains(string expectedSubstring, string actualString)
    {
        if (!actualString.Contains(expectedSubstring, StringComparison.OrdinalIgnoreCase))
            throw new AssertionException($"Expected string to contain '{expectedSubstring}', but was: '{actualString}'");
    }

    public static void DoesNotContain(string unexpectedSubstring, string actualString)
    {
        if (actualString.Contains(unexpectedSubstring, StringComparison.OrdinalIgnoreCase))
            throw new AssertionException($"Expected string NOT to contain '{unexpectedSubstring}', but it did: '{actualString}'");
    }

    public static void Single<T>(IEnumerable<T> collection)
    {
        var count = collection.Count();
        if (count != 1)
            throw new AssertionException($"Expected collection to contain exactly 1 element, but contained {count}.");
    }

    public static T Throws<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T ex)
        {
            return ex;
        }
        catch (Exception ex)
        {
            throw new AssertionException($"Expected exception of type {typeof(T).Name}, but got {ex.GetType().Name}: {ex.Message}");
        }

        throw new AssertionException($"Expected exception of type {typeof(T).Name}, but no exception was thrown.");
    }
}

public class AssertionException : Exception
{
    public AssertionException(string message) : base(message) { }
}
