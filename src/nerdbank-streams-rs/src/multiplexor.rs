use std::{
    collections::{HashMap, VecDeque},
    fmt,
    marker::PhantomData,
    sync::{Arc, Mutex, Weak},
};

use tokio::{
    io::{split, AsyncRead, AsyncReadExt, AsyncWrite, AsyncWriteExt, DuplexStream},
    sync::{mpsc, Notify},
};

use crate::{
    channel::Channel,
    frame::{
        decode_acceptance, decode_error, decode_processed, decode_window_adjust, encode_acceptance,
        encode_error, encode_offer, encode_processed, encode_window_adjust, read_frame,
        read_handshake, write_frame, write_handshake, Code, Frame, MAX_FRAME_PAYLOAD,
    },
};

const DEFAULT_RECEIVE_WINDOW: usize = 50 * MAX_FRAME_PAYLOAD;
const MAX_RECEIVE_WINDOW: usize = 64 * 1024 * 1024;
const DEFAULT_MAX_CHANNEL_RECEIVE_WINDOW: usize = 16 * DEFAULT_RECEIVE_WINDOW;
const DEFAULT_MAX_TOTAL_CHANNEL_RECEIVE_WINDOW: usize = 64 * DEFAULT_RECEIVE_WINDOW;

/// The wire protocol version used by a [`MultiplexingStream`].
#[derive(Clone, Copy, Debug, Eq, PartialEq)]
pub enum ProtocolVersion {
    /// The original fixed-header protocol without flow control.
    V1,
    /// The MessagePack protocol with handshaking and flow control.
    V2,
    /// The handshake-free MessagePack protocol with seeded channels.
    V3,
}

/// The origin of a channel identifier from the local endpoint's perspective.
#[derive(Clone, Copy, Debug, Eq, Hash, PartialEq)]
#[repr(i8)]
pub enum ChannelSource {
    /// The local endpoint offered the channel.
    Local = 1,
    /// The remote endpoint offered the channel.
    Remote = -1,
    /// Both endpoints configured the channel when creating the multiplexor.
    Seeded = 0,
}

impl ChannelSource {
    pub(crate) const fn flipped(self) -> Self {
        match self {
            Self::Local => Self::Remote,
            Self::Remote => Self::Local,
            Self::Seeded => Self::Seeded,
        }
    }
}

/// A channel ID qualified by the endpoint that originated it.
#[derive(Clone, Copy, Debug, Eq, Hash, PartialEq)]
pub struct ChannelId {
    /// The numeric channel ID.
    pub id: u64,
    /// The channel's origin.
    pub source: ChannelSource,
}

/// Configuration for a single channel.
#[derive(Clone, Debug, Default)]
pub struct ChannelOptions {
    /// The maximum unprocessed inbound bytes that the peer may send.
    ///
    /// This crate applies a local 64 MiB upper bound to receive windows. It
    /// rejects channel offers and acceptances that advertise a larger window,
    /// even when that value originated with the peer.
    pub receive_window: Option<usize>,
}

/// Configuration for a [`MultiplexingStream`].
#[derive(Clone, Debug)]
pub struct Options {
    /// The protocol version to speak.
    pub protocol_version: ProtocolVersion,
    /// The default per-channel inbound receive window for protocol v2 and v3.
    ///
    /// This crate applies a local 64 MiB upper bound to receive windows. Peer
    /// advertisements above that bound are rejected as a resource policy.
    pub default_receive_window: usize,
    /// The largest receive window to which an individual channel may
    /// automatically grow.
    ///
    /// This limit applies only to automatic growth in protocol v2 and v3.
    /// It must be at least [`default_receive_window`](Self::default_receive_window)
    /// and no larger than this crate's 64 MiB receive-window limit.
    pub max_channel_receive_window: usize,
    /// The total additional receive-window capacity that all channels on this
    /// stream may reserve through automatic growth.
    ///
    /// The budget excludes every channel's initial receive window and is
    /// released when a channel ends. A value of zero disables automatic
    /// growth while preserving fixed-window flow control. As an unsigned
    /// value, it is always non-negative.
    pub max_total_channel_receive_window: usize,
    /// Channels to make available without an offer/accept exchange in protocol v3.
    pub seeded_channels: Vec<ChannelOptions>,
}

impl Default for Options {
    fn default() -> Self {
        Self {
            protocol_version: ProtocolVersion::V1,
            default_receive_window: DEFAULT_RECEIVE_WINDOW,
            max_channel_receive_window: DEFAULT_MAX_CHANNEL_RECEIVE_WINDOW,
            max_total_channel_receive_window: DEFAULT_MAX_TOTAL_CHANNEL_RECEIVE_WINDOW,
            seeded_channels: Vec::new(),
        }
    }
}

/// An error reported by this crate.
#[derive(Clone, Debug, Eq, PartialEq)]
pub enum Error {
    /// The underlying transport returned an I/O error.
    Io(String),
    /// The peer sent malformed or unsupported protocol data.
    Protocol(String),
    /// The connection or channel closed before an operation completed.
    Closed(String),
    /// The peer terminated a channel with an error.
    Remote(String),
}

impl fmt::Display for Error {
    fn fmt(&self, formatter: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Io(message) => write!(formatter, "I/O error: {message}"),
            Self::Protocol(message) => write!(formatter, "Multiplexing protocol error: {message}"),
            Self::Closed(message) => write!(formatter, "closed: {message}"),
            Self::Remote(message) => write!(formatter, "remote channel error: {message}"),
        }
    }
}

impl std::error::Error for Error {}

impl From<std::io::Error> for Error {
    fn from(error: std::io::Error) -> Self {
        Self::Io(error.to_string())
    }
}

/// Owns a duplex Tokio transport and exposes independently readable and writable channels.
///
/// Clone this value to offer and accept channels from multiple tasks. The
/// transport itself is driven by one reader task and one serialized writer task.
pub struct MultiplexingStream<T> {
    inner: Arc<Inner>,
    marker: PhantomData<fn() -> T>,
}

impl<T> Clone for MultiplexingStream<T> {
    fn clone(&self) -> Self {
        Self {
            inner: Arc::clone(&self.inner),
            marker: PhantomData,
        }
    }
}

