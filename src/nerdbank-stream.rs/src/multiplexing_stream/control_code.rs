use super::error::MultiplexingStreamError;

/// Signals what kind of frame is being transmitted.
#[derive(Clone, Copy, Debug, PartialEq, Eq)]
pub enum ControlCode {
    /// A channel is proposed to the remote party.
    Offer,

    /// A channel proposal has been accepted.
    OfferAccepted,

    /// The payload of the frame is a payload intended for channel consumption.
    Content,

    /// Sent after all bytes have been transmitted on a given channel. Either or both sides may send this.
    /// A channel may be automatically closed when each side has both transmitted and received this message.
    ContentWritingCompleted,

    /// Sent when a channel is closed, an incoming offer is rejected, or an outgoing offer is canceled.
    ChannelTerminated,

    /// Sent when a channel has finished processing data received from the remote party,
    /// allowing them to send more data.
    ContentProcessed,
}

impl TryFrom<u8> for ControlCode {
    type Error = MultiplexingStreamError;

    fn try_from(value: u8) -> Result<Self, Self::Error> {
        TryFrom::<u64>::try_from(value as u64)
    }
}

impl TryFrom<u64> for ControlCode {
    type Error = MultiplexingStreamError;

    fn try_from(value: u64) -> Result<Self, Self::Error> {
        match value {
            0 => Ok(ControlCode::Offer),
            1 => Ok(ControlCode::OfferAccepted),
            2 => Ok(ControlCode::Content),
            3 => Ok(ControlCode::ContentWritingCompleted),
            4 => Ok(ControlCode::ChannelTerminated),
            5 => Ok(ControlCode::ContentProcessed),
            _ => return Err(MultiplexingStreamError::ProtocolViolation(format!("Unrecognized control code: {}", value))),
        }
    }
}

impl From<ControlCode> for u8 {
    fn from(value: ControlCode) -> Self {
        match value {
            ControlCode::Offer => 0,
            ControlCode::OfferAccepted => 1,
            ControlCode::Content => 2,
            ControlCode::ContentWritingCompleted => 3,
            ControlCode::ChannelTerminated => 4,
            ControlCode::ContentProcessed => 5,
        }
    }
}
