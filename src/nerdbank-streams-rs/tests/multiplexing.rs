//! Behavioral tests for all supported MultiplexingStream protocol versions.

use std::{
    pin::Pin,
    sync::{
        atomic::{AtomicUsize, Ordering},
        Arc,
    },
    task::{Context, Poll},
    time::Duration,
};

use nerdbank_streams::mxstream::{ChannelOptions, MultiplexingStream, Options, ProtocolVersion};
use tokio::{
    io::{AsyncRead, AsyncReadExt, AsyncWrite, AsyncWriteExt, DuplexStream, ReadBuf},
    time::timeout,
};

fn options(version: ProtocolVersion) -> Options {
    Options {
        protocol_version: version,
        default_receive_window: 32,
        ..Options::default()
    }
}

async fn connected(
    version: ProtocolVersion,
) -> (
    MultiplexingStream<DuplexStream>,
    MultiplexingStream<DuplexStream>,
) {
    let (left, right) = tokio::io::duplex(1024 * 1024);
    if version == ProtocolVersion::V3 {
        (
            MultiplexingStream::create(left, options(version)).expect("left v3 stream"),
            MultiplexingStream::create(right, options(version)).expect("right v3 stream"),
        )
    } else {
        let (left, right) = tokio::join!(
            MultiplexingStream::connect(left, options(version)),
            MultiplexingStream::connect(right, options(version))
        );
        (left.expect("left stream"), right.expect("right stream"))
    }
}

async fn connected_with_options(
    options: Options,
) -> (
    MultiplexingStream<DuplexStream>,
    MultiplexingStream<DuplexStream>,
) {
    let (left, right) = tokio::io::duplex(1024 * 1024);
    if options.protocol_version == ProtocolVersion::V3 {
        (
            MultiplexingStream::create(left, options.clone()).expect("left v3 stream"),
            MultiplexingStream::create(right, options).expect("right v3 stream"),
        )
    } else {
        let (left, right) = tokio::join!(
            MultiplexingStream::connect(left, options.clone()),
            MultiplexingStream::connect(right, options)
        );
        (left.expect("left stream"), right.expect("right stream"))
    }
}

fn growth_options(
    version: ProtocolVersion,
    default_receive_window: usize,
    max_channel_receive_window: usize,
    max_total_channel_receive_window: usize,
) -> Options {
    Options {
        protocol_version: version,
        default_receive_window,
        max_channel_receive_window,
        max_total_channel_receive_window,
        ..Options::default()
    }
}

async fn open_limited_channel(
    left: &MultiplexingStream<DuplexStream>,
    right: &MultiplexingStream<DuplexStream>,
    receive_window: usize,
) -> (
    nerdbank_streams::mxstream::Channel,
    nerdbank_streams::mxstream::Channel,
) {
    let options = ChannelOptions {
        receive_window: Some(receive_window),
    };
    let (offered, accepted) = tokio::join!(
        left.offer_channel("limited", Some(options.clone())),
        right.accept_channel("limited", Some(options))
    );
    (
        offered.expect("limited offer accepted"),
        accepted.expect("limited channel accepted"),
    )
}

async fn shutdown_after_drop(
    left: MultiplexingStream<DuplexStream>,
    right: MultiplexingStream<DuplexStream>,
) {
    let (left_result, right_result) = tokio::join!(left.shutdown(), right.shutdown());
    left_result.expect("left shutdown");
    right_result.expect("right shutdown");
}

struct DroppingWindowFrames {
    inner: DuplexStream,
    dropped: Arc<AtomicUsize>,
}

impl AsyncRead for DroppingWindowFrames {
    fn poll_read(
        mut self: Pin<&mut Self>,
        context: &mut Context<'_>,
        buffer: &mut ReadBuf<'_>,
    ) -> Poll<std::io::Result<()>> {
        Pin::new(&mut self.inner).poll_read(context, buffer)
    }
}

impl AsyncWrite for DroppingWindowFrames {
    fn poll_write(
        mut self: Pin<&mut Self>,
        context: &mut Context<'_>,
        buffer: &[u8],
    ) -> Poll<std::io::Result<usize>> {
        if matches!(buffer, [0x92..=0x94, 7 | 8, ..]) {
            self.dropped.fetch_add(1, Ordering::Relaxed);
            return Poll::Ready(Ok(buffer.len()));
        }
        Pin::new(&mut self.inner).poll_write(context, buffer)
    }