impl<T> MultiplexingStream<T>
where
    T: AsyncRead + AsyncWrite + Unpin + Send + 'static,
{
    /// Establishes a multiplexing connection, performing the v1 or v2 handshake when needed.
    pub async fn connect(transport: T, options: Options) -> Result<Self, Error> {
        validate_options(&options)?;
        let (mut reader, mut writer) = split(transport);
        let local_random = write_handshake(&mut writer, options.protocol_version).await?;
        let local_is_odd =
            read_handshake(&mut reader, options.protocol_version, local_random).await?;
        Ok(Self::start(reader, writer, options, local_is_odd))
    }

    /// Creates a protocol v3 multiplexor without an initial handshake.
    ///
    /// Use [`connect`](Self::connect) for protocol versions 1 and 2.
    pub fn create(transport: T, options: Options) -> Result<Self, Error> {
        validate_options(&options)?;
        if options.protocol_version != ProtocolVersion::V3 {
            return Err(Error::Protocol(
                "immediate construction is only supported by protocol v3; use connect".into(),
            ));
        }
        let (reader, writer) = split(transport);
        Ok(Self::start(reader, writer, options, None))
    }

    fn start<R, W>(reader: R, writer: W, options: Options, local_is_odd: Option<bool>) -> Self
    where
        R: AsyncRead + Unpin + Send + 'static,
        W: AsyncWrite + Unpin + Send + 'static,
    {
        let (outbound, outbound_rx) = mpsc::unbounded_channel();
        let inner = Arc::new(Inner {
            options: options.clone(),
            local_is_odd,
            next_channel_id: Mutex::new(options.seeded_channels.len() as u64),
            channels: Mutex::new(HashMap::new()),
            offers: Mutex::new(HashMap::new()),
            offers_changed: Notify::new(),
            outbound,
            completion: Completion::default(),
            writer_completion: Completion::default(),
            remaining_window_growth_budget: Mutex::new(options.max_total_channel_receive_window),
        });

        for (id, channel_options) in options.seeded_channels.iter().enumerate() {
            let window = channel_window(channel_options, options.default_receive_window)
                .expect("validated seeded channel window");
            let state = inner.new_channel(
                ChannelId {
                    id: id as u64,
                    source: ChannelSource::Seeded,
                },
                String::new(),
                window,
                Some(window),
            );
            inner
                .channels
                .lock()
                .expect("channel map mutex poisoned")
                .insert(state.id, state);
        }

        let writer_inner = Arc::clone(&inner);
        tokio::spawn(async move {
            writer_loop(writer, outbound_rx, writer_inner).await;
        });
        let reader_inner = Arc::clone(&inner);
        tokio::spawn(async move {
            reader_loop(reader, reader_inner).await;
        });

        Self {
            inner,
            marker: PhantomData,
        }
    }

    /// Offers a named channel and waits for the remote endpoint to accept it.
    pub async fn offer_channel(
        &self,
        name: impl Into<String>,
        options: Option<ChannelOptions>,
    ) -> Result<Channel, Error> {
        let name = name.into();
        let window = channel_window(
            &options.unwrap_or_default(),
            self.inner.options.default_receive_window,
        )?;
        let id = self.inner.next_local_channel_id()?;
        let state = self.inner.new_channel(id, name.clone(), window, None);
        self.inner.insert_channel(Arc::clone(&state))?;
        let payload = encode_offer(self.inner.options.protocol_version, &name, window)?;
        self.inner.send(Frame {
            code: Code::Offer,
            channel: Some(id),
            payload,
        })?;
        state.wait_accepted().await?;
        state.take_channel()
    }

    /// Creates an anonymous channel without waiting for remote acceptance.
    ///
    /// The peer can accept it with [`accept_channel_by_id`](Self::accept_channel_by_id).
    pub fn create_channel(&self, options: Option<ChannelOptions>) -> Result<Channel, Error> {
        let window = channel_window(
            &options.unwrap_or_default(),
            self.inner.options.default_receive_window,
        )?;
        let id = self.inner.next_local_channel_id()?;
        let state = self.inner.new_channel(id, String::new(), window, None);
        self.inner.insert_channel(Arc::clone(&state))?;
        let payload = encode_offer(self.inner.options.protocol_version, "", window)?;
        self.inner.send(Frame {
            code: Code::Offer,
            channel: Some(id),
            payload,
        })?;
        state.take_channel()
    }

    /// Waits for and accepts the first remote channel offered with `name`.
    pub async fn accept_channel(
        &self,
        name: impl AsRef<str>,
        options: Option<ChannelOptions>,
    ) -> Result<Channel, Error> {
        let name = name.as_ref().to_owned();
        loop {
            let observed = self.inner.offers_changed.notified();
            if let Some(state) = self.inner.take_offer(&name) {
                return self.accept_state(state, options.clone()).await;
            }
            if let Some(result) = self.inner.completion.result() {
                return result.and(Err(Error::Closed("multiplexing stream closed".into())));
            }
            observed.await;
        }
    }

    /// Accepts an already offered anonymous channel by its numeric ID.
    ///
    /// In protocol v3, use [`accept_seeded_channel`](Self::accept_seeded_channel)
    /// for a seeded channel because its source is distinct.
    pub async fn accept_channel_by_id(
        &self,
        id: u64,
        options: Option<ChannelOptions>,
    ) -> Result<Channel, Error> {
        let state = self
            .inner
            .get_channel(ChannelId {
                id,
                source: ChannelSource::Remote,
            })
            .ok_or_else(|| Error::Protocol(format!("no remotely offered channel with ID {id}")))?;
        self.inner.remove_offer(&state);
        self.accept_state(state, options).await
    }

    /// Accepts a v3 seeded channel configured at construction time.
    pub fn accept_seeded_channel(&self, id: u64) -> Result<Channel, Error> {
        if self.inner.options.protocol_version != ProtocolVersion::V3 {
            return Err(Error::Protocol(
                "seeded channels require protocol v3".into(),
            ));
        }
        let state = self
            .inner
            .get_channel(ChannelId {
                id,
                source: ChannelSource::Seeded,
            })
            .ok_or_else(|| Error::Protocol(format!("no seeded channel with ID {id}")))?;
        state.accept_seeded()?;
        state.take_channel()
    }

    /// Returns when the transport terminates or fails.
    pub async fn completion(&self) -> Result<(), Error> {
        self.inner.completion.wait().await
    }

    /// Stops the multiplexor and closes all pending operations.
    pub async fn shutdown(&self) -> Result<(), Error> {
        self.inner.fail(Error::Closed(
            "multiplexing stream shut down locally".into(),
        ));
        self.inner.writer_completion.wait().await
    }

    async fn accept_state(
        &self,
        state: Arc<ChannelState>,
        options: Option<ChannelOptions>,
    ) -> Result<Channel, Error> {
        let window = channel_window(
            &options.unwrap_or_default(),
            self.inner.options.default_receive_window,
        )?;
        state.accept_remote_offer(window)?;
        let payload = encode_acceptance(self.inner.options.protocol_version, window);
        self.inner.send(Frame {
            code: Code::OfferAccepted,
            channel: Some(state.id),
            payload,
        })?;
        state.take_channel()
    }
}

