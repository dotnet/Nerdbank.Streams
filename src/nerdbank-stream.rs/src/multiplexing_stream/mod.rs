mod channel_source;
mod codec_v3;
mod control_code;
mod error;
mod frame;
mod message_codec;
mod options;

use std::{
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
    io::{duplex, DuplexStream},
    sync::{oneshot, Mutex},
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
    stream_core: Arc<Mutex<MultiplexingStreamCore>>,
}

struct MultiplexingStreamCore {
    writer: SplitSink<Framed<DuplexStream, MultiplexingMessageCodec>, Message>,
    listening: Option<JoinHandle<Result<(), MultiplexingStreamError>>>,
    open_channels: HashMap<QualifiedChannelId, Channel>,
    offered_channels: HashMap<QualifiedChannelId, PendingChannel>,
    offered_channels_by_them_by_name: HashMap<String, VecDeque<PendingChannelWithId>>,
    accepting_channels: HashMap<String, VecDeque<PendingChannelWithName>>,
}

impl MultiplexingStreamCore {
    pub fn start_listening(
        self,
        stream: SplitStream<Framed<DuplexStream, MultiplexingMessageCodec>>,
    ) -> Result<Arc<Mutex<Self>>, MultiplexingStreamError> {
        if self.listening.is_some() {
            return Err(MultiplexingStreamError::ListeningAlreadyStarted);
        }

        //self.listening = Some(tokio::spawn(Self::listen(mutex, stream)));

        Ok(Arc::new(Mutex::new(self)))
    }

    async fn listen(
        this: Arc<Mutex<MultiplexingStreamCore>>,
        mut stream: SplitStream<Framed<DuplexStream, MultiplexingMessageCodec>>,
    ) -> Result<(), MultiplexingStreamError> {
        loop {
            while let Some(message) = stream.next().await.transpose()? {
                let me = this.lock().await;
                match message {
                    frame::Message::Offer(channel_id, offer_parameters) => {
                        me.on_offer(channel_id, offer_parameters)?
                    }
                    frame::Message::Acceptance(channel_id, acceptance_parameters) => {
                        me.on_offer_accepted(channel_id, acceptance_parameters)?
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
                        me.on_channel_terminated(channel_id, Vec::new())?
                    }
                }
            }
        }
    }

    fn on_offer(
        &self,
        channel_id: QualifiedChannelId,
        offer: OfferParameters,
    ) -> Result<(), MultiplexingStreamError> {
        todo!()
    }

    fn on_offer_accepted(
        &self,
        channel_id: QualifiedChannelId,
        acceptance: AcceptanceParameters,
    ) -> Result<(), MultiplexingStreamError> {
        todo!()
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

    fn on_channel_terminated(
        &self,
        channel_id: QualifiedChannelId,
        payload: Vec<u8>,
    ) -> Result<(), MultiplexingStreamError> {
        todo!()
    }

    fn on_content_processed(
        &self,
        channel_id: QualifiedChannelId,
        payload: ContentProcessed,
    ) -> Result<(), MultiplexingStreamError> {
        todo!()
    }
}

// TODO: dropping this should send a channel closed notice to the remote party
pub struct Channel {
    core: Arc<Mutex<ChannelCore>>,
    id: QualifiedChannelId,
    name: Option<String>,
    central_duplex: DuplexStream,
    channel_duplex: DuplexStream,
}

impl Channel {
    pub fn id(&self) -> QualifiedChannelId {
        self.id
    }

    pub fn name(&self) -> &Option<String> {
        &self.name
    }

    pub fn duplex(&mut self) -> &mut DuplexStream {
        &mut self.channel_duplex
    }
}

enum PendingChannel {
    Name(PendingChannelWithName),
    Id(PendingChannelWithId),
}

pub struct PendingChannelBase {
    mxstream: Arc<Mutex<MultiplexingStreamCore>>,
    matured_signal: oneshot::Sender<Channel>,
}

// TODO: dropping this should send a cancellation notice to the remote party
pub struct PendingChannelWithId {
    id: QualifiedChannelId,
    base: PendingChannelBase,
}

pub struct PendingChannelWithName {
    name: String,
    base: PendingChannelBase,
}

impl PendingChannelWithId {
    pub fn id(&self) -> QualifiedChannelId {
        self.id
    }
}

pub struct MultiplexingStream {
    core: Arc<Mutex<MultiplexingStreamCore>>,
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

    let core = MultiplexingStreamCore {
        writer,
        open_channels: HashMap::new(),
        offered_channels: HashMap::new(),
        offered_channels_by_them_by_name: HashMap::new(),
        accepting_channels: HashMap::new(),
        listening: None,
    };
    let core_mutex = core.start_listening(reader)?;

    Ok(MultiplexingStream {
        core: core_mutex,
        options,
        next_unreserved_channel_id,
    })
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

        let window_size = options
            .clone()
            .map_or(self.options.default_channel_receiving_window_size, |o| {
                o.channel_receiving_window_size
            });
        let offer_parameters = OfferParameters {
            name: name.clone(),
            remote_window_size: Some(window_size as u64),
        };
        let message = Message::Offer(qualified_id, offer_parameters.clone());

        let (sender, receiver) = oneshot::channel();
        let pending_channel = PendingChannelWithId {
            id: qualified_id,
            base: PendingChannelBase {
                mxstream: self.core.clone(),
                matured_signal: sender,
            },
        };
        let mut core = self.core.lock().await;
        core.offered_channels
            .insert(qualified_id, PendingChannel::Id(pending_channel));
        core.writer.send(message).await?;

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
                core.writer
                    .send(Message::Acceptance(id, acceptance_parameters))
                    .await?;
                return Ok(self.mature_pending_channel(
                    PendingChannel::Id(pending_channel),
                    id,
                    Some(name),
                ));
            }
        }

        // Add our interest in this channel to the shared state
        let (sender, receiver) = oneshot::channel();
        let by_name_deque = core
            .accepting_channels
            .entry(name.clone())
            .or_insert(VecDeque::new());
        let pending_channel = PendingChannelWithName {
            name: name,
            base: PendingChannelBase {
                mxstream: self.core.clone(),
                matured_signal: sender,
            },
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

    fn mature_pending_channel(
        &self,
        pending_channel: PendingChannel,
        channel_id: QualifiedChannelId,
        name: Option<String>,
    ) -> Channel {
        let channel_core = ChannelCore {
            stream_core: self.core.clone(),
        };
        let (channel_duplex, central_duplex) = duplex(4096); // TODO: use the right window size here.
        let channel_mutex = Arc::new(Mutex::new(channel_core));
        Channel {
            core: channel_mutex,
            id: channel_id,
            name: name,
            channel_duplex,
            central_duplex,
        }
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
}

#[cfg(test)]
mod tests {
    use tokio::io::duplex;

    use super::*;

    #[tokio::test]
    async fn create_v3() {
        let duplexes = duplex(4096);
        let (mx1, mx2) = (create(duplexes.0).unwrap(), create(duplexes.1).unwrap());
    }

    #[tokio::test]
    async fn offer_accept_by_name() {
        let (alice, bob) = duplex(4096);
        let (mut alice, bob) = (create(alice).unwrap(), create(bob).unwrap());
        const NAME: &str = "test_channel";
        let alice_channel = alice.offer_channel(NAME.to_string(), None).await.unwrap();
        let bob_channel = bob
            .accept_channel_by_name(NAME.to_string(), None)
            .await
            .unwrap();
    }
}
