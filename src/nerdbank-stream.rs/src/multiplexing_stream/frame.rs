use super::{
    channel_source::ChannelSource, control_code::ControlCode, error::MultiplexingStreamError,
    ProtocolMajorVersion,
};

/// The maximum length of a frame's payload.
pub const FRAME_PAYLOAD_MAX_LENGTH: usize = 20 * 1024;

#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub struct QualifiedChannelId {
    pub id: u64,
    pub source: ChannelSource,
}

impl QualifiedChannelId {
    fn flip_perspective(&self) -> Self {
        QualifiedChannelId {
            id: self.id,
            source: self.source.flip(),
        }
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct FrameHeader {
    pub code: ControlCode,
    pub channel_id: Option<QualifiedChannelId>,
}

impl FrameHeader {
    /// Flips a qualified channel ID from being considered Remove<=>Local.
    pub fn flip_channel_perspective(&self) -> Self {
        Self {
            code: self.code,
            channel_id: match self.channel_id {
                None => None,
                Some(id) => Some(id.flip_perspective()),
            },
        }
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Frame {
    pub header: FrameHeader,
    pub payload: Vec<u8>,
}

impl Frame {
    pub fn flip_channel_perspective(self) -> Self {
        Self {
            header: self.header.flip_channel_perspective(),
            payload: self.payload,
        }
    }
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct OfferParameters {
    /// The maximum number of bytes that may be transmitted and not yet acknowledged as processed by the remote party.
    pub remote_window_size: Option<u64>,

    /// The name of the channel.
    pub name: String,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct AcceptanceParameters {
    pub remote_window_size: Option<u64>,
}

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct ContentProcessed(pub u64);

#[derive(Debug, Clone, PartialEq, Eq)]
pub enum Message {
    Offer(QualifiedChannelId, OfferParameters),
    Acceptance(QualifiedChannelId, AcceptanceParameters),
    Content(QualifiedChannelId, Vec<u8>),
    ContentProcessed(QualifiedChannelId, ContentProcessed),
    ContentWritingCompleted(QualifiedChannelId),
    ChannelTerminated(QualifiedChannelId),
}

pub trait FrameCodec {
    fn get_protocol_version(&self) -> ProtocolMajorVersion;
    fn decode_frame(&self, frame: Frame) -> Result<Message, MultiplexingStreamError>;
    fn encode_frame(&self, message: Message) -> Result<Frame, MultiplexingStreamError>;
}
