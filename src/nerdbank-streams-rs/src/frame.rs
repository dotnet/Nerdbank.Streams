use std::cmp::Ordering;

use rand::Rng;
use rmp::encode;
use tokio::io::{AsyncRead, AsyncReadExt, AsyncWrite, AsyncWriteExt};

use crate::multiplexor::{ChannelId, ChannelSource, Error, ProtocolVersion};

pub(crate) const MAX_FRAME_PAYLOAD: usize = 20 * 1024;
const V1_MAGIC: [u8; 4] = [0x2f, 0xdf, 0x1d, 0x50];

#[derive(Clone, Copy, Debug, Eq, PartialEq)]
#[repr(u8)]
pub(crate) enum Code {
    Offer = 0,
    OfferAccepted = 1,
    Content = 2,
    ContentWritingCompleted = 3,
    ChannelTerminated = 4,
    ContentProcessed = 5,
    ContentReadingCompleted = 6,
    ChannelWindowAdjust = 7,
    ChannelWindowGrowthRequest = 8,
}

impl TryFrom<u64> for Code {
    type Error = Error;

    fn try_from(value: u64) -> Result<Self, Self::Error> {
        match value {
            0 => Ok(Self::Offer),
            1 => Ok(Self::OfferAccepted),
            2 => Ok(Self::Content),
            3 => Ok(Self::ContentWritingCompleted),
            4 => Ok(Self::ChannelTerminated),
            5 => Ok(Self::ContentProcessed),
            6 => Ok(Self::ContentReadingCompleted),
            7 => Ok(Self::ChannelWindowAdjust),
            8 => Ok(Self::ChannelWindowGrowthRequest),
            _ => Err(Error::Protocol(format!(
                "unsupported MultiplexingStream control code {value}"
            ))),
        }
    }
}

#[derive(Clone, Debug)]
pub(crate) struct Frame {
    pub(crate) code: Code,
    pub(crate) channel: Option<ChannelId>,
    pub(crate) payload: Vec<u8>,
}

pub(crate) async fn write_handshake<W>(
    writer: &mut W,
    version: ProtocolVersion,
) -> Result<Option<[u8; 16]>, Error>
where
    W: AsyncWrite + Unpin,
{
    match version {
        ProtocolVersion::V1 => {
            let random = random_bytes();
            writer.write_all(&V1_MAGIC).await.map_err(Error::from)?;
            writer.write_all(&random).await.map_err(Error::from)?;
            writer.flush().await.map_err(Error::from)?;
            Ok(Some(random))
        }
        ProtocolVersion::V2 => {
            let random = random_bytes();
            let mut message = Vec::with_capacity(24);
            pack_array(&mut message, 2);
            pack_array(&mut message, 2);
            pack_uint(&mut message, 2);
            pack_uint(&mut message, 0);
            pack_bin(&mut message, &random);
            writer.write_all(&message).await.map_err(Error::from)?;
            writer.flush().await.map_err(Error::from)?;
            Ok(Some(random))
        }
        ProtocolVersion::V3 => Ok(None),
    }
}

pub(crate) async fn read_handshake<R>(
    reader: &mut R,
    version: ProtocolVersion,
    local_random: Option<[u8; 16]>,
) -> Result<Option<bool>, Error>
where
    R: AsyncRead + Unpin,
{
    match version {
        ProtocolVersion::V1 => {
            let mut bytes = [0_u8; 20];
            reader.read_exact(&mut bytes).await.map_err(Error::from)?;
            if bytes[..4] != V1_MAGIC {
                return Err(Error::Protocol("v1 handshake magic number mismatch".into()));
            }

            determine_odd(
                local_random.ok_or_else(|| Error::Protocol("v1 local handshake missing".into()))?,
                bytes[4..].try_into().expect("v1 random length is fixed"),
            )
            .map(Some)
        }
        ProtocolVersion::V2 => {
            let element_count = read_array_len(reader).await?;
            if element_count < 2 {
                return Err(Error::Protocol("v2 handshake has too few elements".into()));
            }
            let version_count = read_array_len(reader).await?;
            if version_count < 2 {
                return Err(Error::Protocol(
                    "v2 handshake version has too few elements".into(),
                ));
            }
            let major = read_uint(reader).await?;
            let _minor = read_uint(reader).await?;
            for _ in 2..version_count {
                skip_value(reader).await?;
            }
            if major != 2 {
                return Err(Error::Protocol(format!(
                    "incompatible v2 handshake major version {major}"
                )));
            }

            let remote_random = read_bin(reader).await?;
            if remote_random.len() != 16 {
                return Err(Error::Protocol(
                    "v2 handshake random number is invalid".into(),
                ));
            }
            for _ in 2..element_count {
                skip_value(reader).await?;
            }
            determine_odd(
                local_random.ok_or_else(|| Error::Protocol("v2 local handshake missing".into()))?,
                remote_random.try_into().expect("length checked"),
            )
            .map(Some)
        }
        ProtocolVersion::V3 => Ok(None),
    }
}

