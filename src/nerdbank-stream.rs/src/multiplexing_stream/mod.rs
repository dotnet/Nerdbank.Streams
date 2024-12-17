mod channel_source;
mod codec_v3;
mod control_code;
mod error;
mod frame;
mod message_codec;
mod options;

use log::info;
use std::{
    cmp::max,
    collections::{HashMap, VecDeque},
    sync::Arc,
};

use channel_source::ChannelSource;
use codec_v3::MultiplexingFrameV3Codec;
use error::MultiplexingStreamError;
pub use frame::QualifiedChannelId;
use frame::{AcceptanceParameters, ContentProcessed, Message, OfferParameters};
use futures::{
    stream::{SplitSink, SplitStream},
    StreamExt,
};
use futures_util::SinkExt;
use message_codec::MultiplexingMessageCodec;
use options::ChannelOptions;
use tokio::{
    io::{duplex, AsyncWriteExt, DuplexStream},
    sync::{mpsc, oneshot, Mutex},
    task::JoinHandle,
};

pub use options::Options;
use tokio_util::codec::Framed;

#[derive(Copy, Clone, Debug)]
pub enum ProtocolMajorVersion {
    //V1,
    //V2,
    V3,
}

struct ChannelCore {
    /// The duplex used by the mxstream processor to send and receive messages on the channel.
    central_duplex: DuplexStream,
}

struct MultiplexingStreamCore {
    /// A synchronous drop box for transmitting messages where the caller has no interest in awaiting transmission.
    message_sink: mpsc::UnboundedSender<Message>,
    processor_task: JoinHandle<Result<(), MultiplexingStreamError>>,
    open_channels: HashMap<QualifiedChannelId, Arc<Mutex<ChannelCore>>>,
    offered_channels_by_them_by_name: HashMap<String, VecDeque<PendingInboundChannel>>,
    offered_channels_by_them_by_id: HashMap<QualifiedChannelId, PendingInboundChannel>,
    offered_channels_by_us: HashMap<QualifiedChannelId, PendingOutboundChannel>,
    accepting_channels: HashMap<String, VecDeque<AwaitingChannel>>,
}

impl MultiplexingStreamCore {
    fn new(writer: SplitSink<Framed<DuplexStream, MultiplexingMessageCodec>, Message>) -> Self {
        let (sink, processor) = mpsc::unbounded_channel::<Message>();
        let processor_task = tokio::spawn(Self::outbound_message_processor(processor, writer));
        Self {
            message_sink: sink,
            processor_task,
            open_channels: HashMap::new(),
            offered_channels_by_them_by_id: HashMap::new(),
            offered_channels_by_them_by_name: HashMap::new(),
            offered_channels_by_us: HashMap::new(),
            accepting_channels: HashMap::new(),
        }
    }

    async fn outbound_message_processor(
        mut queue: mpsc::UnboundedReceiver<Message>,
        mut stream: SplitSink<Framed<DuplexStream, MultiplexingMessageCodec>, Message>,
    ) -> Result<(), MultiplexingStreamError> {
        let mut outgoing_messages = Vec::new();
        while queue.recv_many(&mut outgoing_messages, 5).await > 0 {
            for message in outgoing_messages.drain(..) {
                info!("Sending {}", message);
                stream.feed(message).await?;
            }

            stream.flush().await?;
        }

        Ok(())
    }

    async fn on_offer(
        &mut self,
        self_mutex: &Arc<Mutex<Self>>,
        channel_id: QualifiedChannelId,
        offer: OfferParameters,
    ) -> Result<(), MultiplexingStreamError> {
        if !offer.name.is_empty() {
            if let Some(queue) = self.accepting_channels.get_mut(&offer.name) {
                let mut success = false;
                while let Some(waiter) = queue.pop_front() {
                    let acceptance_parameters = AcceptanceParameters {
                        remote_window_size: Some(
                            waiter.options.channel_receiving_window_size as u64,
                        ),
                    };
                    let max_buf_size = offer
                        .remote_window_size
                        .map_or(waiter.options.channel_receiving_window_size, |r| {
                            max(r as usize, waiter.options.channel_receiving_window_size)
                        });
                    let channel = Channel::new(
                        self_mutex.clone(),
                        self.message_sink.clone(),
                        channel_id,
                        max_buf_size,
                    );
                    self.open_channels.insert(channel_id, channel.core.clone());
                    if waiter.matured_signal.send(channel).is_ok() {
                        self.message_sink
                            .send(Message::Acceptance(channel_id, acceptance_parameters))?;
                        success = true;
                        break;
                    };
                }

                // Recover memory in the map from an empty queue.
                if queue.is_empty() {
                    self.accepting_channels.remove(&offer.name);
                }

                if success {
                    return Ok(());
                }
            }
        }

        // No one is waiting on this offer, so queue it for future interested folks.
        let pending_offer_queue = self
            .offered_channels_by_them_by_name
            .entry(offer.name.clone())
            .or_insert_with(|| VecDeque::new());

        let pending_offer = PendingInboundChannel {
            offer_parameters: offer,
            id: channel_id,
        };

        pending_offer_queue.push_back(pending_offer);

        // Raise on_channel_offered event.

        Ok(())
    }

