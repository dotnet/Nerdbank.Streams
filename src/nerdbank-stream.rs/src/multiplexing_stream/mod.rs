mod channel_source;
mod control_code;
mod error;
mod formatter;
mod frame;
mod options;

use std::{collections::HashMap, default, future::Pending, sync::Arc};

use channel_source::ChannelSource;
use control_code::ControlCode;
use error::MultiplexingStreamError;
use formatter::{FrameIO, V3Formatter};
pub use frame::QualifiedChannelId;
use frame::{FrameHeader, OfferParameters};
use options::ChannelOptions;
use tokio::{io::DuplexStream, sync::Mutex, task::JoinHandle};

pub use options::Options;

pub enum ProtocolMajorVersion {
    //V1,
    //V2,
    V3,
}

struct ChannelCore {
    offer_parameters: OfferParameters,
    options: ChannelOptions,
}

struct MultiplexingStreamCore {
    formatter: Box<dyn FrameIO>,
    open_channels: HashMap<QualifiedChannelId, Channel>,
    offered_channels: HashMap<QualifiedChannelId, PendingChannel>,
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
pub struct PendingChannel {
    id: QualifiedChannelId,
    mxstream: Arc<Mutex<MultiplexingStreamCore>>,
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
    listening: Option<JoinHandle<Result<(), MultiplexingStreamError>>>,
    next_unreserved_channel_id: u64,
}

impl MultiplexingStream {
    pub fn create(duplex: DuplexStream) -> Self {
        Self::create_with_options(duplex, Options::default())
    }

    pub fn create_with_options(duplex: DuplexStream, options: Options) -> Self {
        let core = MultiplexingStreamCore {
            formatter: match options.protocol_major_version {
                ProtocolMajorVersion::V3 => Box::new(V3Formatter::new(duplex)),
            },
            open_channels: HashMap::new(),
            offered_channels: HashMap::new(),
        };
        let next_unreserved_channel_id = options.seeded_channels.len() as u64;
        let core = Arc::new(Mutex::new(core));
        MultiplexingStream {
            core,
            options,
            listening: None,
            next_unreserved_channel_id,
        }
    }

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
        if self.listening.is_none() {
            return Err(MultiplexingStreamError::NotListening);
        }

        let offer_parameters = OfferParameters {
            name,
            remote_window_size: Some(
                options
                    .clone()
                    .map_or(self.options.default_channel_receiving_window_size, |o| {
                        o.channel_receiving_window_size
                    }),
            ),
        };
        let payload = self.core.lock().await.formatter.serializer().serialize_offer(&offer_parameters);
        let qualified_id = QualifiedChannelId {
            source: ChannelSource::Local,
            id: self.reserved_unused_channel_id(),
        };

        let pending_channel = PendingChannel {
            id: qualified_id,
            mxstream: self.core.clone(),
        };
        self.core
            .lock()
            .await
            .offered_channels
            .insert(qualified_id, pending_channel);

        let header = FrameHeader {
            code: ControlCode::Offer,
            channel_id: Some(qualified_id),
        };

        self.core.lock().await.formatter.write_frame(header, payload).await;

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

    async fn listen(&self) -> Result<(), MultiplexingStreamError> {
        loop {
            let frame = self.core.lock().await.formatter.read_frame().await?;
            match frame {
                None => return Ok(()),
                Some((header, payload)) => {
                    let channel_id = header.channel_id.ok_or_else(|| {
                        MultiplexingStreamError::ProtocolViolation(
                            "Missing ID in channel offer.".to_string(),
                        )
                    })?;
                    match header.code {
                        ControlCode::Offer => self.on_offer(channel_id, payload)?,
                        ControlCode::OfferAccepted => {
                            self.on_offer_accepted(channel_id, payload)?
                        }
                        ControlCode::Content => self.on_content(channel_id, payload)?,
                        ControlCode::ContentWritingCompleted => {
                            self.on_content_writing_completed(channel_id)?
                        }
                        ControlCode::ChannelTerminated => {
                            self.on_channel_terminated(channel_id, payload)?
                        }
                        ControlCode::ContentProcessed => {
                            self.on_content_processed(channel_id, payload)?
                        }
                    };
                }
            }
        }
    }

    fn on_offer(
        &self,
        channel_id: QualifiedChannelId,
        payload: Vec<u8>,
    ) -> Result<(), MultiplexingStreamError> {
        todo!()
    }

    fn on_offer_accepted(
        &self,
        channel_id: QualifiedChannelId,
        payload: Vec<u8>,
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
        payload: Vec<u8>,
    ) -> Result<(), MultiplexingStreamError> {
        todo!()
    }
}

#[cfg(test)]
mod tests {
    use tokio::io::duplex;

    use super::*;

    #[test]
    fn simple_v3() {
        let duplexes = duplex(4096);
        let (mx1, mx2) = (
            MultiplexingStream::create(duplexes.0),
            MultiplexingStream::create(duplexes.1),
        );
    }
}