fn validate_options(options: &Options) -> Result<(), Error> {
    validate_window(options.default_receive_window)?;
    validate_window(options.max_channel_receive_window)?;
    if options.max_channel_receive_window < options.default_receive_window {
        return Err(Error::Protocol(
            "maximum channel receive window must be at least the default receive window".into(),
        ));
    }
    if options.protocol_version != ProtocolVersion::V3 && !options.seeded_channels.is_empty() {
        return Err(Error::Protocol(
            "seeded channels require protocol v3".into(),
        ));
    }
    for channel_options in &options.seeded_channels {
        validate_window(channel_window(
            channel_options,
            options.default_receive_window,
        )?)?;
    }
    Ok(())
}

fn channel_window(options: &ChannelOptions, default: usize) -> Result<usize, Error> {
    let window = options.receive_window.unwrap_or(default);
    validate_window(window)?;
    Ok(window)
}

fn validate_window(window: usize) -> Result<(), Error> {
    if window == 0 || window > MAX_RECEIVE_WINDOW {
        return Err(Error::Protocol(format!(
            "receive window must be between 1 and {MAX_RECEIVE_WINDOW}"
        )));
    }
    Ok(())
}

struct Inner {
    options: Options,
    local_is_odd: Option<bool>,
    next_channel_id: Mutex<u64>,
    channels: Mutex<HashMap<ChannelId, Arc<ChannelState>>>,
    offers: Mutex<HashMap<String, VecDeque<Arc<ChannelState>>>>,
    offers_changed: Notify,
    outbound: mpsc::UnboundedSender<Outbound>,
    completion: Completion,
    writer_completion: Completion,
    remaining_window_growth_budget: Mutex<usize>,
}

impl Inner {
    fn new_channel(
        self: &Arc<Self>,
        id: ChannelId,
        name: String,
        local_window: usize,
        remote_window: Option<usize>,
    ) -> Arc<ChannelState> {
        let (application, multiplexor) = tokio::io::duplex(1);
        let state = Arc::new(ChannelState {
            id,
            name,
            protocol: self.options.protocol_version,
            application: Mutex::new(Some(application)),
            local_window: Mutex::new(local_window),
            remote_credit: Mutex::new(RemoteCredit {
                window: remote_window,
                filled: 0,
                reading_completed: false,
            }),
            remote_credit_changed: Notify::new(),
            inbound: Mutex::new(Inbound::default()),
            inbound_changed: Notify::new(),
            window_tuning: Mutex::new(WindowTuning::default()),
            acceptance: Completion::default(),
            completion: Completion::default(),
            pump_stop: Completion::default(),
            lifecycle: Mutex::new(Lifecycle::default()),
            owner: Arc::downgrade(self),
            outbound: self.outbound.clone(),
        });
        start_channel_pumps(Arc::clone(&state), multiplexor);
        state
    }

    fn next_local_channel_id(&self) -> Result<ChannelId, Error> {
        let mut next = self
            .next_channel_id
            .lock()
            .expect("channel ID mutex poisoned");
        let id = match self.local_is_odd {
            Some(is_odd) => {
                let candidate = if *next == 0 {
                    if is_odd {
                        1
                    } else {
                        2
                    }
                } else {
                    *next
                };
                *next = candidate
                    .checked_add(2)
                    .ok_or_else(|| Error::Protocol("channel ID space exhausted".into()))?;
                candidate
            }
            None => {
                *next = next
                    .checked_add(1)
                    .ok_or_else(|| Error::Protocol("channel ID space exhausted".into()))?;
                *next
            }
        };
        Ok(ChannelId {
            id,
            source: ChannelSource::Local,
        })
    }

    fn insert_channel(&self, channel: Arc<ChannelState>) -> Result<(), Error> {
        let mut channels = self.channels.lock().expect("channel map mutex poisoned");
        if channels.insert(channel.id, channel).is_some() {
            return Err(Error::Protocol("duplicate channel ID".into()));
        }
        Ok(())
    }

    fn get_channel(&self, id: ChannelId) -> Option<Arc<ChannelState>> {
        self.channels
            .lock()
            .expect("channel map mutex poisoned")
            .get(&id)
            .cloned()
    }

    fn take_offer(&self, name: &str) -> Option<Arc<ChannelState>> {
        let mut offers = self.offers.lock().expect("offers mutex poisoned");
        let queue = offers.get_mut(name)?;
        let state = queue.pop_front();
        if queue.is_empty() {
            offers.remove(name);
        }
        state
    }

    fn remove_offer(&self, target: &Arc<ChannelState>) {
        self.remove_offer_by_id(&target.name, target.id);
    }

    fn remove_offer_by_id(&self, name: &str, id: ChannelId) {
        let mut offers = self.offers.lock().expect("offers mutex poisoned");
        if let Some(queue) = offers.get_mut(name) {
            queue.retain(|entry| entry.id != id);
            if queue.is_empty() {
                offers.remove(name);
            }
        }
    }

    fn remove_channel(&self, id: ChannelId) {
        let removed = self
            .channels
            .lock()
            .expect("channel map mutex poisoned")
            .remove(&id);
        if let Some(channel) = removed {
            self.remove_offer_by_id(&channel.name, id);
        }
    }

