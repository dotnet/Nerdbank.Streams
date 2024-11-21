use std::io::ErrorKind;

use bytes::{Buf, BufMut};
use msgpack_simple::MsgPack;
use rmp::decode::{NumValueReadError, RmpRead, ValueReadError};
use tokio_util::codec::{Decoder, Encoder};

use super::{
    channel_source::ChannelSource,
    control_code::ControlCode,
    error::MultiplexingStreamError,
    frame::{
        AcceptanceParameters, ContentProcessed, Frame, FrameCodec, FrameHeader, Message,
        OfferParameters, FRAME_PAYLOAD_MAX_LENGTH,
    },
    message_codec::MultiplexingFrameCodecClone,
    ProtocolMajorVersion, QualifiedChannelId,
};

const FRAME_HEADER_MAX_LENGTH: usize = 50; // this is an over-estimate.
const MAX_FRAME_SIZE: usize = FRAME_HEADER_MAX_LENGTH + FRAME_PAYLOAD_MAX_LENGTH;

#[derive(Clone)]
pub struct MultiplexingFrameV3Codec;

impl MultiplexingFrameCodecClone for MultiplexingFrameV3Codec {
    fn clone_box(&self) -> Box<dyn super::message_codec::MultiplexingFrameCodec> {
        Box::new(self.clone())
    }
}

impl FrameCodec for MultiplexingFrameV3Codec {
    fn get_protocol_version(&self) -> ProtocolMajorVersion {
        ProtocolMajorVersion::V3
    }

    fn decode_frame(&self, frame: Frame) -> Result<Message, MultiplexingStreamError> {
        let channel_id = frame.header.channel_id.ok_or_else(|| {
            MultiplexingStreamError::ProtocolViolation("Missing ID in channel offer.".to_string())
        })?;
        Ok(match frame.header.code {
            ControlCode::Offer => {
                Message::Offer(channel_id, Self::deserialize_offer(&frame.payload)?)
            }
            ControlCode::OfferAccepted => {
                Message::Acceptance(channel_id, Self::deserialize_acceptance(&frame.payload)?)
            }
            ControlCode::Content => Message::Content(channel_id, frame.payload),
            ControlCode::ContentWritingCompleted => Message::ContentWritingCompleted(channel_id),
            ControlCode::ChannelTerminated => Message::ChannelTerminated(channel_id),
            ControlCode::ContentProcessed => Message::ContentProcessed(
                channel_id,
                Self::deserialize_content_processed(&frame.payload)?,
            ),
        })
    }

    fn encode_frame(&self, message: Message) -> Result<Frame, MultiplexingStreamError> {
        Ok(match message {
            Message::Offer(qualified_channel_id, offer_parameters) => Frame {
                header: FrameHeader {
                    code: ControlCode::Offer,
                    channel_id: Some(qualified_channel_id),
                },
                payload: Self::serialize_offer(&offer_parameters),
            },
            Message::Acceptance(qualified_channel_id, acceptance_parameters) => Frame {
                header: FrameHeader {
                    code: ControlCode::OfferAccepted,
                    channel_id: Some(qualified_channel_id),
                },
                payload: Self::serialize_acceptance(&acceptance_parameters),
            },
            Message::Content(qualified_channel_id, payload) => Frame {
                header: FrameHeader {
                    code: ControlCode::Content,
                    channel_id: Some(qualified_channel_id),
                },
                payload,
            },
            Message::ContentProcessed(qualified_channel_id, content_processed) => Frame {
                header: FrameHeader {
                    code: ControlCode::ContentProcessed,
                    channel_id: Some(qualified_channel_id),
                },
                payload: Self::serialize_content_processed(&content_processed),
            },
            Message::ContentWritingCompleted(qualified_channel_id) => Frame {
                header: FrameHeader {
                    code: ControlCode::ContentWritingCompleted,
                    channel_id: Some(qualified_channel_id),
                },
                payload: Vec::new(),
            },
            Message::ChannelTerminated(qualified_channel_id) => Frame {
                header: FrameHeader {
                    code: ControlCode::ChannelTerminated,
                    channel_id: Some(qualified_channel_id),
                },
                payload: Vec::new(),
            },
        })
    }
}

impl MultiplexingFrameV3Codec {
    pub fn new() -> Self {
        MultiplexingFrameV3Codec {}
    }