    fn poll_flush(
        mut self: Pin<&mut Self>,
        context: &mut Context<'_>,
    ) -> Poll<std::io::Result<()>> {
        Pin::new(&mut self.inner).poll_flush(context)
    }

    fn poll_shutdown(
        mut self: Pin<&mut Self>,
        context: &mut Context<'_>,
    ) -> Poll<std::io::Result<()>> {
        Pin::new(&mut self.inner).poll_shutdown(context)
    }
}

async fn named_round_trip(version: ProtocolVersion) {
    let (left, right) = connected(version).await;
    let (offered, accepted) = tokio::join!(
        left.offer_channel("messages", None),
        right.accept_channel("messages", None)
    );
    let mut offered = offered.expect("offer accepted");
    let mut accepted = accepted.expect("channel accepted");

    offered.write_all(b"from left").await.expect("write left");
    offered.shutdown().await.expect("close left writer");
    let mut received = Vec::new();
    accepted
        .read_to_end(&mut received)
        .await
        .expect("read left content");
    assert_eq!(received, b"from left");

    accepted
        .write_all(b"from right")
        .await
        .expect("write right");
    accepted.shutdown().await.expect("close right writer");
    let mut reply = Vec::new();
    offered
        .read_to_end(&mut reply)
        .await
        .expect("read right content");
    assert_eq!(reply, b"from right");

    timeout(Duration::from_secs(1), async {
        tokio::try_join!(offered.completion(), accepted.completion())
    })
    .await
    .expect("both channels should complete before multiplexor shutdown")
    .expect("graceful channel completion");

    let (left_result, right_result) = tokio::join!(left.shutdown(), right.shutdown());
    left_result.expect("shutdown left");
    right_result.expect("shutdown right");
}

#[tokio::test]
async fn v1_named_channel_round_trip() {
    named_round_trip(ProtocolVersion::V1).await;
}

#[tokio::test]
async fn v2_named_channel_round_trip() {
    named_round_trip(ProtocolVersion::V2).await;
}

#[tokio::test]
async fn v3_named_channel_round_trip() {
    named_round_trip(ProtocolVersion::V3).await;
}

#[tokio::test]
async fn anonymous_channel_is_accepted_by_id() {
    let (left, right) = connected(ProtocolVersion::V3).await;
    let (left_signal, right_signal) = tokio::join!(
        left.offer_channel("signal", None),
        right.accept_channel("signal", None)
    );
    let mut left_signal = left_signal.expect("signal offer");
    let mut right_signal = right_signal.expect("signal acceptance");
    let mut offered = left.create_channel(None).expect("anonymous offer");
    left_signal
        .write_all(&offered.id().id.to_le_bytes())
        .await
        .expect("send anonymous channel ID");
    let mut id = [0; 8];
    right_signal
        .read_exact(&mut id)
        .await
        .expect("receive anonymous channel ID");
    let mut accepted = right
        .accept_channel_by_id(u64::from_le_bytes(id), None)
        .await
        .expect("anonymous acceptance");

    offered.write_all(b"anonymous").await.expect("write");
    offered.shutdown().await.expect("shutdown");
    let mut received = Vec::new();
    accepted.read_to_end(&mut received).await.expect("read");
    assert_eq!(received, b"anonymous");

    left.shutdown().await.expect("shutdown left");
    right.shutdown().await.expect("shutdown right");
}