    fn send(&self, frame: Frame) -> Result<(), Error> {
        if let Some(result) = self.completion.result() {
            return result.and(Err(Error::Closed("multiplexing stream closed".into())));
        }
        self.outbound
            .send(Outbound::Frame(frame))
            .map_err(|_| Error::Closed("multiplexing writer stopped".into()))
    }

    fn try_reserve_window_growth(&self, bytes: usize) -> bool {
        let mut budget = self
            .remaining_window_growth_budget
            .lock()
            .expect("window growth budget mutex poisoned");
        if *budget < bytes {
            return false;
        }
        *budget -= bytes;
        true
    }

    fn release_window_growth(&self, bytes: usize) {
        if bytes == 0 {
            return;
        }
        let mut budget = self
            .remaining_window_growth_budget
            .lock()
            .expect("window growth budget mutex poisoned");
        *budget = budget
            .checked_add(bytes)
            .expect("window growth budget exceeds configured maximum");
    }

    fn fail(&self, error: Error) {
        if !self.completion.complete(Err(error.clone())) {
            return;
        }
        let channels = self
            .channels
            .lock()
            .expect("channel map mutex poisoned")
            .values()
            .cloned()
            .collect::<Vec<_>>();
        for channel in channels {
            channel.mark_terminated(error.clone());
        }
        self.offers_changed.notify_waiters();
        let _ = self.outbound.send(Outbound::Shutdown);
    }
}

enum Outbound {
    Frame(Frame),
    Shutdown,
}

async fn writer_loop<W>(
    mut writer: W,
    mut receiver: mpsc::UnboundedReceiver<Outbound>,
    inner: Arc<Inner>,
) where
    W: AsyncWrite + Unpin,
{
    let result = loop {
        let Some(message) = receiver.recv().await else {
            break Ok(());
        };
        match message {
            Outbound::Frame(frame) => {
                if inner.completion.result().is_some() {
                    break Ok(());
                }
                if let Err(error) =
                    write_frame(&mut writer, inner.options.protocol_version, &frame).await
                {
                    inner.fail(error.clone());
                    break Err(error);
                }
            }
            Outbound::Shutdown => break Ok(()),
        }
    };
    let result = match writer.shutdown().await {
        Ok(()) => result,
        Err(error) => Err(Error::from(error)),
    };
    if let Err(error) = &result {
        inner.fail(error.clone());
    }
    inner.writer_completion.complete(result);
}

async fn reader_loop<R>(mut reader: R, inner: Arc<Inner>)
where
    R: AsyncRead + Unpin,
{
    loop {
        let frame = tokio::select! {
            result = read_frame(
                &mut reader,
                inner.options.protocol_version,
                inner.local_is_odd,
            ) => result,
            _ = inner.completion.wait() => return,
        };
        let frame = match frame {
            Ok(Some(frame)) => frame,
            Ok(None) => {
                inner.fail(Error::Closed("transport reached end of stream".into()));
                return;
            }
            Err(error) => {
                inner.fail(error);
                return;
            }
        };
        if let Err(error) = dispatch_frame(&inner, frame) {
            inner.fail(error);
            return;
        }
    }
}

fn dispatch_frame(inner: &Arc<Inner>, frame: Frame) -> Result<(), Error> {
    let id = frame.channel;
    match frame.code {
        Code::Offer => {
            let id = id.ok_or_else(|| Error::Protocol("offer has no channel ID".into()))?;
            if id.source != ChannelSource::Remote {
                return Err(Error::Protocol(
                    "offer did not originate from the peer".into(),
                ));
            }
            let (name, remote_window) =
                futures_decode_offer(inner.options.protocol_version, &frame.payload)?;
            let remote_window = if inner.options.protocol_version == ProtocolVersion::V1 {
                None
            } else {
                Some(
                    remote_window
                        .ok_or_else(|| Error::Protocol("offer lacks receive window".into()))?,
                )
            };
            if let Some(window) = remote_window {
                validate_window(window)?;
            }
            let state = inner.new_channel(
                id,
                name.clone(),
                inner.options.default_receive_window,
                remote_window,
            );
            inner.insert_channel(Arc::clone(&state))?;
            inner
                .offers
                .lock()
                .expect("offers mutex poisoned")
                .entry(name)
                .or_default()
                .push_back(state);
            inner.offers_changed.notify_waiters();
        }
        Code::OfferAccepted => {
            let id = id.ok_or_else(|| Error::Protocol("acceptance has no channel ID".into()))?;
            if id.source != ChannelSource::Local {
                return Err(Error::Protocol("peer accepted a channel it offered".into()));
            }
            let Some(state) = inner.get_channel(id) else {
                return Ok(());
            };
            let remote_window = decode_acceptance(inner.options.protocol_version, &frame.payload)?;
            if inner.options.protocol_version != ProtocolVersion::V1 {
                validate_window(
                    remote_window
                        .ok_or_else(|| Error::Protocol("acceptance lacks receive window".into()))?,
                )?;
            }
            state.on_accepted(remote_window)?;
        }
        Code::Content => {
            let id = id.ok_or_else(|| Error::Protocol("content has no channel ID".into()))?;
            let Some(state) = inner.get_channel(id) else {
                return Ok(());
            };
            state.on_content(frame.payload)?;
        }
        Code::ContentProcessed => {
            let id =
                id.ok_or_else(|| Error::Protocol("processed frame has no channel ID".into()))?;
            let Some(state) = inner.get_channel(id) else {
                return Ok(());
            };
            state.on_processed(decode_processed(&frame.payload)?)?;
        }
        Code::ContentWritingCompleted => {
            let id =
                id.ok_or_else(|| Error::Protocol("write completion has no channel ID".into()))?;
            let Some(state) = inner.get_channel(id) else {
                return Ok(());
            };
            state.on_remote_writing_completed();
        }
        Code::ContentReadingCompleted => {
            let id =
                id.ok_or_else(|| Error::Protocol("read completion has no channel ID".into()))?;
            let Some(state) = inner.get_channel(id) else {
                return Ok(());
            };
            state.on_remote_reading_completed();
        }
        Code::ChannelWindowAdjust => {
            if inner.options.protocol_version == ProtocolVersion::V1 {
                return Ok(());
            }
            let id =
                id.ok_or_else(|| Error::Protocol("window adjustment has no channel ID".into()))?;
            let Some(state) = inner.get_channel(id) else {
                return Ok(());
            };
            state.on_window_adjust(decode_window_adjust(&frame.payload)?)?;
        }
        Code::ChannelWindowGrowthRequest => {
            if inner.options.protocol_version == ProtocolVersion::V1 {
                return Ok(());
            }
            let id = id
                .ok_or_else(|| Error::Protocol("window growth request has no channel ID".into()))?;
            let Some(state) = inner.get_channel(id) else {
                return Ok(());
            };
            state.on_window_growth_requested();
        }
        Code::ChannelTerminated => {
            let id = id.ok_or_else(|| Error::Protocol("termination has no channel ID".into()))?;
            let Some(state) = inner.get_channel(id) else {
                return Ok(());
            };
            let error = if inner.options.protocol_version == ProtocolVersion::V1 {
                None
            } else {
                decode_error(&frame.payload)?
            };
            if let Some(error) = error {
                state.mark_terminated(Error::Remote(error));
            } else {
                state.mark_closed("channel terminated by remote endpoint");
            }
        }
    }
    Ok(())
}

