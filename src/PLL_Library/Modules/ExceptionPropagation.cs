using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.ExceptionServices;

namespace PLL.Library;

internal static class ExceptionPropagation
{
    [DoesNotReturn]
    internal static void Throw(Exception Failure)
    {
        ExceptionDispatchInfo.Capture(Failure).Throw();
        throw new InvalidOperationException("Exception dispatch unexpectedly returned.");
    }
}
