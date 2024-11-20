use msgpack_simple;

#[derive(Debug)]
pub enum MultiplexingStreamError {
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
