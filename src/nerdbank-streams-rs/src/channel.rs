use std::{
    io,
    pin::Pin,
    task::{Context, Poll},
};

use tokio::io::{AsyncRead, AsyncWrite, DuplexStream, ReadBuf, ReadHalf, WriteHalf};

use crate::multiplexor::{ChannelId, ChannelState, Error};

/// A bidirectional logical stream carried by a [`MultiplexingStream`](crate::MultiplexingStream).
///
/// It implements Tokio's [`AsyncRead`] and [`AsyncWrite`] traits. Calling
/// [`AsyncWriteExt::shutdown`](tokio::io::AsyncWriteExt::shutdown) completes
/// this endpoint's write side without closing its read side. Dropping a
/// `Channel` closes both sides. For independently owned read and write sides,
/// use [`into_split`](Self::into_split).
pub struct Channel {
    pub(crate) state: std::sync::Arc<ChannelState>,
    pub(crate) io: Option<DuplexStream>,
}

impl Channel {
    /// Returns this channel's protocol identity.
    #[must_use]
    pub fn id(&self) -> ChannelId {
        self.state.id
    }

    /// Returns the name supplied when this channel was offered.
    #[must_use]
    pub fn name(&self) -> &str {
        &self.state.name
    }

    /// Splits this channel into independently owned read and write halves.
    ///
    /// Dropping the read half sends `ContentReadingCompleted`, and dropping or
    /// shutting down the write half sends `ContentWritingCompleted`. Prefer
    /// this method when each direction is owned by a different task; generic
    /// Tokio splitting of a borrowed `Channel` cannot observe half drops.
    #[must_use]
    pub fn into_split(mut self) -> (ChannelReadHalf, ChannelWriteHalf) {
        let io = self
            .io
            .take()
            .expect("channel I/O is present until it is split");
        let (read, write) = tokio::io::split(io);
        (
            ChannelReadHalf {
                state: std::sync::Arc::clone(&self.state),
                io: read,
            },
            ChannelWriteHalf {
                state: std::sync::Arc::clone(&self.state),
                io: Some(write),
            },
        )
    }

    /// Stops receiving data and releases any peer writer blocked by flow control.
    ///
    /// This does not close the local write side. It is meaningful for protocol
    /// versions 2 and 3; version 1 treats it as a no-op.
    pub async fn close_read(&self) -> Result<(), Error> {
        self.state.close_read()
    }

    /// Terminates this channel, optionally reporting an error to the peer.
    ///
    /// Protocol version 1 does not transmit an error payload.
    pub async fn terminate(&self, error: Option<&str>) -> Result<(), Error> {
        self.state.terminate(error).await
    }

    /// Waits for this channel to close.
    ///
    /// This completes successfully after both endpoints have completed their
    /// write sides, or when the peer terminates the channel without an error.
    /// It returns an error for a local or remote termination error.
    pub async fn completion(&self) -> Result<(), Error> {
        self.state.completion().await
    }
}

impl AsyncRead for Channel {
    fn poll_read(
        mut self: Pin<&mut Self>,
        cx: &mut Context<'_>,
        buf: &mut ReadBuf<'_>,
    ) -> Poll<io::Result<()>> {
        let before = buf.filled().len();
        let result = Pin::new(
            self.io
                .as_mut()
                .expect("channel I/O is present until it is split"),
        )
        .poll_read(cx, buf);
        if matches!(result, Poll::Ready(Ok(()))) {
            let consumed = buf.filled().len() - before;
            if consumed != 0 {
                self.state.content_consumed(consumed);
            }
        }
        result
    }
}

impl AsyncWrite for Channel {
    fn poll_write(
        mut self: Pin<&mut Self>,
        cx: &mut Context<'_>,
        buf: &[u8],
    ) -> Poll<io::Result<usize>> {
        Pin::new(
            self.io
                .as_mut()
                .expect("channel I/O is present until it is split"),
        )
        .poll_write(cx, buf)
    }

    fn poll_flush(mut self: Pin<&mut Self>, cx: &mut Context<'_>) -> Poll<io::Result<()>> {
        Pin::new(
            self.io
                .as_mut()
                .expect("channel I/O is present until it is split"),
        )
        .poll_flush(cx)
    }