fn futures_decode_offer(
    version: ProtocolVersion,
    payload: &[u8],
) -> Result<(String, Option<usize>), Error> {
    // Offer payloads are self-contained. The async reader parameter in the
    // formatter is unnecessary for their bounded in-memory representation.
    if version == ProtocolVersion::V1 {
        return String::from_utf8(payload.to_vec())
            .map(|name| (name, None))
            .map_err(|_| Error::Protocol("v1 offer name is not UTF-8".into()));
    }
    decode_offer_payload(payload)
}

fn decode_offer_payload(payload: &[u8]) -> Result<(String, Option<usize>), Error> {
    // Reuse the wire parser through a small, isolated Tokio in-memory stream.
    // This branch is synchronous because an offer payload cannot exceed 20 KiB.
    let mut cursor = OfferCursor {
        data: payload,
        offset: 0,
    };
    let count = cursor.array()?;
    if count == 0 {
        return Err(Error::Protocol("offer payload has too few elements".into()));
    }
    let name = cursor.string()?;
    let window = if count > 1 {
        Some(cursor.usize()?)
    } else {
        None
    };
    if cursor.offset != cursor.data.len() {
        return Err(Error::Protocol("offer payload has trailing data".into()));
    }
    Ok((name, window))
}

#[derive(Default)]
struct Completion {
    value: Mutex<Option<Result<(), Error>>>,
    changed: Notify,
}

impl Completion {
    fn complete(&self, result: Result<(), Error>) -> bool {
        let mut value = self.value.lock().expect("completion mutex poisoned");
        if value.is_some() {
            return false;
        }
        *value = Some(result);
        drop(value);
        self.changed.notify_waiters();
        true
    }

    fn result(&self) -> Option<Result<(), Error>> {
        self.value
            .lock()
            .expect("completion mutex poisoned")
            .clone()
    }

    async fn wait(&self) -> Result<(), Error> {
        loop {
            let observed = self.changed.notified();
            if let Some(result) = self.result() {
                return result;
            }
            observed.await;
        }
    }
}

pub(crate) struct ChannelState {
    pub(crate) id: ChannelId,
    pub(crate) name: String,
    protocol: ProtocolVersion,
    application: Mutex<Option<DuplexStream>>,
    local_window: Mutex<usize>,
    remote_credit: Mutex<RemoteCredit>,
    remote_credit_changed: Notify,
    inbound: Mutex<Inbound>,
    inbound_changed: Notify,
    window_tuning: Mutex<WindowTuning>,
    acceptance: Completion,
    completion: Completion,
    pump_stop: Completion,
    lifecycle: Mutex<Lifecycle>,
    owner: Weak<Inner>,
    outbound: mpsc::UnboundedSender<Outbound>,
}

#[derive(Default)]
struct Lifecycle {
    terminal: bool,
    local_read_closed: bool,
    local_writing_completed: bool,
    remote_writing_completed: bool,
}

struct RemoteCredit {
    window: Option<usize>,
    filled: usize,
    reading_completed: bool,
}

#[derive(Default)]
struct WindowTuning {
    growth_request_outstanding: bool,
    growth_request_pending: bool,
    application_read_pending: bool,
    refusals: u8,
    stalls_since_request: usize,
    reserved_growth: usize,
}

#[derive(Default)]
struct Inbound {
    queued: VecDeque<Vec<u8>>,
    unprocessed: usize,
    remote_writing_completed: bool,
}

impl ChannelState {
    fn take_channel(self: &Arc<Self>) -> Result<Channel, Error> {
        let io = self
            .application
            .lock()
            .expect("channel application mutex poisoned")
            .take()
            .ok_or_else(|| Error::Protocol("channel was already accepted locally".into()))?;
        Ok(Channel {
            state: Arc::clone(self),
            io: Some(io),
        })
    }

    fn accept_remote_offer(&self, window: usize) -> Result<(), Error> {
        *self
            .local_window
            .lock()
            .expect("local window mutex poisoned") = window;
        self.acceptance
            .complete(Ok(()))
            .then_some(())
            .ok_or_else(|| Error::Protocol("channel was already accepted or terminated".into()))
    }

    fn accept_seeded(&self) -> Result<(), Error> {
        self.acceptance
            .complete(Ok(()))
            .then_some(())
            .ok_or_else(|| {
                Error::Protocol("seeded channel was already accepted or terminated".into())
            })
    }

    fn on_accepted(&self, remote_window: Option<usize>) -> Result<(), Error> {
        if self.protocol != ProtocolVersion::V1 {
            let window = remote_window
                .ok_or_else(|| Error::Protocol("acceptance lacks receive window".into()))?;
            let mut credit = self
                .remote_credit
                .lock()
                .expect("remote credit mutex poisoned");
            credit.window = Some(window);
            drop(credit);
            self.remote_credit_changed.notify_waiters();
        }
        if !self.acceptance.complete(Ok(())) {
            return Err(Error::Protocol("duplicate channel acceptance".into()));
        }
        Ok(())
    }

