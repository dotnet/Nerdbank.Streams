// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.Streams;

using System.Buffers;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.IO.Pipelines;
using System.IO.Pipes;
#if NETFRAMEWORK
using System.Security.AccessControl;
using System.Security.Principal;
#endif
using System.Runtime.InteropServices;
#if NET
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
#endif
using System.Threading.Tasks.Sources;
using Microsoft;

/// <summary>
/// A zero-copy duplex transport over shared memory implementing <see cref="IDuplexPipe"/>.
/// Serializers write directly into spans pointing to memory shared between the two endpoints,
/// and readers observe a <see cref="ReadOnlySequence{T}"/> over the same shared memory.
/// </summary>
/// <remarks>
/// On Windows the shared memory is backed by the paging file, so no file is created and the OS frees the memory
/// when both processes close it or exit, even by crashing.
/// On other platforms it is backed by a file (in RAM-backed <c>/dev/shm</c> where available) that is deleted as soon as the client opens it.
/// </remarks>
public sealed class SharedMemoryDuplexPipe : IDuplexPipe, IDisposable
{
    private const int HeaderSize = 128;

    // Comparing a platform-specific enum value is safe on any OS; only creating the named event requires Windows.
    private const SharedMemorySignaling WindowsNamedEvent = (SharedMemorySignaling)1;

    /// <summary>
    /// The options for the named pipe through which the endpoints find each other.
    /// </summary>
    /// <remarks>
    /// Where available, <c>CurrentUserOnly</c> ensures that each endpoint's peer runs as the same user.
    /// This keeps processes of other users from connecting to a listener or impersonating one.
    /// </remarks>
    private const System.IO.Pipes.PipeOptions RendezvousPipeOptions =
#if NET
        System.IO.Pipes.PipeOptions.Asynchronous | System.IO.Pipes.PipeOptions.CurrentUserOnly;
#else
        System.IO.Pipes.PipeOptions.Asynchronous;
#endif

    private readonly MemoryMappedFile mapping;
    private readonly MemoryMappedViewAccessor view;
    private readonly RingReader reader;
    private readonly RingWriter writer;
    private string? backingFileToDelete;
    private Signaling? signaling;
    private volatile bool peerLost;
    private volatile int disposed;

    /// <summary>Initializes a new instance of the <see cref="SharedMemoryDuplexPipe"/> class.</summary>
    /// <param name="name">The unique name of the shared memory, used as an OS object name or a file name.</param>
    /// <param name="isServer">Whether this endpoint is the server, which determines which ring it reads and writes.</param>
    /// <param name="capacity">The size of each direction's ring, in bytes.</param>
    /// <param name="create">Whether to create the shared memory, as opposed to opening one the peer created.</param>
    /// <param name="inProcess">Whether both endpoints are in this process.</param>
    /// <param name="baseDirectory">The directory for the backing file on platforms that require one.</param>
    private unsafe SharedMemoryDuplexPipe(string name, bool isServer, int capacity, bool create, bool inProcess, string? baseDirectory)
    {
        long ringSize = HeaderSize + (long)capacity;
        this.mapping = OpenMapping(name, ringSize * 2, create, baseDirectory, out this.backingFileToDelete, out string? backingFileToUnlink);

        try
        {
            this.view = this.mapping.CreateViewAccessor(0, ringSize * 2, MemoryMappedFileAccess.ReadWrite);
            byte* origin = null;
            this.view.SafeMemoryMappedViewHandle.AcquirePointer(ref origin);
            origin += this.view.PointerOffset;
            Ring clientToServer = new(origin, capacity);
            Ring serverToClient = new(origin + ringSize, capacity);
            this.DataWaiter = new Waiter(inProcess);
            this.SpaceWaiter = new Waiter(inProcess);
            this.reader = new RingReader(this, isServer ? clientToServer : serverToClient);
            this.writer = new RingWriter(this, isServer ? serverToClient : clientToServer);
            if (backingFileToUnlink is not null)
            {
                // Only a fully initialized client removes the name; its mapping keeps the data alive.
                File.Delete(backingFileToUnlink);
            }
        }
        catch
        {
            this.view?.Dispose();
            this.mapping.Dispose();
            throw;
        }
    }

    /// <inheritdoc/>
    public PipeReader Input => this.reader;

    /// <inheritdoc/>
    public PipeWriter Output => this.writer;

    /// <summary>Gets the waiter that the reader parks on until the peer publishes data.</summary>
    private Waiter DataWaiter { get; }

    /// <summary>Gets the waiter that the writer parks on until the peer releases space.</summary>
    private Waiter SpaceWaiter { get; }

    private bool IsDisposed => this.disposed != 0;