pub(crate) async fn write_frame<W>(
    writer: &mut W,
    version: ProtocolVersion,
    frame: &Frame,
) -> Result<(), Error>
where
    W: AsyncWrite + Unpin,
{
    if frame.payload.len() > MAX_FRAME_PAYLOAD {
        return Err(Error::Protocol("frame payload exceeds 20 KiB".into()));
    }

    match version {
        ProtocolVersion::V1 => {
            let id = frame.channel.map_or(0, |channel| channel.id);
            if id > u32::MAX as u64 {
                return Err(Error::Protocol("v1 channel ID exceeds UInt32".into()));
            }
            let length = u16::try_from(frame.payload.len())
                .map_err(|_| Error::Protocol("v1 frame payload exceeds UInt16".into()))?;
            writer
                .write_all(&[frame.code as u8])
                .await
                .map_err(Error::from)?;
            writer
                .write_all(&(id as u32).to_be_bytes())
                .await
                .map_err(Error::from)?;
            writer
                .write_all(&length.to_be_bytes())
                .await
                .map_err(Error::from)?;
            writer
                .write_all(&frame.payload)
                .await
                .map_err(Error::from)?;
        }
        ProtocolVersion::V2 | ProtocolVersion::V3 => {
            if frame.payload.is_empty() {
                if let Some(channel) = frame.channel {
                    let mut encoded = Vec::with_capacity(16);
                    if version == ProtocolVersion::V2 {
                        pack_array(&mut encoded, 2);
                        pack_uint(&mut encoded, frame.code as u64);
                        pack_uint(&mut encoded, channel.id);
                    } else {
                        pack_array(&mut encoded, 3);
                        pack_uint(&mut encoded, frame.code as u64);
                        pack_uint(&mut encoded, channel.id);
                        pack_int(&mut encoded, channel.source as i8 as i64);
                    }
                    writer.write_all(&encoded).await.map_err(Error::from)?;
                } else {
                    writer
                        .write_all(&[0x91, frame.code as u8])
                        .await
                        .map_err(Error::from)?;
                }
            } else {
                let channel = frame
                    .channel
                    .ok_or_else(|| Error::Protocol("payload requires a channel ID".into()))?;
                let mut encoded = Vec::with_capacity(frame.payload.len() + 32);
                if version == ProtocolVersion::V2 {
                    pack_array(&mut encoded, 3);
                    pack_uint(&mut encoded, frame.code as u64);
                    pack_uint(&mut encoded, channel.id);
                } else {
                    pack_array(&mut encoded, 4);
                    pack_uint(&mut encoded, frame.code as u64);
                    pack_uint(&mut encoded, channel.id);
                    pack_int(&mut encoded, channel.source as i8 as i64);
                }
                pack_bin(&mut encoded, &frame.payload);
                writer.write_all(&encoded).await.map_err(Error::from)?;
            }
        }
    }
    writer.flush().await.map_err(Error::from)
}

pub(crate) async fn read_frame<R>(
    reader: &mut R,
    version: ProtocolVersion,
    local_is_odd: Option<bool>,
) -> Result<Option<Frame>, Error>
where
    R: AsyncRead + Unpin,
{
    match version {
        ProtocolVersion::V1 => read_v1_frame(reader, local_is_odd).await,
        ProtocolVersion::V2 | ProtocolVersion::V3 => {
            read_message_pack_frame(reader, version, local_is_odd).await
        }
    }
}