    fn serialize_offer(offer: &OfferParameters) -> Vec<u8> {
        let mut vec = Vec::new();
        vec.push(MsgPack::String(offer.name.clone()));
        if let Some(remote_window_size) = offer.remote_window_size {
            vec.push(MsgPack::Uint(remote_window_size));
        }

        MsgPack::Array(vec).encode()
    }

    fn deserialize_offer(offer: &[u8]) -> Result<OfferParameters, MultiplexingStreamError> {
        let offer = MsgPack::parse(offer)?;
        let array = offer.as_array()?;

        Ok(OfferParameters {
            name: array
                .get(0)
                .ok_or(MultiplexingStreamError::ProtocolViolation(
                    "Missing name in offered channel.".to_string(),
                ))?
                .clone()
                .as_string()?,
            remote_window_size: array.get(1).map(|a| a.clone().as_uint()).transpose()?,
        })
    }

    fn serialize_acceptance(accept: &AcceptanceParameters) -> Vec<u8> {
        let mut vec = Vec::new();
        if let Some(remote_window_size) = accept.remote_window_size {
            vec.push(MsgPack::Uint(remote_window_size));
        }

        MsgPack::Array(vec).encode()
    }

    fn deserialize_acceptance(
        acceptance: &[u8],
    ) -> Result<AcceptanceParameters, MultiplexingStreamError> {
        let acceptance = MsgPack::parse(acceptance)?;
        let array = acceptance.as_array()?;

        Ok(AcceptanceParameters {
            remote_window_size: array.get(0).map(|a| a.clone().as_uint()).transpose()?,
        })
    }

    fn serialize_content_processed(bytes_processed: &ContentProcessed) -> Vec<u8> {
        MsgPack::Array(vec![MsgPack::Uint(bytes_processed.0)]).encode()
    }

    fn deserialize_content_processed(
        bytes_processed: &[u8],
    ) -> Result<ContentProcessed, MultiplexingStreamError> {
        let bytes_processed = MsgPack::parse(bytes_processed)?;
        let array = bytes_processed.as_array()?;

        Ok(ContentProcessed(
            array
                .get(0)
                .ok_or(MultiplexingStreamError::ProtocolViolation(
                    "Missing length of processed content.".to_string(),
                ))?
                .clone()
                .as_uint()?,
        ))
    }
}

impl Decoder for MultiplexingFrameV3Codec {
    type Item = Frame;

    type Error = MultiplexingStreamError;