    async fn on_offer_accepted(
        &mut self,
        self_mutex: &Arc<Mutex<Self>>,
        channel_id: QualifiedChannelId,
        acceptance: AcceptanceParameters,
    ) -> Result<(), MultiplexingStreamError> {
        if let Some(pending_channel) = self.offered_channels_by_us.remove(&channel_id) {
            let max_buf_size = acceptance
                .remote_window_size
                .map_or(pending_channel.local_window_size, |r| {
                    max(r as usize, pending_channel.local_window_size)
                });
            let channel = Channel::new(
                self_mutex.clone(),
                self.message_sink.clone(),
                channel_id,
                max_buf_size,
            );
            self.open_channels.insert(channel_id, channel.core.clone());
            if pending_channel.matured_signal.send(channel).is_err() {
                // The party that offered the channel in the first place is gone.
                // Terminate the channel.
                self.message_sink
                    .send(Message::ChannelTerminated(channel_id))?;
            }
            Ok(())
        } else {
            Err(MultiplexingStreamError::ProtocolViolation(format!("Remote party accepted offer for channel {}, but we have no record of having offered it.", channel_id)))
        }
    }

    fn on_content(
        &self,
        channel_id: QualifiedChannelId,
        payload: Vec<u8>,
    ) -> Result<(), MultiplexingStreamError> {
        todo!()
    }

    fn on_content_writing_completed(
        &self,
        channel_id: QualifiedChannelId,
    ) -> Result<(), MultiplexingStreamError> {
        todo!()
    }

    async fn on_channel_terminated(
        &mut self,
        channel_id: QualifiedChannelId,
        _payload: Vec<u8>,
    ) -> Result<(), MultiplexingStreamError> {
        if let Some(channel) = self.open_channels.remove(&channel_id) {
            let mut channel = channel.lock().await;
            channel.central_duplex.shutdown().await?;
            self.message_sink
                .send(Message::ChannelTerminated(channel_id));
        }

        Ok(())
    }

    fn on_content_processed(
        &self,
        channel_id: QualifiedChannelId,
        payload: ContentProcessed,
    ) -> Result<(), MultiplexingStreamError> {
        todo!()
    }

    fn notify_channel_dropped(
        id: QualifiedChannelId,
        core: Arc<Mutex<MultiplexingStreamCore>>,
    ) -> () {
        tokio::spawn(async move {
            let mut core_stream = core.lock().await;

            // Ignore any errors
            let _ = core_stream.terminate_channel(id).await;
        });
    }

    async fn terminate_channel(
        &mut self,
        id: QualifiedChannelId,
    ) -> Result<bool, MultiplexingStreamError> {
        if let Some(channel_core) = self.open_channels.remove(&id) {
            let mut channel_core = channel_core.lock().await;
            channel_core.central_duplex.shutdown().await?;
            let _ = self.message_sink.send(Message::ChannelTerminated(id));
            Ok(true)
        } else {
            Ok(false)
        }
    }
}

// TODO: dropping this should send a channel closed notice to the remote party
pub struct Channel {
    stream_core: Arc<Mutex<MultiplexingStreamCore>>,
    core: Arc<Mutex<ChannelCore>>,
    message_sink: mpsc::UnboundedSender<Message>,
    id: QualifiedChannelId,

    /// The duplex to expose to the user of the channel.
    channel_duplex: DuplexStream,
}

impl Drop for Channel {
    fn drop(&mut self) {
        MultiplexingStreamCore::notify_channel_dropped(self.id, self.stream_core.clone());
    }
}

impl Channel {
    fn new(
        stream_core: Arc<Mutex<MultiplexingStreamCore>>,
        message_sink: mpsc::UnboundedSender<Message>,
        id: QualifiedChannelId,
        max_buf_size: usize,
    ) -> Self {
        let (channel_duplex, central_duplex) = duplex(max_buf_size);
        let channel_core = ChannelCore { central_duplex };
        let core = Arc::new(Mutex::new(channel_core));
        Channel {
            core,
            message_sink,
            id,
            channel_duplex,
            stream_core,
        }
    }

