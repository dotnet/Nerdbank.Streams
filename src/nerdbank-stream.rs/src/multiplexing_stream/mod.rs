mod channel_source;
mod codec_v3;
mod control_code;
mod error;
mod frame;
mod options;

use std::{collections::HashMap, sync::Arc};

use channel_source::ChannelSource;
use codec_v3::MultiplexingFrameV3Codec;
use error::MultiplexingStreamError;
pub use frame::QualifiedChannelId;
use frame::{AcceptanceParameters, ContentProcessed, Frame, FrameCodec, Message, OfferParameters};
use futures::{
    stream::{SplitSink, SplitStream},
    StreamExt,
};
use futures_util::SinkExt;
use options::ChannelOptions;
use tokio::{io::DuplexStream, sync::Mutex, task::JoinHandle};

pub use options::Options;
use tokio_util::codec::{Decoder, Encoder, Framed};

// Define a trait for the constraints we'll use in many places.
trait MultiplexingStreamCodec:
    FrameCodec
    + Decoder<Item = Frame, Error = MultiplexingStreamError>
    + Encoder<Frame, Error = MultiplexingStreamError>
{
}

// Make it an auto-trait, so that any complying codec will Just Work.
impl<T> MultiplexingStreamCodec for T where
    T: FrameCodec
        + Decoder<Item = Frame, Error = MultiplexingStreamError>
        + Encoder<Frame, Error = MultiplexingStreamError>
{
}

#[derive(Copy, Clone, Debug)]
pub enum ProtocolMajorVersion {
    //V1,
    //V2,
    V3,
}

struct ChannelCore {
    offer_parameters: OfferParameters,
    options: ChannelOptions,
}

struct MultiplexingStreamCore<Codec: MultiplexingStreamCodec> {
    writer: SplitSink<Framed<DuplexStream, Codec>, Frame>,
    open_channels: HashMap<QualifiedChannelId, Channel>,
    offered_channels: HashMap<QualifiedChannelId, PendingChannel<Codec>>,
}

// TODO: dropping this should send a channel closed notice to the remote party
pub struct Channel {
    core: Arc<Mutex<ChannelCore>>,
    id: QualifiedChannelId,
    name: Option<String>,
    duplex: DuplexStream,
}

impl Channel {
    pub fn id(&self) -> QualifiedChannelId {
        self.id
    }

    pub fn name(&self) -> &Option<String> {
        &self.name
    }

    pub fn duplex(&mut self) -> &mut DuplexStream {
        &mut self.duplex
    }
}
// TODO: dropping this should send a cancellation notice to the remote party
pub struct PendingChannel<Codec: MultiplexingStreamCodec> {
    id: QualifiedChannelId,
    mxstream: Arc<Mutex<MultiplexingStreamCore<Codec>>>,
}

impl<Codec: MultiplexingStreamCodec> PendingChannel<Codec> {
    pub fn id(&self) -> QualifiedChannelId {
        self.id
    }

    pub async fn get_channel(self) -> Result<Channel, MultiplexingStreamError> {
        // This method intentionally *consumes* self.
        todo!()
    }
}

pub struct MultiplexingStream<Codec: MultiplexingStreamCodec> {
    core: Arc<Mutex<MultiplexingStreamCore<Codec>>>,
    options: Options,
    listening: Option<JoinHandle<Result<(), MultiplexingStreamError>>>,
    next_unreserved_channel_id: u64,
}

pub fn create_v3(duplex: DuplexStream) -> MultiplexingStream<MultiplexingFrameV3Codec> {
    create_v3_with_options(duplex, Options::default())
}

pub fn create_v3_with_options(
    duplex: DuplexStream,
    options: Options,
) -> MultiplexingStream<MultiplexingFrameV3Codec> {
    let next_unreserved_channel_id = options.seeded_channels.len() as u64;

    let framed = match options.protocol_major_version {
        ProtocolMajorVersion::V3 => Framed::new(duplex, MultiplexingFrameV3Codec::new()),
    };

    let (writer, reader) = framed.split();

    let core = MultiplexingStreamCore::<MultiplexingFrameV3Codec> {
        writer,
        open_channels: HashMap::new(),
        offered_channels: HashMap::new(),
    };
    let core = Arc::new(Mutex::new(core));
    MultiplexingStream {
        core,
        options,
        listening: None,
        next_unreserved_channel_id,
    }
}

impl<Codec: MultiplexingStreamCodec> MultiplexingStream<Codec> {
    pub fn create_channel(
        options: Option<ChannelOptions>,
    ) -> Result<PendingChannel<Codec>, MultiplexingStreamError> {
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
        if self.listening.is_none() {
            return Err(MultiplexingStreamError::NotListening);
        }
        let qualified_id = QualifiedChannelId {
            source: ChannelSource::Local,
            id: self.reserved_unused_channel_id(),
        };

        let offer_parameters = OfferParameters {
            name,
            remote_window_size: Some(
                options
                    .clone()
                    .map_or(self.options.default_channel_receiving_window_size, |o| {
                        o.channel_receiving_window_size
                    }) as u64,
            ),
        };
        let message = Message::Offer(qualified_id, offer_parameters.clone());

        let pending_channel = PendingChannel {
            id: qualified_id,
            mxstream: self.core.clone(),
        };
        let mut core = self.core.lock().await;
        core.offered_channels.insert(qualified_id, pending_channel);
        core.writer.send(Codec::encode_frame(message)?).await?;

        // TODO: If the promise we return is dropped, we should cancel the offer.

        let core = ChannelCore {
            offer_parameters,
            options: options.unwrap_or_else(|| self.default_channel_options()),
        };
        let core = Arc::new(Mutex::new(core));

        todo!()
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
        if self.listening.is_none() {
            return Err(MultiplexingStreamError::NotListening);
        }

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

    // pub fn start_listening(&mut self) -> Result<(), MultiplexingStreamError> {
    //     if self.listening.is_some() {
    //         return Err(MultiplexingStreamError::ListeningAlreadyStarted);
    //     }

    //     self.listening = Some(tokio::spawn(self.listen()));

    //     Ok(())
    // }

    async fn listen(
        this: Arc<Mutex<MultiplexingStream<Codec>>>,
        mut stream: SplitStream<Framed<DuplexStream, Codec>>,
    ) -> Result<(), MultiplexingStreamError> {
        loop {
            while let Some(frame) = stream.next().await.transpose()? {
                let me = this.lock().await;
                match Codec::decode_frame(frame)? {
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

#[cfg(test)]
mod tests {
    use tokio::io::duplex;

    use super::*;

    #[tokio::test]
    async fn simple_v3() {
        let duplexes = duplex(4096);
        let (mx1, mx2) = (
            create_v3(duplexes.0),
            create_v3(duplexes.1),
        );
    }
}