    async fn wait_accepted(&self) -> Result<(), Error> {
        self.acceptance.wait().await
    }

    fn on_content(&self, payload: Vec<u8>) -> Result<(), Error> {
        if self.is_terminal() {
            return Ok(());
        }
        if payload.is_empty() {
            return Ok(());
        }
        let mut inbound = self.inbound.lock().expect("inbound mutex poisoned");
        if self.protocol != ProtocolVersion::V1 {
            let window = *self
                .local_window
                .lock()
                .expect("local window mutex poisoned");
            let available = window.saturating_sub(inbound.unprocessed);
            if payload.len() > available {
                return Err(Error::Protocol(format!(
                    "peer exceeded channel receive window: {} bytes available, {} sent",
                    available,
                    payload.len()
                )));
            }
        }
        inbound.unprocessed += payload.len();
        inbound.queued.push_back(payload);
        drop(inbound);
        self.inbound_changed.notify_one();
        Ok(())
    }

    fn on_processed(&self, bytes: usize) -> Result<(), Error> {
        if self.protocol == ProtocolVersion::V1 {
            return Err(Error::Protocol(
                "v1 does not support ContentProcessed".into(),
            ));
        }
        let mut credit = self
            .remote_credit
            .lock()
            .expect("remote credit mutex poisoned");
        if bytes > credit.filled {
            return Err(Error::Protocol(
                "peer acknowledged more content than was sent".into(),
            ));
        }
        credit.filled -= bytes;
        drop(credit);
        self.remote_credit_changed.notify_waiters();
        Ok(())
    }

    fn on_remote_writing_completed(&self) {
        let should_complete = {
            let mut lifecycle = self
                .lifecycle
                .lock()
                .expect("channel lifecycle mutex poisoned");
            if lifecycle.terminal || lifecycle.remote_writing_completed {
                return;
            }
            lifecycle.remote_writing_completed = true;
            lifecycle.local_writing_completed
        };
        let mut inbound = self.inbound.lock().expect("inbound mutex poisoned");
        inbound.remote_writing_completed = true;
        drop(inbound);
        self.inbound_changed.notify_waiters();
        if should_complete {
            self.complete_gracefully();
        }
    }

    fn on_remote_reading_completed(&self) {
        let mut credit = self
            .remote_credit
            .lock()
            .expect("remote credit mutex poisoned");
        credit.reading_completed = true;
        drop(credit);
        self.remote_credit_changed.notify_waiters();
    }

    fn on_window_growth_requested(&self) {
        if self.protocol == ProtocolVersion::V1 || self.is_terminal() {
            return;
        }

        let answer_request = {
            let mut tuning = self
                .window_tuning
                .lock()
                .expect("window tuning mutex poisoned");
            if tuning.application_read_pending {
                true
            } else {
                tuning.growth_request_outstanding = true;
                false
            }
        };
        if answer_request {
            self.answer_window_growth_request();
        }
    }

    fn on_window_adjust(&self, new_window: usize) -> Result<(), Error> {
        if self.protocol == ProtocolVersion::V1 {
            return Ok(());
        }
        validate_window(new_window)?;

        let grew = {
            let mut credit = self
                .remote_credit
                .lock()
                .expect("remote credit mutex poisoned");
            match credit.window {
                Some(current_window) if new_window > current_window => {
                    credit.window = Some(new_window);
                    true
                }
                _ => false,
            }
        };

        let mut tuning = self
            .window_tuning
            .lock()
            .expect("window tuning mutex poisoned");
        tuning.growth_request_pending = false;
        if grew {
            tuning.refusals = 0;
        } else {
            tuning.refusals = tuning.refusals.saturating_add(1).min(10);
        }
        drop(tuning);

        if grew {
            self.remote_credit_changed.notify_waiters();
        }
        Ok(())
    }

    pub(crate) fn set_application_read_pending(&self, pending: bool) {
        let answer_request = {
            let mut tuning = self
                .window_tuning
                .lock()
                .expect("window tuning mutex poisoned");
            tuning.application_read_pending = pending;
            if pending && tuning.growth_request_outstanding {
                tuning.growth_request_outstanding = false;
                true
            } else {
                false
            }
        };
        if answer_request {
            self.answer_window_growth_request();
        }
    }

    fn answer_window_growth_request(&self) {
        if self.protocol == ProtocolVersion::V1 || self.is_terminal() {
            return;
        }
        let Some(owner) = self.owner.upgrade() else {
            return;
        };

        let mut local_window = self
            .local_window
            .lock()
            .expect("local window mutex poisoned");
        let current_window = *local_window;
        let mut new_window = current_window;
        let proposed_window = current_window
            .saturating_mul(4)
            .min(owner.options.max_channel_receive_window)
            .min(MAX_RECEIVE_WINDOW);
        if proposed_window > current_window {
            let growth = proposed_window - current_window;
            if owner.try_reserve_window_growth(growth) {
                *local_window = proposed_window;
                self.window_tuning
                    .lock()
                    .expect("window tuning mutex poisoned")
                    .reserved_growth += growth;
                new_window = proposed_window;
                if self.is_terminal() {
                    drop(local_window);
                    self.release_reserved_window_growth();
                    return;
                }
            }
        }
        drop(local_window);

        let _ = self.send(Code::ChannelWindowAdjust, encode_window_adjust(new_window));
    }

    fn request_window_growth(&self) {
        if self.protocol == ProtocolVersion::V1 || self.is_terminal() {
            return;
        }

        let should_send = {
            let mut tuning = self
                .window_tuning
                .lock()
                .expect("window tuning mutex poisoned");
            if tuning.growth_request_pending {
                return;
            }

            tuning.stalls_since_request += 1;
            let threshold = 1_usize << tuning.refusals;
            if tuning.stalls_since_request < threshold {
                false
            } else {
                tuning.stalls_since_request = 0;
                tuning.growth_request_pending = true;
                true
            }
        };
        if should_send {
            let _ = self.send(Code::ChannelWindowGrowthRequest, Vec::new());
        }
    }

