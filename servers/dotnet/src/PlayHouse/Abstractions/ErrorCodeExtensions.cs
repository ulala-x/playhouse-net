#nullable enable

using System.ComponentModel;
using System.Reflection;

namespace PlayHouse.Abstractions;

/// <summary>
/// Extension methods for <see cref="ErrorCode"/> enum.
/// </summary>
public static class ErrorCodeExtensions
{
    /// <summary>
    /// Gets the description from the [Description] attribute of the error code.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <returns>The description string from the attribute, or the error code name if no description is found.</returns>
    public static string GetDescription(this ErrorCode errorCode)
    {
        var field = errorCode.GetType().GetField(errorCode.ToString());
        if (field == null)
        {
            return errorCode.ToString();
        }

        var attribute = field.GetCustomAttribute<DescriptionAttribute>();
        return attribute?.Description ?? errorCode.ToString();
    }

    /// <summary>
    /// Converts the error code to its underlying ushort value.
    /// </summary>
    /// <param name="errorCode">The error code.</param>
    /// <returns>The ushort value of the error code.</returns>
    public static ushort ToUInt16(this ErrorCode errorCode)
    {
        return (ushort)errorCode;
    }
}
