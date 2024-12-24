﻿// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Buffers;
using System.Diagnostics;
using System.Text;
using Xunit;

internal class XunitTraceListener : TraceListener
{
    private readonly ITestOutputHelper logger;
    private readonly int testId;
    private readonly StringBuilder lineInProgress = new StringBuilder();
    private readonly Stopwatch testRuntime;
    private bool disposed;

    internal XunitTraceListener(ITestOutputHelper logger, int testId, Stopwatch testTime)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.testId = testId;
        this.testRuntime = testTime;
    }

    public override bool IsThreadSafe => false;

    /// <summary>
    /// Gets or sets the <see cref="Encoding"/> to use to decode the data for readable trace messages
    /// if the data is encoded text.
    /// </summary>
    public Encoding? DataEncoding { get; set; }

    public override unsafe void TraceData(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, object? data)
    {
        if (data is ReadOnlySequence<byte> sequence)
        {
            // Trim the traced output in case it's ridiculously huge.
            const int maxLength = 100;
            bool truncated = false;
            if (sequence.Length > maxLength)
            {
                sequence = sequence.Slice(0, maxLength);
                truncated = true;
            }

            var sb = new StringBuilder(2 + ((int)sequence.Length * 2));
            Decoder? decoder = this.DataEncoding?.GetDecoder();
            sb.Append(decoder != null ? "\"" : "0x");
            foreach (ReadOnlyMemory<byte> segment in sequence)
            {
                if (decoder != null)
                {
                    // Write out decoded characters.
                    using (MemoryHandle segmentPointer = segment.Pin())
                    {
                        int charCount = decoder.GetCharCount((byte*)segmentPointer.Pointer, segment.Length, false);
                        char[] chars = ArrayPool<char>.Shared.Rent(charCount);
                        try
                        {
                            fixed (char* pChars = &chars[0])
                            {
                                int actualCharCount = decoder.GetChars((byte*)segmentPointer.Pointer, segment.Length, pChars, charCount, flush: false);
                                sb.Append(pChars, actualCharCount);
                            }
                        }
                        finally
                        {
                            ArrayPool<char>.Shared.Return(chars);
                        }
                    }
                }
                else
                {
                    // Write out data blob as hex
                    for (int i = 0; i < segment.Length; i++)
                    {
                        sb.AppendFormat("{0:X2}", segment.Span[i]);
                    }
                }
            }

            if (decoder != null)
            {
                int charCount = decoder.GetCharCount(Array.Empty<byte>(), 0, 0, flush: true);
                if (charCount > 0)
                {
                    char[] chars = ArrayPool<char>.Shared.Rent(charCount);
                    try
                    {
                        int actualCharCount = decoder.GetChars(Array.Empty<byte>(), 0, 0, chars, 0, flush: true);
                        sb.Append(chars, 0, actualCharCount);
                    }
                    finally
                    {
                        ArrayPool<char>.Shared.Return(chars);
                    }
                }

                sb.Append('"');
            }

            if (truncated)
            {
                sb.Append("...");
            }

            this.logger.WriteLine(sb.ToString());
        }
    }

    public override void Write(string? message) => this.lineInProgress.Append(message);

    public override void WriteLine(string? message)
    {
        if (!this.disposed)
        {
            this.logger.WriteLine($"[{this.testId,4} {this.testRuntime.Elapsed.TotalSeconds:00.00}] {this.lineInProgress}{message}");
            this.lineInProgress.Clear();
        }
    }

    protected override void Dispose(bool disposing)
    {
        this.disposed = true;
        base.Dispose(disposing);
    }
}
