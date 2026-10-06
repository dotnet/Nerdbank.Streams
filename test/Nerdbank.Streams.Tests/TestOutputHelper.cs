// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Globalization;

/// <summary>
/// Writes to the output of whichever TUnit test is currently running,
/// or to the console when no TUnit test is running (e.g. in the IsolatedTestHost process).
/// </summary>
internal sealed class TestOutputHelper : ITestOutputHelper
{
    internal static readonly TestOutputHelper Instance = new();

    private TestOutputHelper()
    {
    }

    public void Write(string message)
    {
        if (TestContext.Current is { } context)
        {
            context.Output.StandardOutput.Write(message);
        }
        else
        {
            Console.Write(message);
        }
    }

    public void Write(string format, params object[] args) => this.Write(string.Format(CultureInfo.CurrentCulture, format, args));

    public void WriteLine(string message)
    {
        if (TestContext.Current is { } context)
        {
            context.Output.WriteLine(message);
        }
        else
        {
            Console.WriteLine(message);
        }
    }

    public void WriteLine(string format, params object[] args) => this.WriteLine(string.Format(CultureInfo.CurrentCulture, format, args));
}
