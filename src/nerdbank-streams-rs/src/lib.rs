//! Tokio support for the Nerdbank.Streams multiplexing protocol.
//!
//! Protocol versions 1, 2, and 3 are supported. Later additive protocol
//! extensions are deliberately not emitted or accepted.

mod channel;
mod frame;
mod multiplexor;

/// Tokio implementation of the MultiplexingStream protocol.
///
/// This module contains the multiplexing API so that future, unrelated
/// `nerdbank-streams` features do not occupy the crate root.
pub mod mxstream {
    pub use crate::channel::{Channel, ChannelReadHalf, ChannelWriteHalf};
    pub use crate::multiplexor::{
        ChannelId, ChannelOptions, ChannelSource, Error, MultiplexingStream, Options,
        ProtocolVersion,
    };
}
