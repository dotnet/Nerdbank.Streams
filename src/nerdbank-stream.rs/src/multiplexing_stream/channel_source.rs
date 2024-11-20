use super::error::MultiplexingStreamError;

/// An enumeration of the possible sources of a channel.
#[derive(Clone, Copy, Debug, PartialEq, Eq, Hash)]
pub enum ChannelSource {
    /// The channel was offered by the local MultiplexingStream to the other party.
    Local,

    /// The channel was offered to the local MultiplexingStream by the other party.
    Remote,

    /// The channel was seeded during construction via the #Options.seeded_channels array.
    /// This channel is to be accepted by both parties.
    Seeded,
}

impl From<ChannelSource> for i8 {
    fn from(value: ChannelSource) -> Self {
        // The ordinal values are chosen so as to make flipping the perspective as easy as negating the value,
        // while leaving the Seeded value unchanged.
        match value {
            ChannelSource::Local => 1,
            ChannelSource::Remote => -1,
            ChannelSource::Seeded => 0,
        }
    }
}

impl TryFrom<i64> for ChannelSource {
    type Error = MultiplexingStreamError;

    fn try_from(value: i64) -> Result<Self, Self::Error> {
        match value {
            1 => Ok(ChannelSource::Local),
            -1 => Ok(ChannelSource::Remote),
            0 => Ok(ChannelSource::Seeded),
            _ => Err(MultiplexingStreamError::ProtocolViolation(format!(
                "Unexpected channel source: {}",
                value
            ))),
        }
    }
}

impl ChannelSource {
    pub fn flip(&self) -> Self {
        match self {
            ChannelSource::Local => ChannelSource::Remote,
            ChannelSource::Remote => ChannelSource::Local,
            ChannelSource::Seeded => ChannelSource::Seeded,
        }
    }
}
