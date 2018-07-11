// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit.Abstractions;

internal class XunitTraceListener : TraceListener
{
    private readonly ITestOutputHelper logger;
    private readonly string name;
    private readonly StringBuilder lineInProgress = new StringBuilder();
    private bool disposed;

    internal XunitTraceListener(ITestOutputHelper logger, string name)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.name = name;
    }

    public override bool IsThreadSafe => false;

    public override void Write(string message) => this.lineInProgress.Append(message);

    public override void WriteLine(string message)
    {
        if (!this.disposed)
        {
            this.logger.WriteLine(this.lineInProgress.ToString() + message);
            this.lineInProgress.Clear();
        }
    }

    protected override void Dispose(bool disposing)
    {
        this.disposed = true;
        base.Dispose(disposing);
    }
}
