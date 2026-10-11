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
}
