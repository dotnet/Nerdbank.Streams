use super::{frame::FRAME_PAYLOAD_MAX_LENGTH, ProtocolMajorVersion};

const RECOMMENDED_DEFAULT_CHANNEL_RECEIVING_WINDOW_SIZE: usize = 5 * FRAME_PAYLOAD_MAX_LENGTH;

pub struct Options {
    pub default_channel_receiving_window_size: usize,
    pub protocol_major_version: ProtocolMajorVersion,
    pub seeded_channels: Vec<ChannelOptions>,
}

impl Default for Options {
    fn default() -> Self {
        Self {
            default_channel_receiving_window_size:
                RECOMMENDED_DEFAULT_CHANNEL_RECEIVING_WINDOW_SIZE,
            protocol_major_version: ProtocolMajorVersion::V3,
            seeded_channels: Default::default(),
        }
    }
}

#[derive(Clone)]
pub struct ChannelOptions {
    pub channel_receiving_window_size: usize,
}