#[tokio::test]
async fn v3_seeded_channel_has_no_id_collision() {
    let options = Options {
        protocol_version: ProtocolVersion::V3,
        default_receive_window: 32,
        seeded_channels: vec![ChannelOptions::default()],
        ..Options::default()
    };
    let (left_transport, right_transport) = tokio::io::duplex(1024 * 1024);
    let left = MultiplexingStream::create(left_transport, options.clone()).expect("left stream");
    let right = MultiplexingStream::create(right_transport, options).expect("right stream");
    let mut left_seeded = left.accept_seeded_channel(0).expect("left seeded");
    let mut right_seeded = right.accept_seeded_channel(0).expect("right seeded");
    left_seeded
        .write_all(b"seeded")
        .await
        .expect("write seeded");
    left_seeded.shutdown().await.expect("shutdown seeded");
    let mut seeded_data = Vec::new();
    right_seeded
        .read_to_end(&mut seeded_data)
        .await
        .expect("read seeded");
    assert_eq!(seeded_data, b"seeded");

    let anonymous = left.create_channel(None).expect("anonymous offer");
    assert_eq!(anonymous.id().id, 2);

    left.shutdown().await.expect("shutdown left");
    right.shutdown().await.expect("shutdown right");
}

#[tokio::test]
async fn flow_control_blocks_until_the_reader_drains() {
    let (left, right) = connected(ProtocolVersion::V2).await;
    let restricted = ChannelOptions {
        receive_window: Some(4),
    };
    let (offered, accepted) = tokio::join!(
        left.offer_channel("limited", Some(restricted.clone())),
        right.accept_channel("limited", Some(restricted))
    );
    let mut offered = offered.expect("offer accepted");
    let mut accepted = accepted.expect("channel accepted");

    let writer = tokio::spawn(async move {
        offered.write_all(b"abcdefgh").await.expect("write limited");
        offered
    });

    let mut first = [0; 4];
    accepted
        .read_exact(&mut first)
        .await
        .expect("read first window");
    assert_eq!(&first, b"abcd");
    let mut offered = timeout(Duration::from_secs(1), writer)
        .await
        .expect("writer resumed")
        .expect("writer task");
    offered.shutdown().await.expect("shutdown writer");

    let mut second = Vec::new();
    accepted.read_to_end(&mut second).await.expect("read rest");
    assert_eq!(second, b"efgh");
    left.shutdown().await.expect("shutdown left");
    right.shutdown().await.expect("shutdown right");
}

#[tokio::test]
async fn window_growth_increases_usable_send_credit() {
    let (left, right) =
        connected_with_options(growth_options(ProtocolVersion::V2, 4, 64, 60)).await;
    let (offered, mut accepted) = open_limited_channel(&left, &right, 4).await;

    let writer = tokio::spawn(async move {
        let mut offered = offered;
        offered
            .write_all(&[0_u8; 8])
            .await
            .expect("write throttled content");
        offered
    });

    let mut initial_window = [0_u8; 4];
    accepted
        .read_exact(&mut initial_window)
        .await
        .expect("drain original window");
    let mut offered = timeout(Duration::from_secs(1), writer)
        .await
        .expect("baseline credit should release sender")
        .expect("writer task");
    let mut fixed_window = [0_u8; 4];
    accepted
        .read_exact(&mut fixed_window)
        .await
        .expect("drain the final fixed-window content");

    offered
        .write_all(&[0_u8; 16])
        .await
        .expect("the grown window should carry a full 16-byte transfer");
    let mut grown_window = [0_u8; 16];
    accepted
        .read_exact(&mut grown_window)
        .await
        .expect("read content sent with grown credit");
    offered.shutdown().await.expect("complete writer");
    drop(offered);
    drop(accepted);
    shutdown_after_drop(left, right).await;
}

#[tokio::test]
async fn window_growth_is_deferred_until_reader_starves() {
    let (left, right) =
        connected_with_options(growth_options(ProtocolVersion::V3, 4, 64, 60)).await;
    let (offered, mut accepted) = open_limited_channel(&left, &right, 4).await;

    let writer = tokio::spawn(async move {
        let mut offered = offered;
        offered
            .write_all(&[0_u8; 8])
            .await
            .expect("write throttled content");
        offered
    });

    let mut initial_window = [0_u8; 4];
    accepted
        .read_exact(&mut initial_window)
        .await
        .expect("drain original window");
    let offered = timeout(Duration::from_secs(1), writer)
        .await
        .expect("baseline credit should release first transfer")
        .expect("writer task");

    let writer = tokio::spawn(async move {
        let mut offered = offered;
        offered
            .write_all(&[0_u8; 8])
            .await
            .expect("write second transfer");
        offered
    });

    let mut fixed_window = [0_u8; 4];
    accepted
        .read_exact(&mut fixed_window)
        .await
        .expect("drain the final buffered bytes");
    let mut second_transfer = [0_u8; 8];
    accepted
        .read_exact(&mut second_transfer)
        .await
        .expect("drain second fixed-window transfer");
    let mut offered = timeout(Duration::from_secs(1), writer)
        .await
        .expect("baseline credit should complete second transfer")
        .expect("writer task");
    offered
        .write_all(&[0_u8; 16])
        .await
        .expect("starved reader should permit growth");
    let mut grown_window = [0_u8; 16];
    accepted
        .read_exact(&mut grown_window)
        .await
        .expect("read content sent after growth");
    drop(offered);
    drop(accepted);
    shutdown_after_drop(left, right).await;
}