pub(crate) fn encode_offer(
    version: ProtocolVersion,
    name: &str,
    window: usize,
) -> Result<Vec<u8>, Error> {
    if version == ProtocolVersion::V1 {
        let bytes = name.as_bytes();
        if bytes.len() > MAX_FRAME_PAYLOAD {
            return Err(Error::Protocol("channel name exceeds 20 KiB".into()));
        }
        return Ok(bytes.to_vec());
    }

    let mut payload = Vec::with_capacity(name.len() + 16);
    pack_array(&mut payload, 2);
    pack_str(&mut payload, name);
    pack_uint(&mut payload, window as u64);
    Ok(payload)
}

pub(crate) fn encode_acceptance(version: ProtocolVersion, window: usize) -> Vec<u8> {
    if version == ProtocolVersion::V1 {
        return Vec::new();
    }
    let mut payload = Vec::with_capacity(10);
    pack_array(&mut payload, 1);
    pack_uint(&mut payload, window as u64);
    payload
}

pub(crate) fn decode_acceptance(
    version: ProtocolVersion,
    payload: &[u8],
) -> Result<Option<usize>, Error> {
    if version == ProtocolVersion::V1 {
        return Ok(None);
    }
    let mut cursor = SliceReader::new(payload);
    let count = cursor.array_len()?;
    let window = if count > 0 {
        Some(usize_from_u64(cursor.uint()?)?)
    } else {
        None
    };
    if !cursor.is_empty() {
        return Err(Error::Protocol(
            "acceptance payload has trailing data".into(),
        ));
    }
    Ok(window)
}

pub(crate) fn encode_processed(bytes: usize) -> Vec<u8> {
    let mut payload = Vec::with_capacity(10);
    pack_array(&mut payload, 1);
    pack_uint(&mut payload, bytes as u64);
    payload
}

pub(crate) fn decode_processed(payload: &[u8]) -> Result<usize, Error> {
    let mut cursor = SliceReader::new(payload);
    if cursor.array_len()? == 0 {
        return Err(Error::Protocol(
            "processed payload has too few elements".into(),
        ));
    }

    let result = usize_from_u64(cursor.uint()?)?;
    if !cursor.is_empty() {
        return Err(Error::Protocol(
            "processed payload has trailing data".into(),
        ));
    }
    Ok(result)
}

/// Encodes an absolute channel receive-window size for `ChannelWindowAdjust`.
///
/// Window adjustments intentionally share the one-element integer payload
/// format used by `ContentProcessed`, matching the .NET formatter.
pub(crate) fn encode_window_adjust(window: usize) -> Vec<u8> {
    encode_processed(window)
}

/// Decodes an absolute channel receive-window size from `ChannelWindowAdjust`.
pub(crate) fn decode_window_adjust(payload: &[u8]) -> Result<usize, Error> {
    decode_processed(payload)
}

pub(crate) fn encode_error(message: &str) -> Vec<u8> {
    let mut payload = Vec::with_capacity(message.len() + 8);
    pack_array(&mut payload, 1);
    pack_str(&mut payload, message);
    payload
}

pub(crate) fn decode_error(payload: &[u8]) -> Result<Option<String>, Error> {
    if payload.is_empty() {
        return Ok(None);
    }
    let mut cursor = SliceReader::new(payload);
    if cursor.array_len()? == 0 {
        return Ok(None);
    }
    let message = cursor.string()?;
    if !cursor.is_empty() {
        return Err(Error::Protocol(
            "termination payload has trailing data".into(),
        ));
    }
    Ok(Some(message))
}