    /// <summary>
    /// Listens on the specified channel name and accepts a connecting client.
    /// </summary>
    /// <param name="channelName">The unique name for the shared channel.</param>
    /// <param name="options">Options controlling ring capacity, signaling, and backing file directory.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A connected <see cref="SharedMemoryDuplexPipe"/> endpoint representing the server side.</returns>
    public static async Task<SharedMemoryDuplexPipe> ListenAsync(
        string channelName,
        SharedMemoryPipeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnsupportedPlatform();
        Requires.NotNullOrEmpty(channelName, nameof(channelName));
        options ??= new SharedMemoryPipeOptions();
        int capacity = options.Capacity;
        string name = $"nbjsonrpc-{channelName}";

        SharedMemorySignaling signalingMode = options.Signaling switch
        {
            SharedMemorySignaling.Auto => IsWindows() ? WindowsNamedEvent : SharedMemorySignaling.Doorbell,
            var specific => specific,
        };

        SharedMemoryDuplexPipe server = new(name, isServer: true, capacity, create: true, inProcess: false, options.BaseDirectory);
        try
        {
            NamedPipeServerStream rendezvous = CreateRendezvousServer(name);

            server.signaling = signalingMode switch
            {
                WindowsNamedEvent when IsWindows() => new NamedEventSignaling(channelName, server, isServer: true, rendezvous),
                WindowsNamedEvent => throw new PlatformNotSupportedException("Named-event signaling is supported only on Windows."),
                SharedMemorySignaling.Doorbell => new DoorbellSignaling(rendezvous, server),
                _ => throw new ArgumentOutOfRangeException(nameof(options), options.Signaling, "Invalid signaling mode."),
            };

            using (cancellationToken.Register(static s => ((PipeStream)s!).Close(), rendezvous))
            {
                await rendezvous.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            }

            byte[] ready = new byte[1];
            if (await rendezvous.ReadAsync(ready, 0, ready.Length, cancellationToken).ConfigureAwait(false) != 1 || ready[0] != 1)
            {
                throw new IOException("The shared-memory client did not complete initialization.");
            }

            // The client has mapped and unlinked the backing file, so it can no longer be ours to delete.
            server.backingFileToDelete = null;
            server.signaling.Start();
            return server;
        }
        catch
        {
            server.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Connects to a listening server on the specified channel name.
    /// </summary>
    /// <param name="channelName">The unique name of the channel the server is listening on.</param>
    /// <param name="options">Options controlling ring capacity, signaling, and backing file directory.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>A connected <see cref="SharedMemoryDuplexPipe"/> endpoint representing the client side.</returns>
    public static async Task<SharedMemoryDuplexPipe> ConnectAsync(
        string channelName,
        SharedMemoryPipeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnsupportedPlatform();
        Requires.NotNullOrEmpty(channelName, nameof(channelName));
        options ??= new SharedMemoryPipeOptions();
        int capacity = options.Capacity;
        string name = $"nbjsonrpc-{channelName}";

        SharedMemorySignaling signalingMode = options.Signaling switch
        {
            SharedMemorySignaling.Auto => IsWindows() ? WindowsNamedEvent : SharedMemorySignaling.Doorbell,
            var specific => specific,
        };

        NamedPipeClientStream rendezvous = new(".", name, PipeDirection.InOut, RendezvousPipeOptions);
        try
        {
            using (cancellationToken.Register(static s => ((PipeStream)s!).Close(), rendezvous))
            {
                await rendezvous.ConnectAsync(cancellationToken).ConfigureAwait(false);
#if NETFRAMEWORK
                ValidateRendezvousOwner(rendezvous);
#endif
            }
        }
        catch
        {
#if NET || NETSTANDARD2_1
            await rendezvous.DisposeAsync().ConfigureAwait(false);
#else
            rendezvous.Dispose();
#endif
            throw;
        }

        SharedMemoryDuplexPipe? client = null;
        try
        {
            client = new(name, isServer: false, capacity, create: false, inProcess: false, options.BaseDirectory);
            client.signaling = signalingMode switch
            {
                WindowsNamedEvent when IsWindows() => new NamedEventSignaling(channelName, client, isServer: false, rendezvous),
                WindowsNamedEvent => throw new PlatformNotSupportedException("Named-event signaling is supported only on Windows."),
                SharedMemorySignaling.Doorbell => new DoorbellSignaling(rendezvous, client),
                _ => throw new ArgumentOutOfRangeException(nameof(options), options.Signaling, "Invalid signaling mode."),
            };

            await rendezvous.WriteAsync(new byte[] { 1 }, 0, 1, cancellationToken).ConfigureAwait(false);
            client.signaling.Start();
            return client;
        }
        catch
        {
#if NET || NETSTANDARD2_1
            await rendezvous.DisposeAsync().ConfigureAwait(false);
#else
            rendezvous.Dispose();
#endif
            client?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Creates two connected endpoints that share an in-memory mapped ring.
    /// </summary>
    /// <param name="options">Options controlling ring capacity, signaling, and backing file directory.</param>
    /// <param name="cancellationToken">A token to cancel creation.</param>
    /// <returns>The connected client and server endpoints.</returns>
    public static async Task<(SharedMemoryDuplexPipe Client, SharedMemoryDuplexPipe Server)> CreatePairAsync(
        SharedMemoryPipeOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ThrowIfUnsupportedPlatform();
        options ??= new SharedMemoryPipeOptions();
        int capacity = options.Capacity;
        string name = $"nbjsonrpc-{Guid.NewGuid():N}";

        SharedMemoryDuplexPipe server = new(name, isServer: true, capacity, create: true, inProcess: true, options.BaseDirectory);
        SharedMemoryDuplexPipe? client = null;
        try
        {
            client = new(name, isServer: false, capacity, create: false, inProcess: true, options.BaseDirectory);
            server.backingFileToDelete = null;
            server.signaling = new InProcessSignaling(client);
            client.signaling = new InProcessSignaling(server);
            server.signaling.Start();
            client.signaling.Start();
            return (client, server);
        }
        catch
        {
            client?.Dispose();
            server.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Releases all unmanaged resources and backing files associated with this endpoint.
    /// </summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref this.disposed, 1) != 0)
        {
            return;
        }

        this.signaling?.Dispose();
        this.view.SafeMemoryMappedViewHandle.ReleasePointer();
        this.view.Dispose();
        this.mapping.Dispose();
        this.DeleteBackingFile();
    }

    /// <summary>Rejects the netstandard builds, which cannot enforce current-user-only rendezvous on every platform.</summary>
    private static void ThrowIfUnsupportedPlatform()
    {
#if NETSTANDARD2_0 || NETSTANDARD2_1
        throw new PlatformNotSupportedException("Shared-memory IPC requires the .NET Framework or .NET 8 or later assembly; the netstandard assembly cannot enforce current-user-only rendezvous pipe security.");
#endif
    }

    /// <summary>Creates a rendezvous server limited to the current user.</summary>
    private static NamedPipeServerStream CreateRendezvousServer(string name)
    {
#if NETFRAMEWORK
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User ?? throw new InvalidOperationException("The current Windows user has no SID.");
        PipeSecurity security = new();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(user);
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, RendezvousPipeOptions, 4096, 4096, security);
#else
        return new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, RendezvousPipeOptions, 4096, 4096);
#endif
    }

#if NETFRAMEWORK
    /// <summary>Rejects a rendezvous server whose pipe was not created by the current user.</summary>
    private static void ValidateRendezvousOwner(NamedPipeClientStream pipe)
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        SecurityIdentifier user = identity.User ?? throw new InvalidOperationException("The current Windows user has no SID.");
        PipeSecurity security = pipe.GetAccessControl();
        if (!user.Equals(security.GetOwner(typeof(SecurityIdentifier))))
        {
            throw new UnauthorizedAccessException("The shared-memory rendezvous pipe is not owned by the current user.");
        }
    }
#endif

#if NET
    [SupportedOSPlatformGuard("windows")]
#endif
    private static bool IsWindows() =>
#if NET
        OperatingSystem.IsWindows();
#else
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
#endif

    /// <summary>
    /// Creates or opens the shared memory.
    /// </summary>
    /// <param name="name">The unique name of the shared memory.</param>
    /// <param name="size">The size of the shared memory, in bytes.</param>
    /// <param name="create">Whether to create the shared memory, as opposed to opening one the peer created.</param>
    /// <param name="baseDirectory">The directory for the backing file on platforms that require one.</param>
    /// <param name="backingFileToDelete">Receives the path of a backing file that this endpoint must delete when disposed, if any.</param>
    /// <param name="backingFileToUnlink">Receives the backing file path to unlink once the connecting endpoint is fully initialized.</param>
    /// <returns>The mapping.</returns>
    /// <remarks>
    /// On Windows the memory is backed by the paging file rather than a file on disk, so no file is ever created and the memory
    /// is released by the OS when the last process holding it closes or crashes.
    /// .NET does not support named memory on other platforms, so there it is backed by a file (in RAM-backed <c>/dev/shm</c> where available),
    /// which the connecting endpoint deletes after successfully initializing, so a crash after connecting leaves nothing behind.
    /// </remarks>
    private static MemoryMappedFile OpenMapping(string name, long size, bool create, string? baseDirectory, out string? backingFileToDelete, out string? backingFileToUnlink)
    {
        backingFileToDelete = null;
        backingFileToUnlink = null;
        if (IsWindows())
        {
            // Local\ keeps the name visible only within the current logon session.
            string mapName = $@"Local\{name}";
            return create
                ? MemoryMappedFile.CreateNew(mapName, size, MemoryMappedFileAccess.ReadWrite, MemoryMappedFileOptions.None, HandleInheritability.None)
                : MemoryMappedFile.OpenExisting(mapName, MemoryMappedFileRights.ReadWrite, HandleInheritability.None);
        }

#if NET
        string directory = baseDirectory ?? (Directory.Exists("/dev/shm") ? "/dev/shm" : Path.GetTempPath());
        string path = Path.Combine(directory, $"{name}.shm");
        FileStream file;
        if (create)
        {
            file = new(path, new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.ReadWrite,
                Share = FileShare.ReadWrite | FileShare.Delete,
                UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
            });
            backingFileToDelete = path;
        }
        else
        {
            file = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete);
        }

        try
        {
            if (create)
            {
                file.SetLength(size);
            }
            else if (file.Length != size)
            {
                throw new InvalidDataException("The shared memory size does not match the requested capacity.");
            }

            MemoryMappedFile mapping = MemoryMappedFile.CreateFromFile(file, null, size, MemoryMappedFileAccess.ReadWrite, HandleInheritability.None, leaveOpen: false);
            if (!create)
            {
                backingFileToUnlink = path;
            }

            return mapping;
        }
        catch
        {
            file.Dispose();
            if (create)
            {
                TryDeleteFile(path);
            }

            throw;
        }
#else
        throw new PlatformNotSupportedException("Shared-memory IPC on Unix requires the .NET 8 or later assembly.");
#endif
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// Deletes the backing file if this endpoint created it and no peer has taken over responsibility for deleting it.
    /// </summary>
    /// <remarks>
    /// The connecting peer deletes the file once it opens it, after which the name may be reused by another channel,
    /// so this must not be called once a peer has connected.
    /// </remarks>
    private void DeleteBackingFile()
    {
        if (Interlocked.Exchange(ref this.backingFileToDelete, null) is string path)
        {
            TryDeleteFile(path);
        }
    }

    private void ThrowIfDisposed()
    {
        if (this.IsDisposed)
        {
            throw new ObjectDisposedException(this.GetType().FullName);
        }
    }

    private void NotifyPeerData()
    {
        if (!this.IsDisposed)
        {
            this.signaling?.NotifyPeerData();
        }
    }

    private void NotifyPeerSpace()
    {
        if (!this.IsDisposed)
        {
            this.signaling?.NotifyPeerSpace();
        }
    }

    private void OnPeerLost()
    {
        this.peerLost = true;
        this.DataWaiter.Wake();
        this.SpaceWaiter.Wake();
    }

    /// <summary>A reusable, allocation-free awaitable that one side parks on until it is woken, canceled, or disarmed.</summary>
    private sealed class Waiter(bool runContinuationsAsynchronously) : IValueTaskSource
    {
        private const int Idle = 0;
        private const int Armed = 1;

        private ManualResetValueTaskSourceCore<bool> core = new() { RunContinuationsAsynchronously = runContinuationsAsynchronously };
        private CancellationTokenRegistration registration;
        private int state;

        public void GetResult(short token)
        {
            CancellationTokenRegistration registration = this.registration;
            this.registration = default;
            registration.Dispose();
            this.core.GetResult(token);
        }

        public ValueTaskSourceStatus GetStatus(short token) => this.core.GetStatus(token);

        public void OnCompleted(Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => this.core.OnCompleted(continuation, state, token, flags);

        /// <summary>Prepares to wait. The caller must recheck its condition afterward and call <see cref="TryDisarm"/> if it no longer needs to wait.</summary>
        internal ValueTask ArmAsync(CancellationToken cancellationToken)
        {
            this.core.Reset();
            Volatile.Write(ref this.state, Armed);
            if (cancellationToken.CanBeCanceled)
            {
#if NET
                this.registration = cancellationToken.UnsafeRegister(static (s, t) => ((Waiter)s!).Cancel(t), this);
#else
                this.registration = cancellationToken.Register(static s => ((Waiter)s!).Cancel(), this);
#endif
            }

            return new ValueTask(this, this.core.Version);
        }

        internal bool TryDisarm()
        {
            if (Interlocked.CompareExchange(ref this.state, Idle, Armed) != Armed)
            {
                return false;
            }

            CancellationTokenRegistration registration = this.registration;
            this.registration = default;
            registration.Dispose();
            return true;
        }

        /// <summary>Releases the waiting side, if any. A wake-up that arrives while nobody waits is dropped, which is safe because waiters recheck shared state after arming.</summary>
        internal void Wake()
        {
            if (Interlocked.CompareExchange(ref this.state, Idle, Armed) == Armed)
            {
                this.core.SetResult(true);
            }
        }

        private void Cancel(CancellationToken cancellationToken = default)
        {
            if (Interlocked.CompareExchange(ref this.state, Idle, Armed) == Armed)
            {
                this.core.SetException(new OperationCanceledException(cancellationToken));
            }
        }
    }

    private abstract class Signaling : IDisposable
    {
        public virtual void Dispose()
        {
        }

        internal virtual void Start()
        {
        }

        internal abstract void NotifyPeerData();

        internal abstract void NotifyPeerSpace();
    }

    private sealed class InProcessSignaling(SharedMemoryDuplexPipe peer) : Signaling
    {
        internal override void NotifyPeerData() => peer.DataWaiter.Wake();

        internal override void NotifyPeerSpace() => peer.SpaceWaiter.Wake();
    }

    /// <summary>
    /// Signals with named auto-reset events. A dedicated thread per endpoint waits on them and resumes the parked
    /// reader or writer inline. A sentinel pipe monitors peer liveness.
    /// </summary>
    private sealed class NamedEventSignaling : Signaling
    {
        private readonly SharedMemoryDuplexPipe owner;
        private readonly EventWaitHandle inboundData;
        private readonly EventWaitHandle outboundSpace;
        private readonly EventWaitHandle outboundData;
        private readonly EventWaitHandle inboundSpace;
        private readonly PipeStream sentinel;
        private readonly Thread thread;
        private readonly Thread livenessThread;
        private volatile bool stopping;
        private bool started;

        internal NamedEventSignaling(string name, SharedMemoryDuplexPipe owner, bool isServer, PipeStream sentinel)
        {
            this.owner = owner;
            this.sentinel = sentinel;
            string inbound = isServer ? "c2s" : "s2c";
            string outbound = isServer ? "s2c" : "c2s";
            this.inboundData = Open(name, inbound, "data");
            this.outboundSpace = Open(name, outbound, "space");
            this.outboundData = Open(name, outbound, "data");
            this.inboundSpace = Open(name, inbound, "space");
            this.thread = new Thread(this.Run) { IsBackground = true, Name = "Shared-memory event listener" };
            this.livenessThread = new Thread(this.WatchLiveness) { IsBackground = true, Name = "Shared-memory liveness sentinel" };
        }

        public override void Dispose()
        {
            this.stopping = true;
            this.inboundData.Set();
            this.sentinel.Dispose();
            if (this.started && Thread.CurrentThread != this.thread)
            {
                this.thread.Join();
            }

            if (this.started && Thread.CurrentThread != this.livenessThread)
            {
                this.livenessThread.Join();
            }

            this.inboundData.Dispose();
            this.outboundSpace.Dispose();
            this.outboundData.Dispose();
            this.inboundSpace.Dispose();
        }

        internal override void Start()
        {
            this.started = true;
            this.thread.Start();
            this.livenessThread.Start();
        }

        internal override void NotifyPeerData() => this.outboundData.Set();

        internal override void NotifyPeerSpace() => this.inboundSpace.Set();

        private static EventWaitHandle Open(string name, string ring, string kind) => new(false, EventResetMode.AutoReset, $"{name}.{ring}.{kind}");

        private void WatchLiveness()
        {
            try
            {
                byte[] buf = new byte[1];
                while (this.sentinel.Read(buf, 0, 1) > 0)
                {
                }
            }
            catch
            {
            }

            if (!this.stopping)
            {
                this.owner.OnPeerLost();
            }
        }

        private void Run()
        {
            WaitHandle[] handles = [this.inboundData, this.outboundSpace];
            while (true)
            {
                int index = WaitHandle.WaitAny(handles);
                if (this.stopping)
                {
                    return;
                }

                (index == 0 ? this.owner.DataWaiter : this.owner.SpaceWaiter).Wake();
                if (this.stopping)
                {
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Signals by sending one byte over a named pipe (a Unix domain socket on non-Windows platforms).
    /// </summary>
    private sealed class DoorbellSignaling : Signaling
    {
        private static readonly byte[] DataByte = [1];
        private static readonly byte[] SpaceByte = [2];

        private readonly PipeStream stream;
        private readonly SharedMemoryDuplexPipe owner;
        private readonly object writeLock = new();
        private readonly Thread thread;
        private volatile bool stopping;
        private bool started;

        internal DoorbellSignaling(PipeStream stream, SharedMemoryDuplexPipe owner)
        {
            this.stream = stream;
            this.owner = owner;
            this.thread = new Thread(this.Run) { IsBackground = true, Name = "Shared-memory doorbell listener" };
        }

        public override void Dispose()
        {
            this.stopping = true;
            this.stream.Dispose();
            if (this.started && Thread.CurrentThread != this.thread)
            {
                this.thread.Join();
            }
        }

        internal override void Start()
        {
            this.started = true;
            this.thread.Start();
        }

        internal override void NotifyPeerData() => this.Ring(DataByte);

        internal override void NotifyPeerSpace() => this.Ring(SpaceByte);

        private void Ring(byte[] message)
        {
            lock (this.writeLock)
            {
                try
                {
                    this.stream.Write(message, 0, 1);
                }
                catch (IOException)
                {
                    this.owner.OnPeerLost();
                }
                catch (ObjectDisposedException)
                {
                }
            }
        }

        private void Run()
        {
            byte[] buffer = new byte[64];
            try
            {
                while (true)
                {
                    int count = this.stream.Read(buffer, 0, buffer.Length);
                    if (count == 0)
                    {
                        break;
                    }

                    bool data = false;
                    bool space = false;
                    foreach (byte b in buffer.AsSpan(0, count))
                    {
                        data |= b == DataByte[0];
                        space |= b == SpaceByte[0];
                    }

                    if (data)
                    {
                        this.owner.DataWaiter.Wake();
                    }

                    if (space)
                    {
                        this.owner.SpaceWaiter.Wake();
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
            {
            }

            if (!this.stopping)
            {
                this.owner.OnPeerLost();
            }
        }
    }

    /// <summary>
    /// A view of one direction's single-producer/single-consumer ring.
    /// </summary>
    private sealed unsafe class Ring
    {
        private const int WrittenOffset = 0;
        private const int WriterCompletedOffset = 8;
        private const int WriterWaitingOffset = 12;
        private const int GapStartOffset = 16;
        private const int ReadOffset = 64;
        private const int ReaderCompletedOffset = 72;
        private const int ReaderWaitingOffset = 76;

        private readonly byte* header;
        private readonly SharedMemoryManager manager;

        internal Ring(byte* origin, int capacity)
        {
            this.header = origin;
            this.Capacity = capacity;
            this.manager = new SharedMemoryManager(origin + HeaderSize, capacity);
        }

        internal int Capacity { get; }

        /// <summary>Gets the total number of bytes ever published by the writer.</summary>
        internal long Written => Volatile.Read(ref *(long*)(this.header + WrittenOffset));

        /// <summary>Gets the total number of bytes ever released by the reader.</summary>
        internal long Read => Volatile.Read(ref *(long*)(this.header + ReadOffset));

        /// <summary>
        /// Gets the position at which the writer last skipped ahead to the start of the ring, leaving the remaining bytes before the end unused.
        /// </summary>
        internal long GapStart => Volatile.Read(ref *(long*)(this.header + GapStartOffset));

        internal bool WriterCompleted => Volatile.Read(ref *(int*)(this.header + WriterCompletedOffset)) != 0;

        internal bool WriterWaiting => Volatile.Read(ref *(int*)(this.header + WriterWaitingOffset)) != 0;

        internal bool ReaderCompleted => Volatile.Read(ref *(int*)(this.header + ReaderCompletedOffset)) != 0;

        internal bool ReaderWaiting => Volatile.Read(ref *(int*)(this.header + ReaderWaitingOffset)) != 0;

        internal Memory<byte> Slice(int offset, int length) => this.manager.Memory.Slice(offset, length);

        /// <summary>Rounds a position up to the next start of the ring.</summary>
        internal long RoundUpToStart(long position) => (position + this.Capacity - 1) & ~(long)(this.Capacity - 1);

        internal void PublishGapStart(long position) => Interlocked.Exchange(ref *(long*)(this.header + GapStartOffset), position);

        internal void PublishWritten(long total) => Interlocked.Exchange(ref *(long*)(this.header + WrittenOffset), total);

        internal void PublishRead(long total) => Interlocked.Exchange(ref *(long*)(this.header + ReadOffset), total);

        internal void SetWriterCompleted() => Interlocked.Exchange(ref *(int*)(this.header + WriterCompletedOffset), 1);

        internal void SetReaderCompleted() => Interlocked.Exchange(ref *(int*)(this.header + ReaderCompletedOffset), 1);

        internal void SetWriterWaiting(bool value) => Interlocked.Exchange(ref *(int*)(this.header + WriterWaitingOffset), value ? 1 : 0);

        internal void SetReaderWaiting(bool value) => Interlocked.Exchange(ref *(int*)(this.header + ReaderWaitingOffset), value ? 1 : 0);
    }

    private sealed unsafe class SharedMemoryManager(byte* pointer, int length) : MemoryManager<byte>
    {
        public override Span<byte> GetSpan() => new(pointer, length);

        public override MemoryHandle Pin(int elementIndex = 0) => new(pointer + elementIndex);

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }

    private sealed class RingSegment : ReadOnlySequenceSegment<byte>
    {
        internal void Set(Memory<byte> memory, long runningIndex, RingSegment? next)
        {
            this.Memory = memory;
            this.RunningIndex = runningIndex;
            this.Next = next;
        }
    }

    /// <summary>Exposes ring bytes as a <see cref="ReadOnlySequence{T}"/> without copying them.</summary>
    private sealed class RingReader(SharedMemoryDuplexPipe owner, Ring ring) : PipeReader
    {
        private readonly RingSegment first = new();
        private readonly RingSegment second = new();
        private ReadOnlySequence<byte> current;
        private long consumedTotal;
        private long examinedTotal;
        private volatile bool canceled;

        public override void AdvanceTo(SequencePosition consumed) => this.AdvanceTo(consumed, consumed);

        public override void AdvanceTo(SequencePosition consumed, SequencePosition examined)
        {
            if (this.current.IsEmpty)
            {
                return;
            }

            long start = this.consumedTotal;
            long consumedCount = this.current.Slice(0, consumed).Length;
            long examinedOffset = this.current.Slice(0, examined).Length;
            this.examinedTotal = Math.Max(this.examinedTotal, start + examinedOffset);
            this.current = default;
            if (consumedCount > 0)
            {
                this.consumedTotal = start + consumedCount;
                ring.PublishRead(this.consumedTotal);
                if (ring.WriterWaiting)
                {
                    owner.NotifyPeerSpace();
                }
            }
        }

        public override void CancelPendingRead()
        {
            this.canceled = true;
            owner.DataWaiter.Wake();
        }

        public override void Complete(Exception? exception = null)
        {
            if (!owner.IsDisposed)
            {
                ring.SetReaderCompleted();
                owner.NotifyPeerSpace();
            }
        }

#if NET
        // IPC reads usually suspend; pool the async state machine rather than allocate one for each message.
        [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
#endif
        public override async ValueTask<ReadResult> ReadAsync(CancellationToken cancellationToken = default)
        {
            owner.ThrowIfDisposed();
            while (true)
            {
                if (this.HasNews())
                {
                    return this.Read();
                }

                ValueTask wait = owner.DataWaiter.ArmAsync(cancellationToken);
                ring.SetReaderWaiting(true);
                if (this.HasNews() && owner.DataWaiter.TryDisarm())
                {
                    ring.SetReaderWaiting(false);
                    continue;
                }

                try
                {
                    await wait.ConfigureAwait(false);
                }
                finally
                {
                    ring.SetReaderWaiting(false);
                }
            }
        }

        public override bool TryRead(out ReadResult result)
        {
            owner.ThrowIfDisposed();
            if (this.canceled || ring.Written > this.consumedTotal || ring.WriterCompleted || owner.peerLost)
            {
                result = this.Read();
                return true;
            }

            result = default;
            return false;
        }

        private bool HasNews() => this.canceled || ring.Written > this.examinedTotal || ring.WriterCompleted || owner.peerLost;

        private ReadResult Read()
        {
            bool completed = ring.WriterCompleted || owner.peerLost;
            long written = ring.Written;
            if (written > this.consumedTotal && (this.consumedTotal & (ring.Capacity - 1)) != 0 && ring.GapStart == this.consumedTotal)
            {
                // The writer moved back to the start of the ring rather than continue toward the end; skip the unused tail.
                this.consumedTotal = ring.RoundUpToStart(this.consumedTotal);
                this.examinedTotal = Math.Max(this.examinedTotal, this.consumedTotal);
                ring.PublishRead(this.consumedTotal);
                if (ring.WriterWaiting)
                {
                    owner.NotifyPeerSpace();
                }
            }

            long available = written - this.consumedTotal;
            int offset = (int)(this.consumedTotal & (ring.Capacity - 1));
            int contiguous = (int)Math.Min(available, ring.Capacity - offset);

            this.current = available == 0 ? default
                : contiguous == available ? this.CreateSequence(offset, contiguous)
                : this.CreateWrappedSequence(offset, contiguous, (int)(available - contiguous));

            bool wasCanceled = this.canceled;
            this.canceled = false;
            return new ReadResult(this.current, wasCanceled, completed);
        }

        private ReadOnlySequence<byte> CreateSequence(int offset, int length)
        {
            this.first.Set(ring.Slice(offset, length), 0, null);
            return new ReadOnlySequence<byte>(this.first, 0, this.first, length);
        }

        private ReadOnlySequence<byte> CreateWrappedSequence(int offset, int firstLength, int secondLength)
        {
            this.second.Set(ring.Slice(0, secondLength), firstLength, null);
            this.first.Set(ring.Slice(offset, firstLength), 0, this.second);
            return new ReadOnlySequence<byte>(this.first, 0, this.second, secondLength);
        }
    }

    /// <summary>Hands serializers spans that point directly into the ring.</summary>
    private sealed class RingWriter(SharedMemoryDuplexPipe owner, Ring ring) : PipeWriter
    {
        private long writtenTotal;
        private long gapStart;
        private int pending;
        private byte[]? fallback;
        private int fallbackLength;
        private int fallbackCopied;
        private volatile bool canceled;

        public override void Advance(int bytes)
        {
            if (this.fallback is not null)
            {
                this.fallbackLength += bytes;
            }
            else
            {
                this.pending += bytes;
            }
        }

        public override void CancelPendingFlush()
        {
            this.canceled = true;
            owner.SpaceWaiter.Wake();
        }

        public override void Complete(Exception? exception = null)
        {
            if (!owner.IsDisposed)
            {
                this.PublishDirect();
                ring.SetWriterCompleted();
                owner.NotifyPeerData();
            }
        }

        public override async ValueTask<FlushResult> FlushAsync(CancellationToken cancellationToken = default)
        {
            owner.ThrowIfDisposed();
            this.PublishDirect();
            if (this.fallback is byte[] buffer)
            {
                while (this.fallbackCopied < this.fallbackLength)
                {
                    if (this.IsPeerGone())
                    {
                        break;
                    }

                    this.TryRewind();

                    int available = this.ContiguousFree();
                    if (available == 0)
                    {
                        if (this.canceled)
                        {
                            this.canceled = false;
                            return new FlushResult(isCanceled: true, isCompleted: false);
                        }

                        await this.WaitForSpaceAsync(cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    int length = Math.Min(available, this.fallbackLength - this.fallbackCopied);
                    buffer.AsSpan(this.fallbackCopied, length).CopyTo(ring.Slice(this.Offset(), length).Span);
                    this.fallbackCopied += length;
                    this.writtenTotal += length;
                    this.Publish();
                }

                ArrayPool<byte>.Shared.Return(buffer);
                this.fallback = null;
                this.fallbackLength = 0;
                this.fallbackCopied = 0;
            }

            bool wasCanceled = this.canceled;
            this.canceled = false;
            return new FlushResult(wasCanceled, this.IsPeerGone());
        }

        public override Memory<byte> GetMemory(int sizeHint = 0)
        {
            owner.ThrowIfDisposed();
            if (this.fallback is not null)
            {
                this.GrowFallback(sizeHint);
                return this.fallback.AsMemory(this.fallbackLength);
            }

            int required = Math.Max(sizeHint, 1);
            this.TryRewind();
            int contiguous = this.ContiguousFree();
            if (contiguous >= required)
            {
                return ring.Slice(this.Offset(), contiguous);
            }

            this.PublishDirect();
            this.fallback = ArrayPool<byte>.Shared.Rent(Math.Max(required, 4096));
            return this.fallback.AsMemory();
        }

        public override Span<byte> GetSpan(int sizeHint = 0) => this.GetMemory(sizeHint).Span;

        private bool IsPeerGone() => ring.ReaderCompleted || owner.peerLost;

        private int Offset() => (int)((this.writtenTotal + this.pending) & (ring.Capacity - 1));

        private int ContiguousFree()
        {
            long used = this.writtenTotal + this.pending - this.EffectiveRead();
            int free = (int)(ring.Capacity - used);
            return Math.Min(free, ring.Capacity - this.Offset());
        }

        /// <summary>
        /// Moves the write position back to the start of the ring when the reader has consumed everything.
        /// </summary>
        /// <remarks>
        /// This keeps typical traffic within the first pages of the ring, so the OS need only provide physical memory
        /// for as much data as is ever in flight at once, rather than eventually for the whole ring.
        /// It also gives the writer the whole ring as contiguous space, so fewer messages need the copying fallback.
        /// The reader recognizes the skipped tail by <see cref="Ring.GapStart"/>.
        /// </remarks>
        private void TryRewind()
        {
            if (this.pending == 0 && this.Offset() != 0 && ring.Read == this.writtenTotal)
            {
                this.gapStart = this.writtenTotal;
                ring.PublishGapStart(this.gapStart);
                this.writtenTotal = ring.RoundUpToStart(this.writtenTotal);
            }
        }

        /// <summary>Gets the reader's position, treating a reader waiting at the last gap as having already skipped it.</summary>
        private long EffectiveRead()
        {
            long read = ring.Read;
            return read == this.gapStart && (read & (ring.Capacity - 1)) != 0 ? ring.RoundUpToStart(read) : read;
        }

        private bool HasSpaceOrNews() => this.ContiguousFree() > 0 || this.canceled || this.IsPeerGone();

        private async ValueTask WaitForSpaceAsync(CancellationToken cancellationToken)
        {
            ValueTask wait = owner.SpaceWaiter.ArmAsync(cancellationToken);
            ring.SetWriterWaiting(true);
            if (this.HasSpaceOrNews() && owner.SpaceWaiter.TryDisarm())
            {
                ring.SetWriterWaiting(false);
                return;
            }

            try
            {
                await wait.ConfigureAwait(false);
            }
            finally
            {
                ring.SetWriterWaiting(false);
            }
        }

        private void GrowFallback(int sizeHint)
        {
            byte[] buffer = this.fallback!;
            int required = this.fallbackLength + Math.Max(sizeHint, 1);
            if (buffer.Length >= required)
            {
                return;
            }

            byte[] larger = ArrayPool<byte>.Shared.Rent(Math.Max(required, buffer.Length * 2));
            buffer.AsSpan(0, this.fallbackLength).CopyTo(larger);
            ArrayPool<byte>.Shared.Return(buffer);
            this.fallback = larger;
        }

        private void PublishDirect()
        {
            if (this.pending > 0)
            {
                this.writtenTotal += this.pending;
                this.pending = 0;
                this.Publish();
            }
        }

        private void Publish()
        {
            ring.PublishWritten(this.writtenTotal);
            if (ring.ReaderWaiting)
            {
                owner.NotifyPeerData();
            }
        }
    }
}
