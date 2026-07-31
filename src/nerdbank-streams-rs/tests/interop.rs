//! End-to-end tests against the repository's .NET stdio interop peer.

use std::{
    path::{Path, PathBuf},
    pin::Pin,
    task::{Context, Poll},
    time::Duration,
};

use nerdbank_streams::mxstream::{
    Channel, ChannelOptions, Error, MultiplexingStream, Options, ProtocolVersion,
};
use tokio::{
    io::{AsyncRead, AsyncReadExt, AsyncWrite, AsyncWriteExt, ReadBuf},
    process::{Child, ChildStdin, ChildStdout, Command},
    time::timeout,
};

struct ChildTransport {
    input: ChildStdout,
    output: ChildStdin,
}

impl AsyncRead for ChildTransport {
    fn poll_read(
        mut self: Pin<&mut Self>,
        context: &mut Context<'_>,
        buffer: &mut ReadBuf<'_>,
    ) -> Poll<std::io::Result<()>> {
        Pin::new(&mut self.input).poll_read(context, buffer)
    }
}

impl AsyncWrite for ChildTransport {
    fn poll_write(
        mut self: Pin<&mut Self>,
        context: &mut Context<'_>,
        buffer: &[u8],
    ) -> Poll<std::io::Result<usize>> {
        Pin::new(&mut self.output).poll_write(context, buffer)
    }

    fn poll_flush(
        mut self: Pin<&mut Self>,
        context: &mut Context<'_>,
    ) -> Poll<std::io::Result<()>> {
        Pin::new(&mut self.output).poll_flush(context)
    }

    fn poll_shutdown(
        mut self: Pin<&mut Self>,
        context: &mut Context<'_>,
    ) -> Poll<std::io::Result<()>> {
        Pin::new(&mut self.output).poll_shutdown(context)
    }
}

fn repository_root() -> PathBuf {
    Path::new(env!("CARGO_MANIFEST_DIR"))
        .ancestors()
        .nth(2)
        .expect("crate is under src/")
        .to_path_buf()
}

async fn peer_path() -> PathBuf {
    if let Some(path) = std::env::var_os("NERDBANK_STREAMS_INTEROP_PEER") {
        return PathBuf::from(path);
    }

    let root = repository_root();
    for configuration in ["Release", "Debug"] {
        let path = root.join(format!(
            "bin/Nerdbank.Streams.Interop.Tests/{configuration}/net8.0/Nerdbank.Streams.Interop.Tests.dll"
        ));
        if path.exists() {
            return path;
        }
    }

    let output = Command::new("pwsh")
        .args([
            "-NoProfile",
            "-Command",
            "& ./.github/Prime-ForCopilot.ps1; dotnet build test/Nerdbank.Streams.Interop.Tests -c Release",
        ])
        .current_dir(&root)
        .output()
        .await
        .expect("start PowerShell build");
    assert!(
        output.status.success(),
        "failed to build interop peer: {}",
        String::from_utf8_lossy(&output.stderr)
    );
    root.join(
        "bin/Nerdbank.Streams.Interop.Tests/Release/net8.0/Nerdbank.Streams.Interop.Tests.dll",
    )
}

async fn start_peer(
    version: ProtocolVersion,
) -> (Child, ChildTransport, tokio::task::JoinHandle<Vec<u8>>) {
    let path = peer_path().await;
    let mut child = Command::new("dotnet")
        .arg(path)
        .arg(match version {
            ProtocolVersion::V1 => "1",
            ProtocolVersion::V2 => "2",
            ProtocolVersion::V3 => "3",
        })
        .env("DOTNET_SKIP_FIRST_TIME_EXPERIENCE", "1")
        .stdin(std::process::Stdio::piped())
        .stdout(std::process::Stdio::piped())
        .stderr(std::process::Stdio::piped())
        .spawn()
        .expect("start interop peer");
    let stderr = child.stderr.take().expect("peer stderr");
    let stderr_task = tokio::spawn(async move {
        let mut stderr = stderr;
        let mut contents = Vec::new();
        stderr
            .read_to_end(&mut contents)
            .await
            .expect("read peer stderr");
        contents
    });
    let transport = ChildTransport {
        input: child.stdout.take().expect("peer stdout"),
        output: child.stdin.take().expect("peer stdin"),
    };
    (child, transport, stderr_task)
}

