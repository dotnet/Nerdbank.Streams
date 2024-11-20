use std::sync::Arc;

use super::{
    channel_source::ChannelSource,
    control_code::ControlCode,
    error::MultiplexingStreamError,
    frame::{
        AcceptanceParameters, ContentProcessed, Frame, FrameHeader, OfferParameters,
        FRAME_PAYLOAD_MAX_LENGTH,
    },
    ProtocolMajorVersion, QualifiedChannelId,
};
use async_trait::async_trait;
use bytes::BytesMut;
use msgpack_simple::MsgPack;
use tokio::{
    io::{
        split, AsyncRead, AsyncReadExt, AsyncWrite, AsyncWriteExt, DuplexStream, ReadHalf,
        WriteHalf,
    },
    sync::mpsc::{self, UnboundedReceiver, UnboundedSender},
    task::{self, JoinHandle},
};

pub trait Serializer: Sync {
    fn serialize_offer(&self, offer: &OfferParameters) -> Vec<u8>;
    fn deserialize_offer(&self, offer: &[u8]) -> Result<OfferParameters, MultiplexingStreamError>;

    fn serialize_acceptance(&self, accept: &AcceptanceParameters) -> Vec<u8>;
    fn deserialize_acceptance(
        &self,
        acceptance: &[u8],
    ) -> Result<AcceptanceParameters, MultiplexingStreamError>;

    fn serialize_content_processed(&self, bytes_processed: &ContentProcessed) -> Vec<u8>;
    fn deserialize_content_processed(
        &self,
        bytes_processed: &[u8],
    ) -> Result<ContentProcessed, MultiplexingStreamError>;
}

#[async_trait]
pub trait FrameSerializer: Sync {
    async fn write_frame<W: AsyncWrite + Send + Unpin>(
        &self,
        writer: &mut W,
        frame: Frame,
    ) -> Result<(), MultiplexingStreamError>;

    async fn read_frame<R: AsyncRead + Send + Unpin>(
        &self,
        reader: &mut R,
    ) -> Result<Option<Frame>, MultiplexingStreamError>;
}

#[derive(Clone)]
struct MsgPackSerializer {
    major_version: ProtocolMajorVersion,
}

impl Serializer for MsgPackSerializer {
    fn serialize_offer(&self, offer: &OfferParameters) -> Vec<u8> {
        let mut vec = Vec::new();
        vec.push(MsgPack::String(offer.name.clone()));
        if let Some(remote_window_size) = offer.remote_window_size {
            vec.push(MsgPack::Uint(remote_window_size));
        }

        MsgPack::Array(vec).encode()
    }