    pub fn id(&self) -> QualifiedChannelId {
        self.id
    }

    pub fn duplex(&mut self) -> &mut DuplexStream {
        &mut self.channel_duplex
    }
}

// Kinds of pending channels:
// Offered by them:
//    (map-by-name->queue)
//       OfferParameters
//       id
//    (map-by-id)
//       OfferParameters
//       (id)
// Offered by us: (map-by-id)
//    OneTimeChannel<Channel>
// Acceptance pending by us: (map-by-name->queue)
//    OneTimeChannel<Channel>

enum PendingChannel {
    OfferedByThem(PendingInboundChannel),
    OfferedByUs(PendingOutboundChannel),
    AcceptingByUs(AwaitingChannel),
}

/// A channel offered by the remote party that we have not yet accepted.
struct PendingInboundChannel {
    offer_parameters: OfferParameters,
    id: QualifiedChannelId,
}

/// A channel we have offered that the remote party has not yet accepted.
struct PendingOutboundChannel {
    local_window_size: usize,
    matured_signal: oneshot::Sender<Channel>,
}

/// We're waiting to accept a channel by name that the remote party has not yet offered.
struct AwaitingChannel {
    matured_signal: oneshot::Sender<Channel>,
    options: ChannelOptions,
}

pub struct MultiplexingStream {
    core: Arc<Mutex<MultiplexingStreamCore>>,
    listening: Option<JoinHandle<Result<(), MultiplexingStreamError>>>,
    options: Options,
    next_unreserved_channel_id: u64,
}

pub fn create(duplex: DuplexStream) -> Result<MultiplexingStream, MultiplexingStreamError> {
    create_with_options(duplex, Options::default())
}

pub fn create_with_options(
    duplex: DuplexStream,
    options: Options,
) -> Result<MultiplexingStream, MultiplexingStreamError> {
    let next_unreserved_channel_id = options.seeded_channels.len() as u64;

    let framed = match options.protocol_major_version {
        ProtocolMajorVersion::V3 => Framed::new(
            duplex,
            MultiplexingMessageCodec::new(Box::new(MultiplexingFrameV3Codec::new())),
        ),
    };

    let (writer, reader) = framed.split();

    let core = MultiplexingStreamCore::new(writer);

    let mut mxstream = MultiplexingStream {
        core: Arc::new(Mutex::new(core)),
        options,
        next_unreserved_channel_id,
        listening: None,
    };
    mxstream.start_listening(reader)?;

    Ok(mxstream)
}

impl MultiplexingStream {
    pub fn create_channel(
        options: Option<ChannelOptions>,
    ) -> Result<PendingChannel, MultiplexingStreamError> {
        todo!()
    }

    pub fn reject_channel(id: u64) -> Result<(), MultiplexingStreamError> {
        todo!()
    }

    pub async fn offer_channel(
        &mut self,
        name: String,
        options: Option<ChannelOptions>,
    ) -> Result<Channel, MultiplexingStreamError> {
        let qualified_id = QualifiedChannelId {
            source: ChannelSource::Local,
            id: self.reserved_unused_channel_id(),
        };

        let local_window_size = options
            .clone()
            .map_or(self.options.default_channel_receiving_window_size, |o| {
                o.channel_receiving_window_size
            });
        let offer_parameters = OfferParameters {
            name: name.clone(),
            remote_window_size: Some(local_window_size as u64),
        };
        let message = Message::Offer(qualified_id, offer_parameters.clone());

        let (sender, receiver) = oneshot::channel();
        let pending_channel = PendingOutboundChannel {
            local_window_size,
            matured_signal: sender,
        };
        let mut core = self.core.lock().await;
        core.offered_channels_by_us
            .insert(qualified_id, pending_channel);
        core.message_sink.send(message)?;
        drop(core);

        Ok(receiver
            .await
            .map_err(|e| MultiplexingStreamError::ChannelConnectionFailure(e.to_string()))?)
    }

    pub fn accept_channel_by_id(
        &self,
        id: u64,
        options: Option<ChannelOptions>,
    ) -> Result<Channel, MultiplexingStreamError> {
        todo!()
    }

