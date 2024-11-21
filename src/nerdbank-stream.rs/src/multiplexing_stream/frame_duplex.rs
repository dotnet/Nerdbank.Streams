use std::io::{ErrorKind, Read};

use bytes::{Buf, BufMut};
use rmp::decode::{NumValueReadError, RmpRead, ValueReadError};
use tokio_util::codec::{Decoder, Encoder};

use super::{
    channel_source::ChannelSource,
    control_code::ControlCode,
    error::MultiplexingStreamError,
    frame::{Frame, FrameHeader, FRAME_PAYLOAD_MAX_LENGTH},
    QualifiedChannelId,
};

const FRAME_HEADER_MAX_LENGTH: usize = 50; // this is an over-estimate.
const MAX_FRAME_SIZE: usize = FRAME_HEADER_MAX_LENGTH + FRAME_PAYLOAD_MAX_LENGTH;

struct MultiplexingFrameV3Codec {}

impl MultiplexingFrameV3Codec {
    fn new() -> Self {
        MultiplexingFrameV3Codec {}
    }
}

impl Decoder for MultiplexingFrameV3Codec {
    type Item = Frame;

    type Error = MultiplexingStreamError;

    fn decode(&mut self, src: &mut bytes::BytesMut) -> Result<Option<Self::Item>, Self::Error> {
        let mut reader = src.reader();

        let array_len = rmp::decode::read_array_len(&mut reader).map_err(map_error)?;
        if array_len < 1 || array_len == 2 {
            return Err(MultiplexingStreamError::ProtocolViolation(format!(
                "Unexpected array length {} reading frame.",
                array_len
            )));
        }

        let code = ControlCode::try_from(
            rmp::decode::read_int::<u8, _>(&mut reader).map_err(map_val_error)?,
        )?;
        let channel_id = if array_len >= 3 {
            let id = rmp::decode::read_int(&mut reader).map_err(map_val_error)?;
            let source = ChannelSource::try_from(
                rmp::decode::read_int::<i8, _>(&mut reader).map_err(map_val_error)?,
            )?;
            Some(QualifiedChannelId { id, source })
        } else {
            None
        };
        let payload = if array_len >= 4 {
            let payload_len = rmp::decode::read_bin_len(&mut reader).map_err(map_error)?;
            let mut payload = Vec::with_capacity(payload_len as usize);
            payload.resize(payload_len as usize, 0);
            if let Err(e) = reader.read_exact_buf(payload.as_mut_slice()) {
                return match e.kind() {
                    ErrorKind::UnexpectedEof => {
                        // Help ensure we get sufficient bytes next time.
                        src.reserve(MAX_FRAME_SIZE);
                        Ok(None)
                    }
                    _ => Err(MultiplexingStreamError::from(e)),
                };
            }

            payload
        } else {
            Vec::new()
        };

        return Ok(Some(Frame {
            header: FrameHeader { code, channel_id },
            payload,
        }));

        fn map_error(error: ValueReadError) -> MultiplexingStreamError {
            match error {
                // TODO: recognize real errors from insufficient buffers
                _ => MultiplexingStreamError::ReadFailure(format!(
                    "Failure decoding frame: {}",
                    error
                )),
            }
        }

        fn map_val_error(error: NumValueReadError) -> MultiplexingStreamError {
            match error {
                // TODO: recognize real errors from insufficient buffers
                _ => MultiplexingStreamError::ReadFailure(format!(
                    "Failure decoding frame: {}",
                    error
                )),
            }
        }
    }
}

impl Encoder<Frame> for MultiplexingFrameV3Codec {
    type Error = MultiplexingStreamError;

    fn encode(&mut self, frame: Frame, dst: &mut bytes::BytesMut) -> Result<(), Self::Error> {
        let element_count = match (frame.header.channel_id.is_some(), !frame.payload.is_empty()) {
            (false, true) => panic!("Frames with payloads must include a channel id."),
            (true, true) => 4,
            (true, false) => 3,
            (false, false) => 1,
        };

        let mut writer = dst.writer();
        rmp::encode::write_array_len(&mut writer, element_count)?;

        rmp::encode::write_uint(&mut writer, u8::from(frame.header.code) as u64)?;

        if let Some(channel_id) = frame.header.channel_id {
            rmp::encode::write_uint(&mut writer, channel_id.id)?;
            rmp::encode::write_sint(&mut writer, i8::from(channel_id.source) as i64)?;

            if !frame.payload.is_empty() {
                rmp::encode::write_bin(&mut writer, &frame.payload)?;
            }
        }

        Ok(())
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    use futures_util::SinkExt;
    use tokio::io::duplex;
    use tokio_stream::StreamExt;
    use tokio_util::codec::Framed;

    #[tokio::test]
    async fn frame_all_fields() {
        roundtrip(Frame {
            header: FrameHeader {
                channel_id: Some(QualifiedChannelId {
                    id: 5,
                    source: ChannelSource::Local,
                }),
                code: ControlCode::OfferAccepted,
            },
            payload: vec![1, 2, 3],
        })
        .await;
    }

    #[tokio::test]
    async fn frame_no_payload() {
        roundtrip(Frame {
            header: FrameHeader {
                channel_id: Some(QualifiedChannelId {
                    id: 5,
                    source: ChannelSource::Local,
                }),
                code: ControlCode::Offer,
            },
            payload: vec![],
        })
        .await;
    }

    #[tokio::test]
    async fn frame_no_payload_or_channel_id() {
        roundtrip(Frame {
            header: FrameHeader {
                channel_id: None,
                code: ControlCode::Offer,
            },
            payload: vec![],
        })
        .await;
    }
    async fn roundtrip(frame: Frame) {
        let (alice, bob) = duplex(64);
        let mut alice_framed = Framed::new(alice, MultiplexingFrameV3Codec::new());
        let mut bob_framed = Framed::new(bob, MultiplexingFrameV3Codec::new());
        bob_framed.send(frame.clone()).await.unwrap();
        let deserialized_frame = alice_framed.next().await.unwrap().unwrap();
        assert!(frame.eq(&deserialized_frame));
    }
}
