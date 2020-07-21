// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft;

internal static class LanguageExtensions
{
    internal static void Deconstruct<T>(this T[] array, out T first, out T second)
    {
        Requires.NotNull(array, nameof(array));
        Requires.Argument(array.Length == 2, nameof(array), "A two element array is required.");

        first = array[0];
        second = array[1];
    }
}