async fn read_v1_frame<R>(
    reader: &mut R,
    local_is_odd: Option<bool>,
) -> Result<Option<Frame>, Error>
where
    R: AsyncRead + Unpin,
{
    let Some(code_byte) = read_first(reader).await? else {
        return Ok(None);
    };
    let code = Code::try_from(u64::from(code_byte))?;
    let mut remaining = [0_u8; 6];
    reader
        .read_exact(&mut remaining)
        .await
        .map_err(Error::from)?;
    let id = u64::from(u32::from_be_bytes(
        remaining[..4].try_into().expect("fixed slice"),
    ));
    let payload_length = usize::from(u16::from_be_bytes(
        remaining[4..].try_into().expect("fixed slice"),
    ));
    if payload_length > MAX_FRAME_PAYLOAD {
        return Err(Error::Protocol("v1 frame payload exceeds 20 KiB".into()));
    }
    let mut payload = vec![0; payload_length];
    reader.read_exact(&mut payload).await.map_err(Error::from)?;
    let channel = if id == 0 {
        None
    } else {
        Some(ChannelId {
            id,
            source: source_from_v12(id, local_is_odd)?,
        })
    };
    Ok(Some(Frame {
        code,
        channel,
        payload,
    }))
}

async fn read_message_pack_frame<R>(
    reader: &mut R,
    version: ProtocolVersion,
    local_is_odd: Option<bool>,
) -> Result<Option<Frame>, Error>
where
    R: AsyncRead + Unpin,
{
    let Some(first) = read_first(reader).await? else {
        return Ok(None);
    };
    let count = read_array_len_from_marker(reader, first).await?;
    if count == 0 {
        return Err(Error::Protocol("frame has too few elements".into()));
    }
    let code = Code::try_from(read_uint(reader).await?)?;
    let channel = if count > 1 {
        if version == ProtocolVersion::V3 {
            if count < 3 {
                return Err(Error::Protocol("v3 frame lacks a channel source".into()));
            }
            let id = read_uint(reader).await?;
            let source = match read_int(reader).await? {
                -1 => ChannelSource::Remote,
                0 => ChannelSource::Seeded,
                1 => ChannelSource::Local,
                value => return Err(Error::Protocol(format!("invalid channel source {value}"))),
            };
            Some(ChannelId {
                id,
                source: source.flipped(),
            })
        } else {
            let id = read_uint(reader).await?;
            Some(ChannelId {
                id,
                source: source_from_v12(id, local_is_odd)?,
            })
        }
    } else {
        None
    };

    let payload_index = if version == ProtocolVersion::V3 { 4 } else { 3 };
    let payload = if count >= payload_index {
        read_bin(reader).await?
    } else {
        Vec::new()
    };
    if payload.len() > MAX_FRAME_PAYLOAD {
        return Err(Error::Protocol("frame payload exceeds 20 KiB".into()));
    }
    if count != 1 && count != payload_index - 1 && count != payload_index {
        return Err(Error::Protocol("frame has an invalid element count".into()));
    }
    Ok(Some(Frame {
        code,
        channel,
        payload,
    }))
}

fn source_from_v12(id: u64, local_is_odd: Option<bool>) -> Result<ChannelSource, Error> {
    let local_is_odd =
        local_is_odd.ok_or_else(|| Error::Protocol("v1/v2 handshake was not completed".into()))?;
    Ok(if (id % 2 == 1) == local_is_odd {
        ChannelSource::Local
    } else {
        ChannelSource::Remote
    })
}

fn random_bytes() -> [u8; 16] {
    rand::rng().random()
}

fn determine_odd(local: [u8; 16], remote: [u8; 16]) -> Result<bool, Error> {
    match local.cmp(&remote) {
        Ordering::Greater => Ok(true),
        Ordering::Less => Ok(false),
        Ordering::Equal => Err(Error::Protocol(
            "handshake random numbers are identical".into(),
        )),
    }
}

async fn read_first<R>(reader: &mut R) -> Result<Option<u8>, Error>
where
    R: AsyncRead + Unpin,
{
    let mut first = [0_u8; 1];
    match reader.read(&mut first).await {
        Ok(0) => Ok(None),
        Ok(_) => Ok(Some(first[0])),
        Err(error) => Err(Error::from(error)),
    }
}

async fn read_marker<R>(reader: &mut R) -> Result<u8, Error>
where
    R: AsyncRead + Unpin,
{
    read_first(reader)
        .await?
        .ok_or_else(|| Error::Protocol("unexpected end of MessagePack value".into()))
}