    pub(crate) fn content_consumed(&self, bytes: usize) {
        if self.protocol == ProtocolVersion::V1 || bytes == 0 || self.is_terminal() {
            return;
        }
        let mut inbound = self.inbound.lock().expect("inbound mutex poisoned");
        if bytes > inbound.unprocessed {
            drop(inbound);
            self.mark_terminated(Error::Protocol(
                "application consumed more bytes than were received".into(),
            ));
            return;
        }
        inbound.unprocessed -= bytes;
        drop(inbound);
        let _ = self.send(Code::ContentProcessed, encode_processed(bytes));
    }

    pub(crate) fn close_read(&self) -> Result<(), Error> {
        if self.protocol == ProtocolVersion::V1 {
            return Ok(());
        }
        let send_result = {
            let mut lifecycle = self
                .lifecycle
                .lock()
                .expect("channel lifecycle mutex poisoned");
            if lifecycle.terminal || lifecycle.local_read_closed {
                return Ok(());
            }
            lifecycle.local_read_closed = true;
            self.enqueue(Code::ContentReadingCompleted, Vec::new())
        };
        send_result
    }

    pub(crate) async fn terminate(&self, error: Option<&str>) -> Result<(), Error> {
        self.terminate_now(error)
    }

    pub(crate) fn drop_channel(&self) {
        let _ = self.terminate_now(None);
    }

    fn terminate_now(&self, error: Option<&str>) -> Result<(), Error> {
        let payload = if self.protocol == ProtocolVersion::V1 {
            Vec::new()
        } else {
            error.map_or_else(Vec::new, encode_error)
        };
        let send_result = {
            let mut lifecycle = self
                .lifecycle
                .lock()
                .expect("channel lifecycle mutex poisoned");
            if lifecycle.terminal {
                return Ok(());
            }
            lifecycle.terminal = true;
            self.enqueue(Code::ChannelTerminated, payload)
        };
        if let Err(error) = send_result {
            self.finish_terminal(Err(error.clone()), "channel terminated locally", true);
            return Err(error);
        }
        if let Some(message) = error {
            self.finish_terminal(
                Err(Error::Remote(message.into())),
                "channel terminated locally",
                true,
            );
        } else {
            self.finish_terminal(Ok(()), "channel terminated locally", true);
        }
        Ok(())
    }

    pub(crate) async fn completion(&self) -> Result<(), Error> {
        self.completion.wait().await
    }

    fn send(&self, code: Code, payload: Vec<u8>) -> Result<(), Error> {
        if let Some(owner) = self.owner.upgrade() {
            if owner.completion.result().is_some() {
                return Err(Error::Closed("multiplexing stream closed".into()));
            }
        }
        let lifecycle = self
            .lifecycle
            .lock()
            .expect("channel lifecycle mutex poisoned");
        if lifecycle.terminal {
            return Err(Error::Closed("channel terminated".into()));
        }
        self.enqueue(code, payload)
    }

    fn mark_terminated(&self, error: Error) {
        if !self.enter_terminal() {
            return;
        }
        self.finish_terminal(Err(error), "channel terminated", true);
    }

    fn mark_closed(&self, message: &str) {
        if !self.enter_terminal() {
            return;
        }
        self.finish_terminal(Ok(()), message, true);
    }

    fn complete_local_writing(&self) {
        let (should_complete, send_result) = {
            let mut lifecycle = self
                .lifecycle
                .lock()
                .expect("channel lifecycle mutex poisoned");
            if lifecycle.terminal || lifecycle.local_writing_completed {
                return;
            }
            lifecycle.local_writing_completed = true;
            (
                lifecycle.remote_writing_completed,
                self.enqueue(Code::ContentWritingCompleted, Vec::new()),
            )
        };
        if let Err(error) = send_result {
            self.mark_terminated(error);
            return;
        }
        if should_complete {
            self.complete_gracefully();
        }
    }

    fn complete_gracefully(&self) {
        let send_result = {
            let mut lifecycle = self
                .lifecycle
                .lock()
                .expect("channel lifecycle mutex poisoned");
            if lifecycle.terminal {
                return;
            }
            lifecycle.terminal = true;
            self.enqueue(Code::ChannelTerminated, Vec::new())
        };
        if send_result.is_err() {
            self.finish_terminal(
                Err(Error::Closed("multiplexing writer stopped".into())),
                "both channel writers completed",
                true,
            );
            return;
        }
        self.finish_terminal(Ok(()), "both channel writers completed", false);
    }

    fn enter_terminal(&self) -> bool {
        let mut lifecycle = self
            .lifecycle
            .lock()
            .expect("channel lifecycle mutex poisoned");
        if lifecycle.terminal {
            return false;
        }
        lifecycle.terminal = true;
        true
    }

    fn finish_terminal(&self, result: Result<(), Error>, closed_message: &str, stop_pumps: bool) {
        let acceptance_result = match &result {
            Ok(()) => Err(Error::Closed(closed_message.to_owned())),
            Err(error) => Err(error.clone()),
        };
        self.acceptance.complete(acceptance_result);
        self.completion.complete(result);
        self.release_reserved_window_growth();
        if stop_pumps {
            self.pump_stop.complete(Ok(()));
            self.stop_pumps();
        }
        if let Some(owner) = self.owner.upgrade() {
            owner.remove_channel(self.id);
        }
    }

    fn release_reserved_window_growth(&self) {
        let reserved_growth = {
            let mut tuning = self
                .window_tuning
                .lock()
                .expect("window tuning mutex poisoned");
            std::mem::take(&mut tuning.reserved_growth)
        };
        if let Some(owner) = self.owner.upgrade() {
            owner.release_window_growth(reserved_growth);
        }
    }

    fn stop_pumps(&self) {
        let mut inbound = self.inbound.lock().expect("inbound mutex poisoned");
        inbound.remote_writing_completed = true;
        drop(inbound);
        self.inbound_changed.notify_waiters();
        self.on_remote_reading_completed();
    }

    fn is_terminal(&self) -> bool {
        self.lifecycle
            .lock()
            .expect("channel lifecycle mutex poisoned")
            .terminal
    }

    fn is_local_read_closed(&self) -> bool {
        self.lifecycle
            .lock()
            .expect("channel lifecycle mutex poisoned")
            .local_read_closed
    }

