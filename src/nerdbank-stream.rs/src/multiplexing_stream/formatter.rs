use super::{
    channel_source::ChannelSource,
    control_code::ControlCode,
    error::MultiplexingStreamError,
    frame::{AcceptanceParameters, ContentProcessed, Frame, FrameHeader, OfferParameters},
    ProtocolMajorVersion, QualifiedChannelId,
};
use async_trait::async_trait;
use msgpack_simple::MsgPack;
use tokio::io::DuplexStream;

pub trait Serializer: Sync {
    fn serialize_frame(&self, frame: Frame) -> Vec<u8>;
    fn deserialize_frame(&self, frame: &[u8]) -> Result<Frame, MultiplexingStreamError>;

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

struct MsgPackSerializer {
    major_version: ProtocolMajorVersion,
}

impl Serializer for MsgPackSerializer {
    fn serialize_frame(&self, frame: Frame) -> Vec<u8> {
        let mut elements = Vec::new();

        elements.push(MsgPack::Uint(u8::from(frame.header.code) as u64));
        if frame.header.channel_id.is_some() || frame.payload.len() > 0 {
            if let Some(channel_id) = frame.header.channel_id {
                elements.push(MsgPack::Uint(channel_id.id));
                match self.major_version {
                    ProtocolMajorVersion::V3 => {
                        elements.push(MsgPack::Int(i8::from(channel_id.source) as i64));
                    }
                }
            } else {
                panic!("Frames with payloads must include a channel id.");
            }

            if frame.payload.len() > 0 {
                elements.push(MsgPack::Binary(frame.payload));
            }
        }

        return MsgPack::Array(elements).encode();
    }

    fn deserialize_frame(&self, frame: &[u8]) -> Result<Frame, MultiplexingStreamError> {
        let frame = MsgPack::parse(frame)?;
        let array = frame.as_array()?;
        let code = ControlCode::try_from(
            array
                .get(0)
                .ok_or(MultiplexingStreamError::ProtocolViolation(
                    "Missing frame code".to_string(),
                ))?
                .clone()
                .as_uint()?,
        )?;
        let channel_id = match self.major_version {
            ProtocolMajorVersion::V3 => {
                if array.len() >= 3 {
                    Some(QualifiedChannelId {
                        id: array.get(1).unwrap().clone().as_uint()?,
                        source: ChannelSource::try_from(array.get(2).unwrap().clone().as_int()?)?,
                    })
                } else {
                    if array.len() == 2 {
                        return Err(MultiplexingStreamError::ProtocolViolation(
                            "Unexpected array length.".to_string(),
                        ));
                    }
                    None
                }
            }
        };
        let payload = array.get(3).map_or_else(
            || Ok::<_, MultiplexingStreamError>(Vec::new()),
            |v| Ok(v.clone().as_binary()?),
        )?;

        Ok(Frame {
            header: FrameHeader { code, channel_id },
            payload,
        })
    }

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

    async fn write_frame(
        &self,
        header: FrameHeader,
        payload: Vec<u8>,
    ) -> Result<(), MultiplexingStreamError>;

    async fn read_frame(&self) -> Result<Option<(FrameHeader, Vec<u8>)>, MultiplexingStreamError>;
}
pub struct V3Formatter {
    serializer: Box<dyn Serializer>,
    duplex: DuplexStream,
}

impl V3Formatter {
    pub fn new(duplex: DuplexStream) -> Self {
        V3Formatter {
            duplex,
            serializer: Box::new(MsgPackSerializer {
                major_version: ProtocolMajorVersion::V3,
            }),
        }
    }
}

#[async_trait]
impl FrameIO for V3Formatter {
    fn serializer(&self) -> &Box<dyn Serializer> {
        &self.serializer
    }

    async fn write_frame(
        &self,
        header: FrameHeader,
        payload: Vec<u8>,
    ) -> Result<(), MultiplexingStreamError> {
        self.serializer.serialize_frame(Frame { header, payload });
        todo!()
    }

    async fn read_frame(&self) -> Result<Option<(FrameHeader, Vec<u8>)>, MultiplexingStreamError> {
        todo!()
    }
}

#[cfg(test)]
mod tests {
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

    #[test]
    fn frame_all_fields() {
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
        assert_eq!(
            frame,
            f.deserialize_frame(&f.serialize_frame(frame.clone()))
                .unwrap()
        );
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
        assert_eq!(
            frame,
            f.deserialize_frame(&f.serialize_frame(frame.clone()))
                .unwrap()
        );
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
        assert_eq!(
            frame,
            f.deserialize_frame(&f.serialize_frame(frame.clone()))
                .unwrap()
        );
    }
}