#[tokio::test]
async fn window_growth_is_capped_per_channel() {
    let (left, right) = connected_with_options(growth_options(ProtocolVersion::V2, 4, 8, 60)).await;
    let (offered, mut accepted) = open_limited_channel(&left, &right, 4).await;

    let first_writer = tokio::spawn(async move {
        let mut offered = offered;
        offered
            .write_all(&[0_u8; 8])
            .await
            .expect("write first transfer");
        offered
    });
    let mut initial_window = [0_u8; 4];
    accepted
        .read_exact(&mut initial_window)
        .await
        .expect("drain original window");
    let offered = timeout(Duration::from_secs(1), first_writer)
        .await
        .expect("baseline credit should release first transfer")
        .expect("writer task");
    let mut first_growth = [0_u8; 4];
    accepted
        .read_exact(&mut first_growth)
        .await
        .expect("drain final first-transfer content");

    let mut second_writer = tokio::spawn(async move {
        let mut offered = offered;
        offered
            .write_all(&[0_u8; 20])
            .await
            .expect("write second transfer");
        offered
    });
    let mut capped_window = [0_u8; 8];
    accepted
        .read_exact(&mut capped_window)
        .await
        .expect("drain capped window");
    assert!(
        timeout(Duration::from_millis(100), &mut second_writer)
            .await
            .is_err(),
        "a repeated-size adjustment must not grant more than the per-channel cap"
    );

    let mut remaining = [0_u8; 12];
    accepted
        .read_exact(&mut remaining)
        .await
        .expect("drain remaining fixed-window transfer");
    let offered = timeout(Duration::from_secs(1), second_writer)
        .await
        .expect("writer completes after normal credit is returned")
        .expect("writer task");
    drop(offered);
    drop(accepted);
    shutdown_after_drop(left, right).await;
}

#[tokio::test]
async fn window_does_not_grow_when_stream_budget_is_exhausted() {
    let (left, right) = connected_with_options(growth_options(ProtocolVersion::V3, 4, 64, 0)).await;
    let (offered, mut accepted) = open_limited_channel(&left, &right, 4).await;

    let mut writer = tokio::spawn(async move {
        let mut offered = offered;
        offered
            .write_all(&[0_u8; 12])
            .await
            .expect("write throttled content");
        offered
    });
    let mut original_window = [0_u8; 4];
    accepted
        .read_exact(&mut original_window)
        .await
        .expect("drain original window");
    assert!(
        timeout(Duration::from_millis(100), &mut writer)
            .await
            .is_err(),
        "a zero stream-wide budget must decline the request"
    );

    let mut remaining = [0_u8; 8];
    accepted
        .read_exact(&mut remaining)
        .await
        .expect("drain remaining fixed-window transfer");
    let offered = timeout(Duration::from_secs(1), writer)
        .await
        .expect("writer completes with baseline flow control")
        .expect("writer task");
    drop(offered);
    drop(accepted);
    shutdown_after_drop(left, right).await;
}