async fn read_array_len<R>(reader: &mut R) -> Result<usize, Error>
where
    R: AsyncRead + Unpin,
{
    let marker = read_marker(reader).await?;
    read_array_len_from_marker(reader, marker).await
}

async fn read_array_len_from_marker<R>(reader: &mut R, marker: u8) -> Result<usize, Error>
where
    R: AsyncRead + Unpin,
{
    match marker {
        0x90..=0x9f => Ok(usize::from(marker & 0x0f)),
        0xdc => Ok(usize::from(read_u16(reader).await?)),
        0xdd => usize_from_u64(u64::from(read_u32(reader).await?)),
        _ => Err(Error::Protocol("expected MessagePack array".into())),
    }
}

async fn read_uint<R>(reader: &mut R) -> Result<u64, Error>
where
    R: AsyncRead + Unpin,
{
    let marker = read_marker(reader).await?;
    match marker {
        0x00..=0x7f => Ok(u64::from(marker)),
        0xcc => Ok(u64::from(read_u8(reader).await?)),
        0xcd => Ok(u64::from(read_u16(reader).await?)),
        0xce => Ok(u64::from(read_u32(reader).await?)),
        0xcf => read_u64(reader).await,
        0xd0 => {
            let value = read_u8(reader).await? as i8;
            u64::try_from(value).map_err(|_| Error::Protocol("expected unsigned integer".into()))
        }
        0xd1 => {
            let value = read_u16(reader).await? as i16;
            u64::try_from(value).map_err(|_| Error::Protocol("expected unsigned integer".into()))
        }
        0xd2 => {
            let value = read_u32(reader).await? as i32;
            u64::try_from(value).map_err(|_| Error::Protocol("expected unsigned integer".into()))
        }
        0xd3 => {
            let value = read_u64(reader).await? as i64;
            u64::try_from(value).map_err(|_| Error::Protocol("expected unsigned integer".into()))
        }
        _ => Err(Error::Protocol("expected MessagePack integer".into())),
    }
}

async fn read_int<R>(reader: &mut R) -> Result<i64, Error>
where
    R: AsyncRead + Unpin,
{
    let marker = read_marker(reader).await?;
    match marker {
        0x00..=0x7f => Ok(i64::from(marker)),
        0xe0..=0xff => Ok(i64::from(marker as i8)),
        0xcc => Ok(i64::from(read_u8(reader).await?)),
        0xcd => Ok(i64::from(read_u16(reader).await?)),
        0xce => Ok(i64::from(read_u32(reader).await?)),
        0xcf => i64::try_from(read_u64(reader).await?)
            .map_err(|_| Error::Protocol("integer exceeds Int64".into())),
        0xd0 => Ok(i64::from(read_u8(reader).await? as i8)),
        0xd1 => Ok(i64::from(read_u16(reader).await? as i16)),
        0xd2 => Ok(i64::from(read_u32(reader).await? as i32)),
        0xd3 => Ok(read_u64(reader).await? as i64),
        _ => Err(Error::Protocol("expected MessagePack integer".into())),
    }
}

async fn read_bin<R>(reader: &mut R) -> Result<Vec<u8>, Error>
where
    R: AsyncRead + Unpin,
{
    let marker = read_marker(reader).await?;
    let length = match marker {
        0xc4 => usize::from(read_u8(reader).await?),
        0xc5 => usize::from(read_u16(reader).await?),
        0xc6 => usize_from_u64(u64::from(read_u32(reader).await?))?,
        _ => return Err(Error::Protocol("expected MessagePack binary data".into())),
    };
    if length > MAX_FRAME_PAYLOAD.max(16) {
        return Err(Error::Protocol(
            "MessagePack binary value is too large".into(),
        ));
    }
    let mut bytes = vec![0; length];
    reader.read_exact(&mut bytes).await.map_err(Error::from)?;
    Ok(bytes)
}

async fn skip_value<R>(reader: &mut R) -> Result<(), Error>
where
    R: AsyncRead + Unpin,
{
    let marker = read_marker(reader).await?;
    skip_value_from_marker(reader, marker).await
}

