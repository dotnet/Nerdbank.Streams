// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE.txt file in the project root for full license information.

/* This is a derivative from multiple answers on https://stackoverflow.com/questions/3342941/kill-child-process-when-parent-process-is-killed */

#if NETFRAMEWORK

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft;
using PInvoke;
using static PInvoke.Kernel32;

#pragma warning disable SA1629 // xml doc comments must end with periods (we end with a hyperlink).

/// <summary>
/// Allows processes to be automatically killed if this parent process unexpectedly quits
/// (or when an instance of this class is disposed).
/// </summary>
/// <remarks>
/// This "just works" on Windows 8.
/// To support Windows Vista or Windows 7 requires an app.manifest with specific content as described here:
/// https://stackoverflow.com/a/9507862/46926
/// </remarks>
internal class ProcessJobTracker : IDisposable
{
    /// <summary>
    /// The job handle.
    /// </summary>
    /// <remarks>
    /// Closing this handle would close all tracked processes. So we don't do it in this process
    /// so that it happens automatically when our process exits.
    /// </remarks>
    private SafeObjectHandle jobHandle;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessJobTracker"/> class.
    /// </summary>
    public ProcessJobTracker()
    {
        // The job name is optional (and can be null) but it helps with diagnostics.
        //  If it's not null, it has to be unique. Use SysInternals' Handle command-line
        //  utility: handle -a ChildProcessTracker
        string jobName = nameof(ProcessJobTracker) + Process.GetCurrentProcess().Id;
        this.jobHandle = CreateJobObject(IntPtr.Zero, jobName);

        var extendedInfo = new JOB_OBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOB_OBJECT_BASIC_LIMIT_INFORMATION
            {
                LimitFlags = JOB_OBJECT_LIMIT_FLAGS.JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
            },
        };

        // This code can be a lot simpler if we use pointers, but since this class is so generally interesting
        // and may be copied and pasted to other projects that prefer to avoid unsafe code, we use Marshal and IntPtr's instead.
        int length = Marshal.SizeOf(extendedInfo);
        IntPtr pExtendedInfo = Marshal.AllocHGlobal(length);
        try
        {
            Marshal.StructureToPtr(extendedInfo, pExtendedInfo, fDeleteOld: false);
            try
            {
                if (!SetInformationJobObject(this.jobHandle, JOBOBJECT_INFO_CLASS.JobObjectExtendedLimitInformation, pExtendedInfo, (uint)length))
                {
                    throw new Win32Exception();
                }
            }
            finally
            {
                Marshal.DestroyStructure<JOB_OBJECT_EXTENDED_LIMIT_INFORMATION>(pExtendedInfo);
            }
        }
        finally
        {
            Marshal.FreeHGlobal(pExtendedInfo);
        }
    }

    /// <summary>
    /// Ensures a given process is killed when the current process exits.
    /// </summary>
    /// <param name="process">The process whose lifetime should never exceed the lifetime of the current process.</param>
    public void AddProcess(Process process)
    {
        Requires.NotNull(process, nameof(process));

        bool success = AssignProcessToJobObject(this.jobHandle, new SafeObjectHandle(process.Handle, ownsHandle: false));
        if (!success && !process.HasExited)
        {
            throw new Win32Exception();
        }
    }

    /// <summary>
    /// Kills all processes previously tracked with <see cref="AddProcess(Process)"/> by closing the Windows Job.
    /// </summary>
    public void Dispose()
    {
        this.jobHandle?.Dispose();
    }
}

#endif