    pub async fn accept_channel_by_name(
        &self,
        name: String,
        options: Option<ChannelOptions>,
    ) -> Result<Channel, MultiplexingStreamError> {
        let mut core = self.core.lock().await;

        let options = options.unwrap_or_else(|| self.default_channel_options());
        let local_window_size = options.channel_receiving_window_size as u64;
        let acceptance_parameters = AcceptanceParameters {
            remote_window_size: Some(local_window_size),
        };

        // First see if there's an existing offer to accept.
        if let Some(vec) = core.offered_channels_by_them_by_name.get_mut(&name) {
            if let Some(pending_channel) = vec.pop_front() {
                let id = pending_channel.id;
                core.message_sink
                    .send(Message::Acceptance(id, acceptance_parameters))?;
                let max_buf_size = pending_channel
                    .offer_parameters
                    .remote_window_size
                    .map_or(local_window_size, |r| max(local_window_size, r));
                let channel = Channel::new(
                    self.core.clone(),
                    core.message_sink.clone(),
                    id,
                    max_buf_size as usize,
                );
                core.open_channels.insert(id, channel.core.clone());
                return Ok(channel);
            }
        }

        // Add our interest in this channel to the shared state
        let (sender, receiver) = oneshot::channel();
        let by_name_deque = core
            .accepting_channels
            .entry(name)
            .or_insert_with(|| VecDeque::new());
        let pending_channel = AwaitingChannel {
            matured_signal: sender,
            options,
        };
        by_name_deque.push_back(pending_channel);

        // Release our lock on the shared state and wait for an incoming offer to resolve the accept request.
        drop(core);
        Ok(receiver.await.map_err(|e| {
            MultiplexingStreamError::ChannelConnectionFailure(format!(
                "Failure while accepting a channel by name: {}",
                e
            ))
        })?)
    }

    fn default_channel_options(&self) -> ChannelOptions {
        ChannelOptions {
            channel_receiving_window_size: self.options.default_channel_receiving_window_size,
        }
    }

    fn reserved_unused_channel_id(&mut self) -> u64 {
        let channel_id = self.next_unreserved_channel_id;
        self.next_unreserved_channel_id += 1;
        channel_id
    }

    pub fn start_listening(
        &mut self,
        stream: SplitStream<Framed<DuplexStream, MultiplexingMessageCodec>>,
    ) -> Result<(), MultiplexingStreamError> {
        if self.listening.is_some() {
            return Err(MultiplexingStreamError::ListeningAlreadyStarted);
        }

        self.listening = Some(tokio::spawn(Self::listen(self.core.clone(), stream)));

        Ok(())
    }

    async fn listen(
        this: Arc<Mutex<MultiplexingStreamCore>>,
        mut stream: SplitStream<Framed<DuplexStream, MultiplexingMessageCodec>>,
    ) -> Result<(), MultiplexingStreamError> {
        loop {
            while let Some(message) = stream.next().await.transpose()? {
                let mut me = this.lock().await;
                info!("Received {}", message);
                match message {
                    frame::Message::Offer(channel_id, offer_parameters) => {
                        me.on_offer(&this, channel_id, offer_parameters).await?
                    }
                    frame::Message::Acceptance(channel_id, acceptance_parameters) => {
                        me.on_offer_accepted(&this, channel_id, acceptance_parameters)
                            .await?
                    }
                    frame::Message::Content(channel_id, payload) => {
                        me.on_content(channel_id, payload)?
                    }
                    frame::Message::ContentProcessed(channel_id, content_processed) => {
                        me.on_content_processed(channel_id, content_processed)?
                    }
                    frame::Message::ContentWritingCompleted(channel_id) => {
                        me.on_content_writing_completed(channel_id)?
                    }
                    frame::Message::ChannelTerminated(channel_id) => {
                        me.on_channel_terminated(channel_id, Vec::new()).await?
                    }
                }
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use tokio::io::{duplex, AsyncReadExt};

    use super::*;

    #[tokio::test]
    async fn create_v3() {
        let duplexes = duplex(4096);
        let (mx1, mx2) = (create(duplexes.0).unwrap(), create(duplexes.1).unwrap());
    }

    #[tokio::test]
    async fn offer_accept_by_name_then_drop() {
        env_logger::init();

        let (alice, bob) = duplex(4096);
        let (mut alice, bob) = (create(alice).unwrap(), create(bob).unwrap());
        const NAME: &str = "test_channel";
        let alice_channel = alice.offer_channel(NAME.to_string(), None);
        let bob_channel = bob.accept_channel_by_name(NAME.to_string(), None);

        let (alice_channel, bob_channel) = tokio::join!(alice_channel, bob_channel);
        let alice_channel = alice_channel.unwrap();
        let mut bob_channel = bob_channel.unwrap();

        // Verify that dropping one end of the channel is communicated with the other side.
        drop(alice_channel);
        let mut buf = [0u8; 1];
        let count = bob_channel.channel_duplex.read(&mut buf).await.unwrap();
        assert_eq!(count, 0);
    }
}