async fn skip_value_from_marker<R>(reader: &mut R, marker: u8) -> Result<(), Error>
where
    R: AsyncRead + Unpin,
{
    let size = match marker {
        0x00..=0x7f | 0x80..=0x8f | 0x90..=0x9f | 0xa0..=0xbf | 0xc0..=0xc3 | 0xe0..=0xff => {
            return match marker {
                0x90..=0x9f => {
                    for _ in 0..(marker & 0x0f) {
                        Box::pin(skip_value(reader)).await?;
                    }
                    Ok(())
                }
                0x80..=0x8f => Err(Error::Protocol("MessagePack maps are not supported".into())),
                0xa0..=0xbf => skip_bytes(reader, usize::from(marker & 0x1f)).await,
                _ => Ok(()),
            };
        }
        0xcc | 0xd0 => 1,
        0xcd | 0xd1 => 2,
        0xce | 0xd2 | 0xca => 4,
        0xcf | 0xd3 | 0xcb => 8,
        _ => return Err(Error::Protocol("unsupported MessagePack value".into())),
    };
    skip_bytes(reader, size).await
}

async fn skip_bytes<R>(reader: &mut R, size: usize) -> Result<(), Error>
where
    R: AsyncRead + Unpin,
{
    let mut bytes = vec![0; size];
    reader.read_exact(&mut bytes).await.map_err(Error::from)?;
    Ok(())
}

async fn read_u8<R>(reader: &mut R) -> Result<u8, Error>
where
    R: AsyncRead + Unpin,
{
    read_marker(reader).await
}

async fn read_u16<R>(reader: &mut R) -> Result<u16, Error>
where
    R: AsyncRead + Unpin,
{
    let mut bytes = [0; 2];
    reader.read_exact(&mut bytes).await.map_err(Error::from)?;
    Ok(u16::from_be_bytes(bytes))
}

async fn read_u32<R>(reader: &mut R) -> Result<u32, Error>
where
    R: AsyncRead + Unpin,
{
    let mut bytes = [0; 4];
    reader.read_exact(&mut bytes).await.map_err(Error::from)?;
    Ok(u32::from_be_bytes(bytes))
}

async fn read_u64<R>(reader: &mut R) -> Result<u64, Error>
where
    R: AsyncRead + Unpin,
{
    let mut bytes = [0; 8];
    reader.read_exact(&mut bytes).await.map_err(Error::from)?;
    Ok(u64::from_be_bytes(bytes))
}

fn pack_array(target: &mut Vec<u8>, length: usize) {
    encode::write_array_len(
        target,
        u32::try_from(length).expect("MessagePack array length exceeds UInt32"),
    )
    .expect("writing to Vec cannot fail");
}

fn pack_uint(target: &mut Vec<u8>, value: u64) {
    encode::write_uint(target, value).expect("writing to Vec cannot fail");
}

fn pack_int(target: &mut Vec<u8>, value: i64) {
    encode::write_sint(target, value).expect("writing to Vec cannot fail");
}

fn pack_bin(target: &mut Vec<u8>, bytes: &[u8]) {
    encode::write_bin_len(
        target,
        u32::try_from(bytes.len()).expect("MessagePack binary length exceeds UInt32"),
    )
    .expect("writing to Vec cannot fail");
    target.extend_from_slice(bytes);
}

fn pack_str(target: &mut Vec<u8>, value: &str) {
    encode::write_str(target, value).expect("writing to Vec cannot fail");
}

fn usize_from_u64(value: u64) -> Result<usize, Error> {
    usize::try_from(value).map_err(|_| Error::Protocol("integer exceeds platform usize".into()))
}

struct SliceReader<'a> {
    data: &'a [u8],
    offset: usize,
}

impl<'a> SliceReader<'a> {
    fn new(data: &'a [u8]) -> Self {
        Self { data, offset: 0 }
    }

    fn is_empty(&self) -> bool {
        self.offset == self.data.len()
    }

    fn marker(&mut self) -> Result<u8, Error> {
        let result = self
            .data
            .get(self.offset)
            .copied()
            .ok_or_else(|| Error::Protocol("unexpected end of MessagePack value".into()))?;
        self.offset += 1;
        Ok(result)
    }

