//! Behavioral tests for all supported MultiplexingStream protocol versions.

use std::time::Duration;

use nerdbank_streams::mxstream::{ChannelOptions, MultiplexingStream, Options, ProtocolVersion};
use tokio::{
    io::{AsyncReadExt, AsyncWriteExt, DuplexStream},
    time::{sleep, timeout},
};

fn options(version: ProtocolVersion) -> Options {
    Options {
        protocol_version: version,
        default_receive_window: 32,
        seeded_channels: Vec::new(),
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
    let mut offered = left.create_channel(None).expect("anonymous offer");
    sleep(Duration::from_millis(10)).await;
    let mut accepted = right
        .accept_channel_by_id(offered.id().id, None)
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
    sleep(Duration::from_millis(30)).await;
    assert!(
        !writer.is_finished(),
        "writer should wait for ContentProcessed credit"
    );

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

    sleep(Duration::from_millis(30)).await;
    assert!(!writer.is_finished(), "writer should be flow controlled");
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
    sleep(Duration::from_millis(30)).await;
    assert!(!blocked_writer.is_finished(), "first writer should block");

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