    fn decode(&mut self, src: &mut bytes::BytesMut) -> Result<Option<Self::Item>, Self::Error> {
        let mut reader = src.reader();

        let array_len = match rmp::decode::read_array_len(&mut reader) {
            Ok(v) => v,
            Err(e) if is_eof(&e) => return report_more_bytes_needed(src),
            Err(e) => return Err(MultiplexingStreamError::from(e)),
        };
        if array_len < 1 || array_len == 2 {
            return Err(MultiplexingStreamError::ProtocolViolation(format!(
                "Unexpected array length {} reading frame.",
                array_len
            )));
        }

        let code = ControlCode::try_from(match rmp::decode::read_int::<u8, _>(&mut reader) {
            Ok(v) => v,
            Err(e) if is_eof_value(&e) => return report_more_bytes_needed(src),
            Err(e) => return Err(MultiplexingStreamError::from(e)),
        })?;
        let channel_id = if array_len >= 3 {
            let id = match rmp::decode::read_int(&mut reader) {
                Ok(v) => v,
                Err(e) if is_eof_value(&e) => return report_more_bytes_needed(src),
                Err(e) => return Err(MultiplexingStreamError::from(e)),
            };
            let source =
                ChannelSource::try_from(match rmp::decode::read_int::<i8, _>(&mut reader) {
                    Ok(v) => v,
                    Err(e) if is_eof_value(&e) => return report_more_bytes_needed(src),
                    Err(e) => return Err(MultiplexingStreamError::from(e)),
                })?;
            Some(QualifiedChannelId { id, source })
        } else {
            None
        };
        let payload = if array_len >= 4 {
            let payload_len = match rmp::decode::read_bin_len(&mut reader) {
                Ok(v) => v,
                Err(e) if is_eof(&e) => return report_more_bytes_needed(src),
                Err(e) => return Err(MultiplexingStreamError::from(e)),
            };
            let mut payload = Vec::with_capacity(payload_len as usize);
            payload.resize(payload_len as usize, 0);
            if let Err(e) = reader.read_exact_buf(payload.as_mut_slice()) {
                return match e.kind() {
                    ErrorKind::UnexpectedEof => report_more_bytes_needed(src),
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

        fn is_eof(error: &ValueReadError) -> bool {
            matches!(error, ValueReadError::InvalidMarkerRead(ref e) if e.kind() == ErrorKind::UnexpectedEof)
        }

        fn is_eof_value(error: &NumValueReadError) -> bool {
            matches!(error, NumValueReadError::InvalidMarkerRead(ref e) if e.kind() == ErrorKind::UnexpectedEof)
        }

        fn report_more_bytes_needed(
            src: &mut bytes::BytesMut,
        ) -> Result<Option<Frame>, MultiplexingStreamError> {
            src.reserve(MAX_FRAME_SIZE);
            Ok(None)
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
    use crate::multiplexing_stream::message_codec::{
        MultiplexingFrameCodec, MultiplexingMessageCodec,
    };

    use super::*;
    use futures_util::SinkExt;
    use tokio::io::duplex;
    use tokio_stream::StreamExt;
    use tokio_util::codec::Framed;

    fn qualified_channel() -> QualifiedChannelId {
        QualifiedChannelId {
            id: 2,
            source: ChannelSource::Local,
        }
    }

    fn minimal_frame() -> Frame {
        Frame {
            header: FrameHeader {
                channel_id: None,
                code: ControlCode::Offer,
            },
            payload: Vec::new(),
        }
    }

    async fn send_many_frames(count: u32) {
        let (alice, bob) = duplex(64);
        let mut alice_framed = Framed::new(alice, MultiplexingFrameV3Codec::new());
        let mut bob_framed = Framed::new(bob, MultiplexingFrameV3Codec::new());
        for _ in 0..count {
            bob_framed.send(minimal_frame()).await.unwrap();
        }
        drop(bob_framed);

        let mut received = 0;
        while let Some(frame) = alice_framed.next().await {
            frame.unwrap();
            received += 1;
        }

        assert_eq!(count, received);
    }

    #[tokio::test]
    async fn send_one_frame() {
        send_many_frames(1).await;
    }

    #[tokio::test]
    async fn send_two_frames() {
        send_many_frames(2).await;
    }

    async fn roundtrip(message: Message, frame_codec: impl MultiplexingFrameCodec + 'static) {
        let codec = MultiplexingMessageCodec::new(Box::new(frame_codec));
        let (alice, bob) = duplex(64);
        let mut alice_framed = Framed::new(alice, codec.clone());
        let mut bob_framed = Framed::new(bob, codec);
        bob_framed.send(message.clone()).await.unwrap();
        let deserialized_message = alice_framed.next().await.unwrap().unwrap();
        assert!(message.eq(&deserialized_message));
    }

    #[tokio::test]
    async fn offer_no_window_size() {
        roundtrip(
            Message::Offer(
                qualified_channel(),
                OfferParameters {
                    name: "hi".to_string(),
                    remote_window_size: None,
                },
            ),
            MultiplexingFrameV3Codec::new(),
        )
        .await;
    }

    #[tokio::test]
    async fn offer_with_window_size() {
        roundtrip(
            Message::Offer(
                qualified_channel(),
                OfferParameters {
                    name: "hi".to_string(),
                    remote_window_size: Some(35),
                },
            ),
            MultiplexingFrameV3Codec::new(),
        )
        .await;
    }

    #[tokio::test]
    async fn acceptance_no_window_size() {
        roundtrip(
            Message::Acceptance(
                qualified_channel(),
                AcceptanceParameters {
                    remote_window_size: None,
                },
            ),
            MultiplexingFrameV3Codec::new(),
        )
        .await;
    }

    #[tokio::test]
    async fn acceptance_with_window_size() {
        roundtrip(
            Message::Acceptance(
                qualified_channel(),
                AcceptanceParameters {
                    remote_window_size: Some(64),
                },
            ),
            MultiplexingFrameV3Codec::new(),
        )
        .await;
    }

    #[tokio::test]
    async fn content_processed() {
        roundtrip(
            Message::ContentProcessed(qualified_channel(), ContentProcessed(13)),
            MultiplexingFrameV3Codec::new(),
        )
        .await;
    }
}