async fn read_line(channel: &mut Channel) -> String {
    let mut result = Vec::new();
    loop {
        let mut byte = [0_u8; 1];
        let bytes_read = channel.read(&mut byte).await.expect("read channel");
        assert_ne!(bytes_read, 0, "channel closed before a line was received");
        result.extend_from_slice(&byte[..bytes_read]);
        if byte[0] == b'\n' {
            return String::from_utf8(result).expect("UTF-8 line");
        }
    }
}

async fn interop(version: ProtocolVersion) {
    let (mut child, transport, stderr_task) = start_peer(version).await;
    let options = Options {
        protocol_version: version,
        default_receive_window: 16,
        seeded_channels: if version == ProtocolVersion::V3 {
            vec![ChannelOptions::default()]
        } else {
            Vec::new()
        },
        ..Options::default()
    };
    let multiplexor = if version == ProtocolVersion::V3 {
        MultiplexingStream::create(transport, options).expect("create v3 multiplexor")
    } else {
        MultiplexingStream::connect(transport, options)
            .await
            .expect("connect multiplexor")
    };
    let mut client_offer = multiplexor
        .offer_channel(
            "clientOffer",
            Some(ChannelOptions {
                receive_window: Some(16),
            }),
        )
        .await
        .expect("client offer accepted");
    // Both the Rust and .NET peers start with small receive windows here, so
    // this transfer exercises additive growth frames in each direction on v2
    // and v3 while retaining baseline behavior on v1.
    let large_line = "ABCDEF".repeat(512) + "\n";
    client_offer
        .write_all(large_line.as_bytes())
        .await
        .expect("write constrained transfer");
    assert_eq!(
        read_line(&mut client_offer).await,
        format!("recv: {large_line}")
    );

    let error_channel = multiplexor
        .offer_channel("clientErrorOffer", None)
        .await
        .expect("error offer accepted");
    let mut error_communication = multiplexor
        .offer_channel("clientErrorOfferComm", None)
        .await
        .expect("communication offer accepted");
    error_channel
        .terminate(Some("Error: Hello world"))
        .await
        .expect("terminate error channel");
    let response = read_line(&mut error_communication).await;
    if version == ProtocolVersion::V1 {
        assert_eq!(response, "Completed with no error\n");
    } else {
        assert!(response.contains("Received error from remote side: Error: Hello world"));
    }

    let mut server_offer = multiplexor
        .accept_channel("serverOffer", None)
        .await
        .expect("accept server offer");
    assert_eq!(read_line(&mut server_offer).await, "theserver\n");
    server_offer
        .write_all(b"recv: theserver\n")
        .await
        .expect("answer server offer");
    server_offer
        .shutdown()
        .await
        .expect("close server response");
    timeout(Duration::from_secs(1), server_offer.completion())
        .await
        .expect("server-offered channel completion")
        .expect("server-offered channel graceful result");

    let server_error = multiplexor
        .accept_channel("serverErrorOffer", None)
        .await
        .expect("accept server error offer");
    match server_error.completion().await {
        Ok(()) if version == ProtocolVersion::V1 => {}
        Err(Error::Remote(message)) if version != ProtocolVersion::V1 => {
            assert!(message.contains("Exception: Hello World"));
        }
        result => panic!("unexpected server error completion: {result:?}"),
    }

    if version == ProtocolVersion::V3 {
        let mut seeded = multiplexor
            .accept_seeded_channel(0)
            .expect("accept seeded channel");
        seeded
            .write_all(b"theclient\n")
            .await
            .expect("write seeded");
        assert_eq!(read_line(&mut seeded).await, "recv: theclient\n");
    }

    multiplexor.shutdown().await.expect("shutdown multiplexor");
    let exit = timeout(Duration::from_secs(10), child.wait())
        .await
        .expect("peer exit timeout")
        .expect("wait for peer");
    let stderr = stderr_task.await.expect("stderr task");
    assert!(
        exit.success(),
        "interop peer failed ({exit:?}): {}",
        String::from_utf8_lossy(&stderr)
    );
}

#[tokio::test]
async fn v1_dotnet_interop() {
    interop(ProtocolVersion::V1).await;
}

#[tokio::test]
async fn v2_dotnet_interop() {
    interop(ProtocolVersion::V2).await;
}

#[tokio::test]
async fn v3_dotnet_interop() {
    interop(ProtocolVersion::V3).await;
}
