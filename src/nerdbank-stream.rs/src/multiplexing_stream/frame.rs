use super::{channel_source::ChannelSource, control_code::ControlCode};

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

#[derive(Clone, Debug, PartialEq, Eq)]
pub struct Frame {
    pub header: FrameHeader,
    pub payload: Vec<u8>,
}

impl FrameHeader {
    /// Flips a qualified channel ID from being considered Remove<=>Local.
    fn flip_channel_perspective(&self) -> Self {
        Self {
            code: self.code,
            channel_id: match self.channel_id {
                None => None,
                Some(id) => Some(id.flip_perspective()),
            },
        }
    }
}

#[derive(Debug, PartialEq, Eq)]
pub struct OfferParameters {
    /// The maximum number of bytes that may be transmitted and not yet acknowledged as processed by the remote party.
    pub remote_window_size: Option<u64>,

    /// The name of the channel.
    pub name: String,
}

#[derive(Debug, PartialEq, Eq)]
pub struct AcceptanceParameters {
    pub remote_window_size: Option<u64>,
}

#[derive(Debug, PartialEq, Eq)]
pub struct ContentProcessed(pub u64);
