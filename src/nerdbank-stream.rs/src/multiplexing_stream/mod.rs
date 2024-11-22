mod channel_source;
mod codec_v3;
mod control_code;
mod error;
mod frame;
mod message_codec;
mod options;

use std::{collections::HashMap, sync::Arc};

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
    sync::Mutex,
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
    offer_parameters: OfferParameters,
    options: ChannelOptions,
    stream_core: Arc<Mutex<MultiplexingStreamCore>>,
}

struct MultiplexingStreamCore {
    writer: SplitSink<Framed<DuplexStream, MultiplexingMessageCodec>, Message>,
    listening: Option<JoinHandle<Result<(), MultiplexingStreamError>>>,
    open_channels: HashMap<QualifiedChannelId, Channel>,
    offered_channels: HashMap<QualifiedChannelId, PendingChannel>,
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
    // central_duplex: DuplexStream,
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
// TODO: dropping this should send a cancellation notice to the remote party
pub struct PendingChannel {
    id: QualifiedChannelId,
    mxstream: Arc<Mutex<MultiplexingStreamCore>>,
    central_duplex: DuplexStream,
}

impl PendingChannel {
    pub fn id(&self) -> QualifiedChannelId {
        self.id
    }

    pub async fn get_channel(self) -> Result<Channel, MultiplexingStreamError> {
        // This method intentionally *consumes* self.
        todo!()
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

        let (central_duplex, channel_duplex) = duplex(window_size);
        let pending_channel = PendingChannel {
            id: qualified_id,
            mxstream: self.core.clone(),
            central_duplex,
        };
        let mut core = self.core.lock().await;
        core.offered_channels.insert(qualified_id, pending_channel);
        core.writer.send(message).await?;

        // TODO: If the promise we return is dropped, we should cancel the offer.

        let channel_core = ChannelCore {
            offer_parameters,
            options: options.unwrap_or_else(|| self.default_channel_options()),
            stream_core: self.core.clone(),
        };
        let channel_core = Arc::new(Mutex::new(channel_core));

        Ok(Channel {
            core: channel_core,
            id: qualified_id,
            name: Some(name),
            // central_duplex,
            channel_duplex,
        })
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
        todo!()
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
    async fn simple_v3() {
        let duplexes = duplex(4096);
        let (mx1, mx2) = (create(duplexes.0), create(duplexes.1));
    }
}