    fn enqueue(&self, code: Code, payload: Vec<u8>) -> Result<(), Error> {
        self.outbound
            .send(Outbound::Frame(Frame {
                code,
                channel: Some(self.id),
                payload,
            }))
            .map_err(|_| Error::Closed("multiplexing writer stopped".into()))
    }

    async fn next_inbound(&self) -> Option<Vec<u8>> {
        loop {
            let observed = self.inbound_changed.notified();
            {
                let mut inbound = self.inbound.lock().expect("inbound mutex poisoned");
                if let Some(content) = inbound.queued.pop_front() {
                    return Some(content);
                }
                if inbound.remote_writing_completed {
                    return None;
                }
            }
            observed.await;
        }
    }

    async fn reserve_send_budget(&self, requested: usize) -> Option<usize> {
        if self.is_terminal() {
            return None;
        }
        if self.protocol == ProtocolVersion::V1 {
            return Some(requested);
        }
        loop {
            let observed = self.remote_credit_changed.notified();
            {
                let mut credit = self
                    .remote_credit
                    .lock()
                    .expect("remote credit mutex poisoned");
                if credit.reading_completed {
                    return None;
                }
                if let Some(window) = credit.window {
                    let remaining = window.saturating_sub(credit.filled);
                    if remaining != 0 {
                        let reserved = remaining.min(requested);
                        credit.filled += reserved;
                        return Some(reserved);
                    }
                }
            }
            self.request_window_growth();
            observed.await;
        }
    }
}

fn start_channel_pumps(state: Arc<ChannelState>, multiplexor: DuplexStream) {
    let (mut outgoing, mut incoming) = split(multiplexor);
    let write_state = Arc::clone(&state);
    tokio::spawn(async move {
        if write_state.id.source != ChannelSource::Seeded
            && write_state.wait_accepted().await.is_err()
        {
            return;
        }
        let mut buffer = vec![0_u8; MAX_FRAME_PAYLOAD];
        loop {
            let read_result = tokio::select! {
                result = outgoing.read(&mut buffer) => result,
                _ = write_state.pump_stop.wait() => return,
            };
            match read_result {
                Ok(0) => {
                    write_state.complete_local_writing();
                    break;
                }
                Ok(bytes_read) => {
                    let mut offset = 0;
                    while offset < bytes_read {
                        let requested = bytes_read - offset;
                        let send_length = tokio::select! {
                            budget = write_state.reserve_send_budget(requested) => budget,
                            _ = write_state.pump_stop.wait() => return,
                        };
                        let Some(send_length) = send_length else {
                            break;
                        };
                        let payload = buffer[offset..offset + send_length].to_vec();
                        if write_state.send(Code::Content, payload).is_err() {
                            return;
                        }
                        offset += send_length;
                    }
                }
                Err(error) => {
                    let _ = write_state.terminate(Some(&error.to_string())).await;
                    break;
                }
            }
        }
    });

    tokio::spawn(async move {
        loop {
            let content = tokio::select! {
                content = state.next_inbound() => content,
                _ = state.pump_stop.wait() => return,
            };
            let Some(content) = content else {
                let _ = incoming.shutdown().await;
                return;
            };
            let write_result = tokio::select! {
                result = incoming.write_all(&content) => result,
                _ = state.pump_stop.wait() => return,
            };
            if let Err(error) = write_result {
                if !state.is_local_read_closed() {
                    let _ = state.terminate(Some(&error.to_string())).await;
                }
                return;
            }
        }
    });
}

struct OfferCursor<'a> {
    data: &'a [u8],
    offset: usize,
}

impl<'a> OfferCursor<'a> {
    fn take(&mut self, count: usize) -> Result<&'a [u8], Error> {
        let end = self
            .offset
            .checked_add(count)
            .ok_or_else(|| Error::Protocol("offer MessagePack length overflows".into()))?;
        let value = self
            .data
            .get(self.offset..end)
            .ok_or_else(|| Error::Protocol("offer payload ends unexpectedly".into()))?;
        self.offset = end;
        Ok(value)
    }

    fn marker(&mut self) -> Result<u8, Error> {
        Ok(self.take(1)?[0])
    }

    fn array(&mut self) -> Result<usize, Error> {
        match self.marker()? {
            0x90..=0x9f => Ok(usize::from(self.data[self.offset - 1] & 0x0f)),
            0xdc => Ok(usize::from(u16::from_be_bytes(
                self.take(2)?.try_into().expect("fixed slice"),
            ))),
            0xdd => usize::try_from(u32::from_be_bytes(
                self.take(4)?.try_into().expect("fixed slice"),
            ))
            .map_err(|_| Error::Protocol("offer array length exceeds usize".into())),
            _ => Err(Error::Protocol("offer payload is not an array".into())),
        }
    }

    fn usize(&mut self) -> Result<usize, Error> {
        let marker = self.marker()?;
        let value = match marker {
            0x00..=0x7f => u64::from(marker),
            0xcc => u64::from(self.take(1)?[0]),
            0xcd => u64::from(u16::from_be_bytes(
                self.take(2)?.try_into().expect("fixed slice"),
            )),
            0xce => u64::from(u32::from_be_bytes(
                self.take(4)?.try_into().expect("fixed slice"),
            )),
            0xcf => u64::from_be_bytes(self.take(8)?.try_into().expect("fixed slice")),
            _ => {
                return Err(Error::Protocol(
                    "offer window is not unsigned integer".into(),
                ))
            }
        };
        usize::try_from(value).map_err(|_| Error::Protocol("offer window exceeds usize".into()))
    }

    fn string(&mut self) -> Result<String, Error> {
        let marker = self.marker()?;
        let length = match marker {
            0xa0..=0xbf => usize::from(marker & 0x1f),
            0xd9 => usize::from(self.take(1)?[0]),
            0xda => usize::from(u16::from_be_bytes(
                self.take(2)?.try_into().expect("fixed slice"),
            )),
            0xdb => usize::try_from(u32::from_be_bytes(
                self.take(4)?.try_into().expect("fixed slice"),
            ))
            .map_err(|_| Error::Protocol("offer name length exceeds usize".into()))?,
            _ => return Err(Error::Protocol("offer name is not a string".into())),
        };
        String::from_utf8(self.take(length)?.to_vec())
            .map_err(|_| Error::Protocol("offer name is not UTF-8".into()))
    }
}
