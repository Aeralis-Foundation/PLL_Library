using System;
using System.Collections.Generic;

namespace PLL.Library;

internal static class CleanupOperations
{
    internal static void Capture(ref List<Exception>? Failures, Action Operation)
    {
        try { Operation(); }
        catch (Exception Failure) { (Failures ??= new List<Exception>()).Add(Failure); }
    }

    internal static void Throw(List<Exception>? Failures, string Message)
    {
        if (Failures is { Count: 1 }) ExceptionPropagation.Throw(Failures[0]);
        if (Failures is not null) throw new AggregateException(Message, Failures);
    }
}
