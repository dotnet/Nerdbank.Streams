// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Xunit.Abstractions;

public class MultiplexingStreamV3Tests : MultiplexingStreamV2Tests
{
    public MultiplexingStreamV3Tests(ITestOutputHelper logger)
        : base(logger)
    {
    }

    protected override int ProtocolMajorVersion => 3;
}
