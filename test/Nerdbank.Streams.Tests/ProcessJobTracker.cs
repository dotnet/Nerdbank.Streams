// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

/* This is a derivative from multiple answers on https://stackoverflow.com/questions/3342941/kill-child-process-when-parent-process-is-killed */

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
    private SafeObjectHandle? jobHandle;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessJobTracker"/> class.
    /// </summary>
    public ProcessJobTracker()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return;
        }

        // The job name is optional (and can be null) but it helps with diagnostics.
        //  If it's not null, it has to be unique. Use SysInternals' Handle command-line
        //  utility: handle -a ChildProcessTracker
        string jobName = nameof(ProcessJobTracker) + Process.GetCurrentProcess().Id;
        this.jobHandle = CreateJobObject(IntPtr.Zero, jobName);

        var extendedInfo = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            BasicLimitInformation = new JOBOBJECT_BASIC_LIMIT_INFORMATION
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
                if (!SetInformationJobObject(this.jobHandle, JOBOBJECTINFOCLASS.JobObjectExtendedLimitInformation, pExtendedInfo, (uint)length))
                {
                    throw new Win32Exception();
                }
            }
            finally
            {
                Marshal.DestroyStructure<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>(pExtendedInfo);
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
    /// <returns>The error that prevented the process from being added to the job; or <see langword="null"/> if the process was successfully added (or the OS has no job objects).</returns>
    /// <remarks>
    /// Job assignment is a best effort convenience for cleaning up child processes, and can fail for environmental
    /// reasons that are outside the caller's control (e.g. the process already exited, or the process already belongs
    /// to a job hierarchy that this job is not a part of). Callers should therefore treat a returned error
    /// as a diagnostic warning rather than a failure.
    /// </remarks>
    public Exception? TryAddProcess(Process process)
    {
        Requires.NotNull(process, nameof(process));

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return null;
        }

        if (!AssignProcessToJobObject(this.jobHandle, new SafeObjectHandle(process.Handle, ownsHandle: false)))
        {
            // Capture the error code *immediately*, since any subsequent interop call
            // (e.g. Process.HasExited) would overwrite it and lead to misleading diagnostics.
            int errorCode = Marshal.GetLastWin32Error();
            return new System.ComponentModel.Win32Exception(errorCode);
        }

        return null;
    }

    /// <summary>
    /// Kills all processes previously tracked with <see cref="TryAddProcess(Process)"/> by closing the Windows Job.
    /// </summary>
    public void Dispose()
    {
        this.jobHandle?.Dispose();
    }
}
