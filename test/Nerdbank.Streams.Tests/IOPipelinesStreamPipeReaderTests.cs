﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO.Pipelines;
using Xunit;

public class IOPipelinesStreamPipeReaderTests : StreamPipeReaderTestBase
{
    public IOPipelinesStreamPipeReaderTests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    protected override PipeReader CreatePipeReader(Stream stream, int hintSize = 0) => PipeReader.Create(stream, new StreamPipeReaderOptions(bufferSize: hintSize == 0 ? -1 : hintSize));
}