#[tokio::test]
async fn dropped_window_frames_fall_back_to_fixed_window_flow_control() {
    let dropped = Arc::new(AtomicUsize::new(0));
    let (left_transport, right_transport) = tokio::io::duplex(1024 * 1024);
    let options = growth_options(ProtocolVersion::V3, 4, 64, 60);
    let left = MultiplexingStream::create(
        DroppingWindowFrames {
            inner: left_transport,
            dropped: Arc::clone(&dropped),
        },
        options.clone(),
    )
    .expect("create left stream");
    let right = MultiplexingStream::create(
        DroppingWindowFrames {
            inner: right_transport,
            dropped: Arc::clone(&dropped),
        },
        options,
    )
    .expect("create right stream");
    let receive_options = ChannelOptions {
        receive_window: Some(4),
    };
    let (offered, accepted) = tokio::join!(
        left.offer_channel("limited", Some(receive_options.clone())),
        right.accept_channel("limited", Some(receive_options))
    );
    let offered = offered.expect("limited offer accepted");
    let mut accepted = accepted.expect("limited channel accepted");

    let writer = tokio::spawn(async move {
        let mut offered = offered;
        offered
            .write_all(&[0_u8; 20])
            .await
            .expect("write fixed-window transfer");
        offered
    });
    let mut received = [0_u8; 20];
    accepted
        .read_exact(&mut received)
        .await
        .expect("fixed-window transfer completes");
    let offered = timeout(Duration::from_secs(1), writer)
        .await
        .expect("writer completes with returned baseline credit")
        .expect("writer task");
    assert_ne!(
        dropped.load(Ordering::Relaxed),
        0,
        "the transfer must have exercised a dropped window frame"
    );

    drop(offered);
    drop(accepted);
    left.shutdown().await.expect("left shutdown");
    right.shutdown().await.expect("right shutdown");
}

#[tokio::test]
async fn v1_remains_unaffected_by_window_growth_options() {
    let (left, right) = connected_with_options(growth_options(ProtocolVersion::V1, 4, 64, 0)).await;
    let (offered, mut accepted) = open_limited_channel(&left, &right, 4).await;

    let mut offered = timeout(Duration::from_secs(1), async move {
        let mut offered = offered;
        offered
            .write_all(&[0_u8; 64])
            .await
            .expect("v1 write should not be flow controlled");
        offered
    })
    .await
    .expect("v1 must retain its no-flow-control behavior");
    let mut received = [0_u8; 64];
    accepted
        .read_exact(&mut received)
        .await
        .expect("read v1 content");
    offered.shutdown().await.expect("complete v1 writer");
    drop(offered);
    drop(accepted);
    shutdown_after_drop(left, right).await;
}

#[tokio::test]
async fn closing_a_reader_releases_a_blocked_writer() {
    let (left, right) = connected(ProtocolVersion::V3).await;
    let restricted = ChannelOptions {
        receive_window: Some(1),
    };
    let (offered, accepted) = tokio::join!(
        left.offer_channel("limited", Some(restricted.clone())),
        right.accept_channel("limited", Some(restricted))
    );
    let mut offered = offered.expect("offer accepted");
    let accepted = accepted.expect("channel accepted");
    let writer = tokio::spawn(async move {
        offered.write_all(b"abcdefgh").await.expect("write limited");
        offered
    });

    accepted.close_read().await.expect("close reader");
    let offered = timeout(Duration::from_secs(1), writer)
        .await
        .expect("writer released")
        .expect("writer task");
    drop(offered);

    left.shutdown().await.expect("shutdown left");
    right.shutdown().await.expect("shutdown right");
}

#[tokio::test]
async fn flow_control_on_one_channel_does_not_block_another_channel() {
    let (left, right) = connected(ProtocolVersion::V2).await;
    let restricted = ChannelOptions {
        receive_window: Some(1),
    };
    let (blocked_offer, blocked_accept) = tokio::join!(
        left.offer_channel("blocked", Some(restricted.clone())),
        right.accept_channel("blocked", Some(restricted))
    );
    let mut blocked_offer = blocked_offer.expect("blocked offer");
    let blocked_accept = blocked_accept.expect("blocked acceptance");
    let blocked_writer = tokio::spawn(async move {
        blocked_offer
            .write_all(b"abcdefgh")
            .await
            .expect("blocked write");
        blocked_offer
    });

    let (other_offer, other_accept) = tokio::join!(
        left.offer_channel("other", None),
        right.accept_channel("other", None)
    );
    let mut other_offer = other_offer.expect("other offer");
    let mut other_accept = other_accept.expect("other acceptance");
    other_offer.write_all(b"ready").await.expect("other write");
    other_offer.shutdown().await.expect("other shutdown");
    let mut received = Vec::new();
    other_accept
        .read_to_end(&mut received)
        .await
        .expect("other read");
    assert_eq!(received, b"ready");

    blocked_accept
        .close_read()
        .await
        .expect("release blocked writer");
    let blocked_offer = timeout(Duration::from_secs(1), blocked_writer)
        .await
        .expect("blocked writer released")
        .expect("blocked writer task");
    drop(blocked_offer);
    left.shutdown().await.expect("shutdown left");
    right.shutdown().await.expect("shutdown right");
}

