use msgpack_simple;

#[derive(Debug)]
pub enum MultiplexingStreamError {
    Io(std::io::Error),
    PayloadTooLarge(usize),
    ChannelFailure,
    WriteFailure(String),
    ReadFailure(String),
    ProtocolViolation(String),
    ListeningAlreadyStarted,
    NotListening,
}

impl std::error::Error for MultiplexingStreamError {}

impl std::fmt::Display for MultiplexingStreamError {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        match self {
            MultiplexingStreamError::ProtocolViolation(message) => {
                write!(f, "Protocol violation: {}", message)
            }
            MultiplexingStreamError::ListeningAlreadyStarted => {
                write!(f, "Listening has already started.")
            }
            MultiplexingStreamError::NotListening => {
                write!(f, "Listening must start before this operation is allowed.")
            }
            MultiplexingStreamError::WriteFailure(e) => {
                write!(f, "Error writing frame: {}", e)
            }
            MultiplexingStreamError::ReadFailure(e) =>  {
                write!(f, "Error reading frame: {}", e)
            }
            MultiplexingStreamError::Io(error) => {
                write!(f, "IO error: {}", error)
            }
            MultiplexingStreamError::PayloadTooLarge(size) => {
                write!(f, "Payload too large: {} bytes", size)
            }
            MultiplexingStreamError::ChannelFailure => {
                write!(f, "Failure sending frame to channel")
            }
        }
    }
}

impl From<msgpack_simple::ParseError> for MultiplexingStreamError {
    fn from(_: msgpack_simple::ParseError) -> Self {
        MultiplexingStreamError::ProtocolViolation("msgpack decode failure".to_string())
    }
}

impl From<msgpack_simple::ConversionError> for MultiplexingStreamError {
    fn from(value: msgpack_simple::ConversionError) -> Self {
        MultiplexingStreamError::ProtocolViolation(value.attempted.to_string())
    }
}

impl From<std::io::Error> for MultiplexingStreamError {
    fn from(value: std::io::Error) -> Self {
        MultiplexingStreamError::Io(value)
    }
}

impl<E: rmp::encode::RmpWriteErr> From<rmp::encode::ValueWriteError<E>>
    for MultiplexingStreamError
{
    fn from(value: rmp::encode::ValueWriteError<E>) -> Self {
        MultiplexingStreamError::WriteFailure(format!("Msgpack write error: {}", value))
    }
}
