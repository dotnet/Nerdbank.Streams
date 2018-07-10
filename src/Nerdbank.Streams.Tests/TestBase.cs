// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Threading;
using Validation;
using Xunit.Abstractions;

public abstract class TestBase
{
    protected static readonly TimeSpan ExpectedTimeout = TimeSpan.FromMilliseconds(200);

    protected static readonly TimeSpan UnexpectedTimeout = Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(5);

    private readonly CancellationTokenSource timeoutTokenSource;

    protected TestBase(ITestOutputHelper logger)
    {
        this.Logger = logger;
        this.timeoutTokenSource = new CancellationTokenSource(TestTimeout);
    }

    protected ITestOutputHelper Logger { get; }

    protected CancellationToken TimeoutToken => Debugger.IsAttached ? CancellationToken.None : this.timeoutTokenSource.Token;

    private static TimeSpan TestTimeout => UnexpectedTimeout;

    public async Task ReadAsync(Stream stream, byte[] buffer, int? count = null, int offset = 0, bool isAsync = true)
    {
        Requires.NotNull(stream, nameof(stream));
        Requires.NotNull(buffer, nameof(buffer));

        count = count ?? buffer.Length;

        if (count == 0)
        {
            return;
        }

        int bytesRead = 0;
        while (bytesRead < count)
        {
            int bytesJustRead = isAsync
                ? await stream.ReadAsync(buffer, offset + bytesRead, count.Value - bytesRead, this.TimeoutToken).WithCancellation(this.TimeoutToken)
                : stream.Read(buffer, offset + bytesRead, count.Value - bytesRead);
            if (bytesJustRead == 0)
            {
                throw new EndOfStreamException();
            }

            bytesRead += bytesJustRead;
        }
    }
}
