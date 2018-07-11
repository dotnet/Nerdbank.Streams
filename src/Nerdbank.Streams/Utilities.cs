// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

namespace Nerdbank.Streams
{
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Microsoft;

    /// <summary>
    /// Internal utilities.
    /// </summary>
    internal static class Utilities
    {
        /// <summary>
        /// A completed task.
        /// </summary>
        internal static readonly Task CompletedTask = Task.FromResult(0);

        /// <summary>
        /// Removes an element from the middle of a queue without disrupting the other elements.
        /// </summary>
        /// <typeparam name="T">The element to remove.</typeparam>
        /// <param name="queue">The queue to modify.</param>
        /// <param name="valueToRemove">The value to remove.</param>
        /// <returns><c>true</c> if the value was found and removed; <c>false</c> if no match was found.</returns>
        /// <remarks>
        /// If a value appears multiple times in the queue, only its first entry is removed.
        /// </remarks>
        internal static bool RemoveMidQueue<T>(this Queue<T> queue, T valueToRemove)
            where T : class
        {
            Requires.NotNull(queue, nameof(queue));
            Requires.NotNull(valueToRemove, nameof(valueToRemove));

            int originalCount = queue.Count;
            int dequeueCounter = 0;
            bool found = false;
            while (dequeueCounter < originalCount)
            {
                dequeueCounter++;
                T dequeued = queue.Dequeue();
                if (!found && dequeued == valueToRemove)
                { // only find 1 match
                    found = true;
                }
                else
                {
                    queue.Enqueue(dequeued);
                }
            }

            return found;
        }
    }
}