    fn deserialize_offer(&self, offer: &[u8]) -> Result<OfferParameters, MultiplexingStreamError> {
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

    fn serialize_acceptance(&self, accept: &AcceptanceParameters) -> Vec<u8> {
        let mut vec = Vec::new();
        if let Some(remote_window_size) = accept.remote_window_size {
            vec.push(MsgPack::Uint(remote_window_size));
        }

        MsgPack::Array(vec).encode()
    }

    fn deserialize_acceptance(
        &self,
        acceptance: &[u8],
    ) -> Result<AcceptanceParameters, MultiplexingStreamError> {
        let acceptance = MsgPack::parse(acceptance)?;
        let array = acceptance.as_array()?;

        Ok(AcceptanceParameters {
            remote_window_size: array.get(0).map(|a| a.clone().as_uint()).transpose()?,
        })
    }

    fn serialize_content_processed(&self, bytes_processed: &ContentProcessed) -> Vec<u8> {
        MsgPack::Array(vec![MsgPack::Uint(bytes_processed.0)]).encode()
    }

    fn deserialize_content_processed(
        &self,
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

#[async_trait]
impl FrameSerializer for MsgPackSerializer {
    async fn write_frame<W: AsyncWrite + Send + Unpin>(
        &self,
        writer: &mut W,
        frame: Frame,
    ) -> Result<(), MultiplexingStreamError> {
        let element_count = match (frame.header.channel_id.is_some(), frame.payload.is_empty()) {
            (false, true) => panic!("Frames with payloads must include a channel id."),
            (true, true) => 4,
            (true, false) => 3,
            (false, false) => 1,
        };

        let mut frame_header_writer = rmp::encode::ByteBuf::new();
        rmp::encode::write_array_len(&mut frame_header_writer, element_count)?;

        rmp::encode::write_uint(&mut frame_header_writer, u8::from(frame.header.code) as u64)?;

        if let Some(channel_id) = frame.header.channel_id {
            rmp::encode::write_uint(&mut frame_header_writer, channel_id.id)?;
            rmp::encode::write_sint(&mut frame_header_writer, i8::from(channel_id.source) as i64)?;

            if !frame.payload.is_empty() {
                rmp::encode::write_bin_len(
                    &mut frame_header_writer,
                    u32::try_from(frame.payload.len()).map_err(|e| {
                        MultiplexingStreamError::PayloadTooLarge(frame.payload.len())
                    })?,
                )?;
            }
        }

        writer.write_all(frame_header_writer.as_slice()).await?;
        if !frame.payload.is_empty() {
            writer.write_all(&frame.payload).await?;
        }

        Ok(())
    }

    async fn read_frame<R: AsyncRead + Send + Unpin>(
        &self,
        reader: &mut R,
    ) -> Result<Option<Frame>, MultiplexingStreamError> {
        const FRAME_HEADER_MAX_LENGTH: usize = 50; // this is an over-estimate.
        const MAX_FRAME_SIZE: usize = FRAME_HEADER_MAX_LENGTH + FRAME_PAYLOAD_MAX_LENGTH;
        let mut frame_buffer = BytesMut::with_capacity(MAX_FRAME_SIZE);

        reader.read_buf(&mut frame_buffer).await?;

        todo!()
    }
}

// used for v1-v2
fn create_frame_header(
    code: ControlCode,
    channel_id: Option<u64>,
    source: Option<ChannelSource>,
    is_odd_endpoint: bool,
) -> FrameHeader {
    let qualified_id = channel_id.map(|id| QualifiedChannelId {
        id,
        source: source.unwrap_or_else(|| {
            let channel_is_odd = id % 2 == 1;
            if channel_is_odd == is_odd_endpoint {
                ChannelSource::Remote
            } else {
                ChannelSource::Local
            }
        }),
    });
    FrameHeader {
        code,
        channel_id: qualified_id,
    }
}

#[async_trait]
pub trait FrameIO: Sync {
    fn serializer(&self) -> &Box<dyn Serializer>;

    fn write_frame(&self, frame: Frame) -> Result<(), MultiplexingStreamError>;
    async fn read_frame(&self) -> Result<Option<Frame>, MultiplexingStreamError>;
}

pub struct V3Formatter {
    outgoing_frames: UnboundedSender<Frame>,
    incoming_frames: UnboundedReceiver<Frame>,
    reader: JoinHandle<Result<(), MultiplexingStreamError>>,
    writer: JoinHandle<Result<(), MultiplexingStreamError>>,
    serializer: Box<dyn Serializer>,
}

impl V3Formatter {
    pub fn new(duplex: DuplexStream) -> Self {
        let (mut reader, mut writer) = split(duplex);

        let serializer = MsgPackSerializer {
            major_version: ProtocolMajorVersion::V3,
        };

        let outbound_serializer = serializer.clone();
        let (outbound_sender, mut outbound_receiver) = mpsc::unbounded_channel();
        let outbound_runner: JoinHandle<Result<(), MultiplexingStreamError>> =
            task::spawn(async move {
                while let Some(frame) = outbound_receiver.recv().await {
                    outbound_serializer.write_frame(&mut writer, frame).await?;
                }

                Ok(())
            });

        let inbound_serializer = serializer.clone();
        let (inbound_sender, inbound_receiver) = mpsc::unbounded_channel();
        let inbound_runner: JoinHandle<Result<(), MultiplexingStreamError>> =
            task::spawn(async move {
                while let Some(frame) = inbound_serializer.read_frame(&mut reader).await? {
                    inbound_sender
                        .send(frame)
                        .map_err(|_| MultiplexingStreamError::ChannelFailure)?;
                }

                Ok(())
            });

        V3Formatter {
            serializer: Box::new(serializer),
            writer: outbound_runner,
            reader: inbound_runner,
            outgoing_frames: outbound_sender,
            incoming_frames: inbound_receiver,
        }
    }
}

#[async_trait]
impl FrameIO for V3Formatter {
    fn serializer(&self) -> &Box<dyn Serializer> {
        &self.serializer
    }

    fn write_frame(&self, frame: Frame) -> Result<(), MultiplexingStreamError> {
        self.outgoing_frames
            .send(frame)
            .map_err(|_| MultiplexingStreamError::ChannelFailure)?;
        Ok(())
    }

    async fn read_frame(&self) -> Result<Option<Frame>, MultiplexingStreamError> {
        todo!()
    }
}

#[cfg(test)]
mod tests {
    use std::io::BufWriter;

    use tokio::io::duplex;

    use super::*;

    #[test]
    fn offer_no_window_size() {
        let f = MsgPackSerializer {
            major_version: ProtocolMajorVersion::V3,
        };
        let offer = OfferParameters {
            name: "hi".to_string(),
            remote_window_size: None,
        };
        assert_eq!(
            offer,
            f.deserialize_offer(&f.serialize_offer(&offer)).unwrap()
        );
    }

    #[test]
    fn offer_with_window_size() {
        let f = MsgPackSerializer {
            major_version: ProtocolMajorVersion::V3,
        };
        let offer = OfferParameters {
            name: "hi".to_string(),
            remote_window_size: Some(38),
        };
        assert_eq!(
            offer,
            f.deserialize_offer(&f.serialize_offer(&offer)).unwrap()
        );
    }

    #[test]
    fn acceptance_no_window_size() {
        let f = MsgPackSerializer {
            major_version: ProtocolMajorVersion::V3,
        };
        let acceptance = AcceptanceParameters {
            remote_window_size: None,
        };
        assert_eq!(
            acceptance,
            f.deserialize_acceptance(&f.serialize_acceptance(&acceptance))
                .unwrap()
        );
    }

    #[test]
    fn acceptance_with_window_size() {
        let f = MsgPackSerializer {
            major_version: ProtocolMajorVersion::V3,
        };
        let acceptance = AcceptanceParameters {
            remote_window_size: Some(64),
        };
        assert_eq!(
            acceptance,
            f.deserialize_acceptance(&f.serialize_acceptance(&acceptance))
                .unwrap()
        );
    }

    #[test]
    fn content_processed() {
        let f = MsgPackSerializer {
            major_version: ProtocolMajorVersion::V3,
        };
        let content = ContentProcessed(13);
        assert_eq!(
            content,
            f.deserialize_content_processed(&f.serialize_content_processed(&content))
                .unwrap()
        );
    }

    async fn roundtrip_frame<S: FrameSerializer>(f: S, frame: Frame) {
        let (mut writer, mut reader) = duplex(64);
        f.write_frame(&mut writer, frame.clone()).await.unwrap();
        let deserialized_frame = f.read_frame(&mut reader).await.unwrap();
        assert!(matches!(deserialized_frame, Some(f) if f.eq(&frame)));
    }

    #[tokio::test]
    async fn frame_all_fields() {
        let f = MsgPackSerializer {
            major_version: ProtocolMajorVersion::V3,
        };
        let frame = Frame {
            header: FrameHeader {
                channel_id: Some(QualifiedChannelId {
                    id: 5,
                    source: ChannelSource::Local,
                }),
                code: ControlCode::Offer,
            },
            payload: vec![1, 2, 3],
        };

        roundtrip_frame(f, frame);
    }

    #[test]
    fn frame_no_payload() {
        let f = MsgPackSerializer {
            major_version: ProtocolMajorVersion::V3,
        };
        let frame = Frame {
            header: FrameHeader {
                channel_id: Some(QualifiedChannelId {
                    id: 5,
                    source: ChannelSource::Local,
                }),
                code: ControlCode::Offer,
            },
            payload: vec![],
        };
        roundtrip_frame(f, frame);
    }

    #[test]
    fn frame_no_payload_or_channel_id() {
        let f = MsgPackSerializer {
            major_version: ProtocolMajorVersion::V3,
        };
        let frame = Frame {
            header: FrameHeader {
                channel_id: None,
                code: ControlCode::Offer,
            },
            payload: vec![],
        };
        roundtrip_frame(f, frame);
    }
}
