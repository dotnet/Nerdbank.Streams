// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams.Interop.Tests
{
    using System;
    using System.Threading.Tasks;
    using Nerdbank.Streams;

    /// <summary>Entrypoint of the test app.</summary>
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            ////System.Diagnostics.Debugger.Launch();
            var mx = await MultiplexingStream.CreateAsync(FullDuplexStream.Splice(Console.OpenStandardInput(), Console.OpenStandardOutput()));

            var clientOffer = mx.AcceptChannelAsync("clientOffer");
            var serverOffer = mx.OfferChannelAsync("serverOffer");

            await mx.Completion;
        }
    }
}
