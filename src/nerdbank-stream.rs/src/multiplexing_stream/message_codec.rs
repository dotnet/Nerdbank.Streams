use tokio_util::codec::{Decoder, Encoder};

use super::{error::MultiplexingStreamError, frame::{Frame, FrameCodec, Message}};

pub struct MultiplexingMessageCodec<Codec> {
    frame_codec: Codec,
}

impl<Codec> MultiplexingMessageCodec<Codec> {
    pub fn new(codec: Codec) -> Self {
        Self { frame_codec: codec }
    }
}

impl<Codec: FrameCodec + Encoder<Frame, Error = MultiplexingStreamError>> Encoder<Message>
    for MultiplexingMessageCodec<Codec>
{
    type Error = Codec::Error;

    fn encode(&mut self, item: Message, dst: &mut bytes::BytesMut) -> Result<(), Self::Error> {
        self.frame_codec.encode(Codec::encode_frame(item)?, dst)
    }
}

impl<Codec: FrameCodec + Decoder<Item = Frame, Error = MultiplexingStreamError>> Decoder
    for MultiplexingMessageCodec<Codec>
{
    type Item = Message;

    type Error = Codec::Error;

    fn decode(&mut self, src: &mut bytes::BytesMut) -> Result<Option<Self::Item>, Self::Error> {
        self.frame_codec
            .decode(src)?
            .map(|f| Ok(Codec::decode_frame(f)?))
            .transpose()
    }
}
