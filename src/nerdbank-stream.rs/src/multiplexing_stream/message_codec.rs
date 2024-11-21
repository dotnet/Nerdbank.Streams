use tokio_util::codec::{Decoder, Encoder};

use super::{
    error::MultiplexingStreamError,
    frame::{Frame, FrameCodec, Message},
};

pub trait MultiplexingFrameCodecClone {
    fn clone_box(&self) -> Box<dyn MultiplexingFrameCodec>;
}

// Define a trait for the constraints we'll use in many places.
pub trait MultiplexingFrameCodec:
    FrameCodec
    + MultiplexingFrameCodecClone
    + Decoder<Item = Frame, Error = MultiplexingStreamError>
    + Encoder<Frame, Error = MultiplexingStreamError>
{
}

// Make it an auto-trait, so that any complying codec will Just Work.
impl<T> MultiplexingFrameCodec for T where
    T: FrameCodec
        + MultiplexingFrameCodecClone
        + Decoder<Item = Frame, Error = MultiplexingStreamError>
        + Encoder<Frame, Error = MultiplexingStreamError>
{
}

impl Clone for Box<dyn MultiplexingFrameCodec> {
    fn clone(&self) -> Box<dyn MultiplexingFrameCodec> {
        self.clone_box()
    }
}

#[derive(Clone)]
pub struct MultiplexingMessageCodec {
    frame_codec: Box<dyn MultiplexingFrameCodec>,
}

impl MultiplexingMessageCodec {
    pub fn new(codec: Box<dyn MultiplexingFrameCodec>) -> Self {
        Self { frame_codec: codec }
    }
}

impl Encoder<Message> for MultiplexingMessageCodec {
    type Error = MultiplexingStreamError;

    fn encode(&mut self, item: Message, dst: &mut bytes::BytesMut) -> Result<(), Self::Error> {
        self.frame_codec
            .encode(self.frame_codec.encode_frame(item)?, dst)
    }
}

impl Decoder for MultiplexingMessageCodec {
    type Item = Message;
    type Error = MultiplexingStreamError;

    fn decode(&mut self, src: &mut bytes::BytesMut) -> Result<Option<Self::Item>, Self::Error> {
        self.frame_codec
            .decode(src)?
            .map(|f| Ok(self.frame_codec.decode_frame(f)?))
            .transpose()
    }
}