#[tokio::test]
async fn transport_eof_faults_the_other_multiplexor() {
    let (left, right) = connected(ProtocolVersion::V3).await;
    right.shutdown().await.expect("shutdown remote endpoint");
    let error = timeout(Duration::from_secs(1), left.completion())
        .await
        .expect("completion")
        .expect_err("remote EOF should fault the multiplexor");
    assert!(error
        .to_string()
        .contains("transport reached end of stream"));
}

#[tokio::test]
async fn invalid_seeded_channel_configuration_is_rejected() {
    let (left, _) = tokio::io::duplex(32);
    let result = MultiplexingStream::create(
        left,
        Options {
            protocol_version: ProtocolVersion::V2,
            default_receive_window: 1,
            seeded_channels: vec![ChannelOptions::default()],
            ..Options::default()
        },
    );
    let error = match result {
        Ok(_) => panic!("v2 seeded channels are invalid"),
        Err(error) => error,
    };
    assert!(error
        .to_string()
        .contains("seeded channels require protocol v3"));
}

#[tokio::test]
async fn invalid_window_growth_configuration_is_rejected() {
    let (transport, _) = tokio::io::duplex(32);
    let error =
        match MultiplexingStream::create(transport, growth_options(ProtocolVersion::V3, 8, 4, 0)) {
            Ok(_) => panic!("maximum below default should be invalid"),
            Err(error) => error,
        };
    assert!(error
        .to_string()
        .contains("maximum channel receive window must be at least"));
}

#[tokio::test]
async fn graceful_completion_removes_channels_for_repeated_use() {
    let (left, right) = connected(ProtocolVersion::V3).await;

    for index in 0..4 {
        let name = format!("repeated-{index}");
        let (offered, accepted) = tokio::join!(
            left.offer_channel(name.clone(), None),
            right.accept_channel(name, None)
        );
        let mut offered = offered.expect("offer accepted");
        let mut accepted = accepted.expect("channel accepted");

        offered.write_all(b"request").await.expect("write request");
        offered.shutdown().await.expect("complete request");
        let mut request = Vec::new();
        accepted
            .read_to_end(&mut request)
            .await
            .expect("read request");
        assert_eq!(request, b"request");

        accepted
            .write_all(b"response")
            .await
            .expect("write response");
        accepted.shutdown().await.expect("complete response");
        let mut response = Vec::new();
        offered
            .read_to_end(&mut response)
            .await
            .expect("read response");
        assert_eq!(response, b"response");

        timeout(Duration::from_secs(1), async {
            tokio::try_join!(offered.completion(), accepted.completion())
        })
        .await
        .expect("channel completion")
        .expect("graceful channel result");
    }

    left.shutdown().await.expect("shutdown left");
    right.shutdown().await.expect("shutdown right");
}