    fn poll_shutdown(mut self: Pin<&mut Self>, cx: &mut Context<'_>) -> Poll<io::Result<()>> {
        Pin::new(
            self.io
                .as_mut()
                .expect("channel I/O is present until it is split"),
        )
        .poll_shutdown(cx)
    }
}

impl Drop for Channel {
    fn drop(&mut self) {
        if self.io.is_some() {
            self.state.drop_channel();
        }
    }
}

/// The receiving half returned by [`Channel::into_split`].
pub struct ChannelReadHalf {
    state: std::sync::Arc<ChannelState>,
    io: ReadHalf<DuplexStream>,
}

impl ChannelReadHalf {
    /// Returns this channel's protocol identity.
    #[must_use]
    pub fn id(&self) -> ChannelId {
        self.state.id
    }

    /// Returns the name supplied when this channel was offered.
    #[must_use]
    pub fn name(&self) -> &str {
        &self.state.name
    }

    /// Stops receiving data and releases any peer writer blocked by flow control.
    pub async fn close_read(&self) -> Result<(), Error> {
        self.state.close_read()
    }

    /// Waits for this channel to close.
    pub async fn completion(&self) -> Result<(), Error> {
        self.state.completion().await
    }
}

impl AsyncRead for ChannelReadHalf {
    fn poll_read(
        mut self: Pin<&mut Self>,
        cx: &mut Context<'_>,
        buf: &mut ReadBuf<'_>,
    ) -> Poll<io::Result<()>> {
        let before = buf.filled().len();
        let result = Pin::new(&mut self.io).poll_read(cx, buf);
        if matches!(result, Poll::Ready(Ok(()))) {
            let consumed = buf.filled().len() - before;
            if consumed != 0 {
                self.state.content_consumed(consumed);
            }
        }
        result
    }
}

impl Drop for ChannelReadHalf {
    fn drop(&mut self) {
        let _ = self.state.close_read();
    }
}

/// The sending half returned by [`Channel::into_split`].
pub struct ChannelWriteHalf {
    state: std::sync::Arc<ChannelState>,
    io: Option<WriteHalf<DuplexStream>>,
}

impl ChannelWriteHalf {
    /// Returns this channel's protocol identity.
    #[must_use]
    pub fn id(&self) -> ChannelId {
        self.state.id
    }

    /// Returns the name supplied when this channel was offered.
    #[must_use]
    pub fn name(&self) -> &str {
        &self.state.name
    }

    /// Terminates this channel, optionally reporting an error to the peer.
    ///
    /// Protocol version 1 does not transmit an error payload.
    pub async fn terminate(&self, error: Option<&str>) -> Result<(), Error> {
        self.state.terminate(error).await
    }

    /// Waits for this channel to close.
    pub async fn completion(&self) -> Result<(), Error> {
        self.state.completion().await
    }
}

impl AsyncWrite for ChannelWriteHalf {
    fn poll_write(
        mut self: Pin<&mut Self>,
        cx: &mut Context<'_>,
        buf: &[u8],
    ) -> Poll<io::Result<usize>> {
        Pin::new(
            self.io
                .as_mut()
                .expect("write half I/O is present until it is dropped"),
        )
        .poll_write(cx, buf)
    }

    fn poll_flush(mut self: Pin<&mut Self>, cx: &mut Context<'_>) -> Poll<io::Result<()>> {
        Pin::new(
            self.io
                .as_mut()
                .expect("write half I/O is present until it is dropped"),
        )
        .poll_flush(cx)
    }

    fn poll_shutdown(mut self: Pin<&mut Self>, cx: &mut Context<'_>) -> Poll<io::Result<()>> {
        Pin::new(
            self.io
                .as_mut()
                .expect("write half I/O is present until it is dropped"),
        )
        .poll_shutdown(cx)
    }
}

impl Drop for ChannelWriteHalf {
    fn drop(&mut self) {
        let Some(mut io) = self.io.take() else {
            return;
        };

        // Shutdown flushes the application write half. The outbound pump then
        // relays all buffered bytes before it observes EOF and emits
        // ContentWritingCompleted.
        if let Ok(runtime) = tokio::runtime::Handle::try_current() {
            runtime.spawn(async move {
                let _ = tokio::io::AsyncWriteExt::shutdown(&mut io).await;
            });
        }
    }
}