    fn take(&mut self, length: usize) -> Result<&'a [u8], Error> {
        let end = self
            .offset
            .checked_add(length)
            .ok_or_else(|| Error::Protocol("MessagePack length overflows".into()))?;
        let result = self
            .data
            .get(self.offset..end)
            .ok_or_else(|| Error::Protocol("unexpected end of MessagePack value".into()))?;
        self.offset = end;
        Ok(result)
    }

    fn array_len(&mut self) -> Result<usize, Error> {
        match self.marker()? {
            0x90..=0x9f => Ok(usize::from(self.data[self.offset - 1] & 0x0f)),
            0xdc => Ok(usize::from(u16::from_be_bytes(
                self.take(2)?.try_into().expect("fixed slice"),
            ))),
            0xdd => usize_from_u64(u64::from(u32::from_be_bytes(
                self.take(4)?.try_into().expect("fixed slice"),
            ))),
            _ => Err(Error::Protocol("expected MessagePack array".into())),
        }
    }

    fn uint(&mut self) -> Result<u64, Error> {
        let marker = self.marker()?;
        match marker {
            0x00..=0x7f => Ok(u64::from(marker)),
            0xcc => Ok(u64::from(self.take(1)?[0])),
            0xcd => Ok(u64::from(u16::from_be_bytes(
                self.take(2)?.try_into().expect("fixed slice"),
            ))),
            0xce => Ok(u64::from(u32::from_be_bytes(
                self.take(4)?.try_into().expect("fixed slice"),
            ))),
            0xcf => Ok(u64::from_be_bytes(
                self.take(8)?.try_into().expect("fixed slice"),
            )),
            _ => Err(Error::Protocol(
                "expected MessagePack unsigned integer".into(),
            )),
        }
    }

    fn string(&mut self) -> Result<String, Error> {
        let marker = self.marker()?;
        let length = match marker {
            0xa0..=0xbf => usize::from(marker & 0x1f),
            0xd9 => usize::from(self.take(1)?[0]),
            0xda => usize::from(u16::from_be_bytes(
                self.take(2)?.try_into().expect("fixed slice"),
            )),
            0xdb => usize_from_u64(u64::from(u32::from_be_bytes(
                self.take(4)?.try_into().expect("fixed slice"),
            )))?,
            _ => return Err(Error::Protocol("expected MessagePack string".into())),
        };
        String::from_utf8(self.take(length)?.to_vec())
            .map_err(|_| Error::Protocol("MessagePack string is not UTF-8".into()))
    }
}

#[cfg(test)]
mod tests {
    use super::{decode_window_adjust, encode_window_adjust, read_frame, write_frame, Code, Frame};
    use crate::multiplexor::{ChannelId, ChannelSource, ProtocolVersion};

    #[tokio::test]
    async fn window_extension_frames_round_trip_in_v3() {
        let (mut writer, mut reader) = tokio::io::duplex(128);
        let channel = ChannelId {
            id: 5,
            source: ChannelSource::Local,
        };

        write_frame(
            &mut writer,
            ProtocolVersion::V3,
            &Frame {
                code: Code::ChannelWindowAdjust,
                channel: Some(channel),
                payload: encode_window_adjust(1024),
            },
        )
        .await
        .expect("write window adjustment");
        let adjustment = read_frame(&mut reader, ProtocolVersion::V3, None)
            .await
            .expect("read window adjustment")
            .expect("window adjustment frame");
        assert_eq!(adjustment.code, Code::ChannelWindowAdjust);
        assert_eq!(
            adjustment.channel,
            Some(ChannelId {
                id: 5,
                source: ChannelSource::Remote,
            })
        );
        assert_eq!(
            decode_window_adjust(&adjustment.payload).expect("decode adjustment"),
            1024
        );

        write_frame(
            &mut writer,
            ProtocolVersion::V3,
            &Frame {
                code: Code::ChannelWindowGrowthRequest,
                channel: Some(channel),
                payload: Vec::new(),
            },
        )
        .await
        .expect("write window growth request");
        let request = read_frame(&mut reader, ProtocolVersion::V3, None)
            .await
            .expect("read window growth request")
            .expect("window growth request frame");
        assert_eq!(request.code, Code::ChannelWindowGrowthRequest);
        assert!(request.payload.is_empty());
    }
}