#[tokio::test]
async fn termination_then_drop_does_not_emit_trailing_channel_frames() {
    let (left, right) = connected(ProtocolVersion::V3).await;
    let (offered, accepted) = tokio::join!(
        left.offer_channel("terminated", None),
        right.accept_channel("terminated", None)
    );
    let offered = offered.expect("offer accepted");
    let accepted = accepted.expect("channel accepted");

    offered
        .terminate(Some("intentional termination"))
        .await
        .expect("terminate channel");
    drop(offered);
    let error = timeout(Duration::from_secs(1), accepted.completion())
        .await
        .expect("remote channel termination")
        .expect_err("remote termination should fault the channel");
    assert!(error.to_string().contains("intentional termination"));
    drop(accepted);

    let (offered, accepted) = tokio::join!(
        left.offer_channel("still-usable", None),
        right.accept_channel("still-usable", None)
    );
    let mut offered = offered.expect("subsequent offer accepted");
    let accepted = accepted.expect("subsequent channel accepted");
    offered
        .write_all(b"still connected")
        .await
        .expect("write after terminated channel");
    offered.shutdown().await.expect("complete writer");
    drop(accepted);

    let (left_result, right_result) = tokio::join!(left.shutdown(), right.shutdown());
    left_result.expect("shutdown left");
    right_result.expect("shutdown right");
}

#[tokio::test]
async fn owned_split_half_drops_notify_the_peer() {
    let (left, right) = connected(ProtocolVersion::V3).await;
    let constrained = ChannelOptions {
        receive_window: Some(1),
    };
    let (offered, accepted) = tokio::join!(
        left.offer_channel("split", Some(constrained.clone())),
        right.accept_channel("split", Some(constrained))
    );
    let offered = offered.expect("offer accepted");
    let mut accepted = accepted.expect("channel accepted");
    let (read_half, write_half) = offered.into_split();

    drop(read_half);
    timeout(Duration::from_secs(1), accepted.write_all(b"discarded"))
        .await
        .expect("read-half drop should unblock the peer writer")
        .expect("write after peer reader completion");

    drop(write_half);
    let mut buffer = [0_u8; 1];
    assert_eq!(
        timeout(Duration::from_secs(1), accepted.read(&mut buffer))
            .await
            .expect("write-half drop should complete peer reader")
            .expect("read after peer writer completion"),
        0
    );
    accepted.shutdown().await.expect("complete peer writer");
    timeout(Duration::from_secs(1), accepted.completion())
        .await
        .expect("channel completion")
        .expect("graceful channel result");

    left.shutdown().await.expect("shutdown left");
    right.shutdown().await.expect("shutdown right");
}

#[tokio::test]
async fn dropping_a_channel_notifies_both_peer_directions() {
    let (left, right) = connected(ProtocolVersion::V3).await;
    let constrained = ChannelOptions {
        receive_window: Some(1),
    };
    let (offered, accepted) = tokio::join!(
        left.offer_channel("drop", Some(constrained.clone())),
        right.accept_channel("drop", Some(constrained))
    );
    let offered = offered.expect("offer accepted");
    let mut accepted = accepted.expect("channel accepted");

    drop(offered);
    let write_error = timeout(Duration::from_secs(1), accepted.write_all(b"discarded"))
        .await
        .expect("channel drop should unblock the peer writer")
        .expect_err("channel drop should close the peer writer");
    assert_eq!(write_error.kind(), std::io::ErrorKind::BrokenPipe);
    let mut buffer = [0_u8; 1];
    assert_eq!(
        timeout(Duration::from_secs(1), accepted.read(&mut buffer))
            .await
            .expect("channel drop should complete the peer reader")
            .expect("read after peer writer completion"),
        0
    );
    timeout(Duration::from_secs(1), accepted.completion())
        .await
        .expect("channel completion")
        .expect("graceful channel result");

    left.shutdown().await.expect("shutdown left");
    right.shutdown().await.expect("shutdown right");
}

#[tokio::test]
async fn protocol_failure_closes_the_transport_writer() {
    let (transport, mut peer) = tokio::io::duplex(64);
    let multiplexor =
        MultiplexingStream::create(transport, options(ProtocolVersion::V3)).expect("create mux");

    peer.write_all(&[0x91, 9])
        .await
        .expect("send unsupported control code");
    let error = timeout(Duration::from_secs(1), multiplexor.completion())
        .await
        .expect("multiplexor protocol failure")
        .expect_err("unsupported control code should fail the multiplexor");
    assert!(error.to_string().contains("unsupported"));

    let mut buffer = [0_u8; 1];
    assert_eq!(
        timeout(Duration::from_secs(1), peer.read(&mut buffer))
            .await
            .expect("writer should close after reader failure")
            .expect("read closed transport"),
        0
    );
}
