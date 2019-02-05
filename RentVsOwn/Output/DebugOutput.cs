﻿using System;
using System.Diagnostics;
using JetBrains.Annotations;

namespace RentVsOwn.Output
{
    /// <summary>
    ///     Output to console/debug.
    ///     Our standard output method.
    /// </summary>
    [PublicAPI]
    public sealed class DebugOutput : IOutput
    {
        /// <inheritdoc />
        public void Flush()
        {
        }

        /// <inheritdoc />
        public void VerboseLine(string text)
        {
            Debug.WriteLine(text ?? string.Empty);
        }

        /// <inheritdoc />
        public void WriteLine(string text)
        {
            Console.WriteLine(text ?? string.Empty);
            Debug.WriteLine(text ?? string.Empty);
        }
    }
}