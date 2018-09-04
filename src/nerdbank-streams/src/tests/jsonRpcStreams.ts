import * as rpc from "vscode-jsonrpc";
import { Channel } from "..";

export function startJsonRpc(channel: Channel): rpc.MessageConnection {
    return rpc.createMessageConnection(
        new rpc.StreamMessageReader(channel.stream),
        new rpc.StreamMessageWriter(channel.stream));
}
