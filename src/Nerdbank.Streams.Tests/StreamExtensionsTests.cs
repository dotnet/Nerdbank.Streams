// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

using System;
using System.IO;
using System.IO.Pipelines;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft;
using Microsoft.VisualStudio.Threading;
using Moq;
using Nerdbank.Streams;
using Xunit;
using Xunit.Abstractions;

public class StreamExtensionsTests : TestBase
{
    public StreamExtensionsTests(ITestOutputHelper logger)
        : base(logger)
    {
    }
}
